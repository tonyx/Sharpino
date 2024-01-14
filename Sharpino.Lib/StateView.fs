namespace Sharpino
open FSharpPlus.Data

open FSharp.Core
open FSharpPlus

open Sharpino.Core
open Sharpino.Storage
open Sharpino.Cache
open Sharpino.Utils
open Sharpino.Definitions

open FsToolkit.ErrorHandling
open log4net
open log4net.Config
open System

module StateView =
    let serializer = new Utils.JsonSerializer(Utils.serSettings) :> Utils.ISerializer
    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)

    let inline private tryGetSnapshotByIdAndDeserialize<'A
        when 'A: (static member Zero: 'A) 
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (member Serialize: ISerializer -> string)
        and 'A: (static member Deserialize: ISerializer -> Json -> Result<'A, string>)
        >
        (id: int)
        (storage: IEventStore) =
            let snapshot = storage.TryGetSnapshotById 'A.Version 'A.StorageName id
            match snapshot |>> snd with
            | Some snapshot' ->
                let deserSnapshot = 'A.Deserialize (serializer, snapshot')
                let eventid = snapshot |>> fst
                match deserSnapshot with
                | Ok deserSnapshot -> (eventid.Value, deserSnapshot) |> Ok
                | Error e -> 
                    log.Error (sprintf "deserialization error %A for snapshot %s" e snapshot')
                    printf "deserialization error %A for snapshot %s" e snapshot'
                    Error (sprintf "deserialization error %A for snapshot %s" e snapshot')
            | None ->
                (0, 'A.Zero) |> Ok

    let inline private tryGetSnapshotByIdAndDeserializeRefactored<'A 
        when 'A :> Aggregate
        and 'A: (static member Deserialize: ISerializer -> Json -> Result<'A, string>)
        >
        (id: int)
        (version: string)
        (storageName: string)
        (storage: IEventStore) 
        (zero: 'A)
        =
            let snapshot = storage.TryGetSnapshotById version storageName id
            match snapshot |>> snd with
            | Some snapshot' ->
                let deserSnapshot = 'A.Deserialize (serializer, snapshot')
                let eventid = snapshot |>> fst
                match deserSnapshot with
                | Ok deserSnapshot -> (eventid.Value, deserSnapshot) |> Ok
                | Error e -> 
                    log.Error (sprintf "deserialization error %A for snapshot %s" e snapshot')
                    printf "deserialization error %A for snapshot %s" e snapshot'
                    Error (sprintf "deserialization error %A for snapshot %s" e snapshot')
            | None ->
                (0, zero) |> Ok

    let inline private getLastSnapshotOrStateCache<'A 
        when 'A: (static member Zero: 'A) 
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (static member Lock: obj)
        and 'A: (member Serialize: ISerializer -> string)
        and 'A: (static member Deserialize: ISerializer -> Json -> Result<'A, string>)
        >
        (storage: IEventStore) =
            log.Debug "getLastSnapshot"
            async {
                return
                    result {
                        let lastCacheEventId = Cache.StateCache<'A>.Instance.LastEventId() |> Option.defaultValue 0
                        let (snapshotEventId, lastSnapshotId) = storage.TryGetLastSnapshotId 'A.Version 'A.StorageName |> Option.defaultValue (0, 0)
                        if (lastSnapshotId = 0 && lastCacheEventId = 0) then
                            return (0, 'A.Zero)
                        else
                            if lastCacheEventId >= snapshotEventId then
                                let! state = 
                                    Cache.StateCache<'A>.Instance.GestState lastCacheEventId
                                return (lastCacheEventId, state)
                            else
                                let! (eventId, snapshot) = 
                                    tryGetSnapshotByIdAndDeserialize<'A> lastSnapshotId storage
                                return (eventId, snapshot)
                    }
            }
            |> Async.RunSynchronously

    let inline private getLastSnapshotOrStateCacheRefactored<'A 
        when 'A :> Aggregate
        and 'A: (static member Deserialize: ISerializer -> Json -> Result<'A, string>)
        >
        (aggregateId: Guid)
        (version: string)
        (storageName: string)
        (storage: IEventStore) 
        (zero: 'A)
        =
            log.Debug "getLastSnapshot"
            async {
                return
                    result {
                        let lastCacheEventId = Cache.StateCacheRefactored<'A>.Instance.LastEventId(aggregateId) |> Option.defaultValue 0
                        let (snapshotEventId, lastSnapshotId) = storage.TryGetLastSnapshotIdByAggregateId version storageName aggregateId |> Option.defaultValue (0, 0)
                        if (lastSnapshotId = 0 && lastCacheEventId = 0) then
                            return (0, zero)
                        else
                            if lastCacheEventId >= snapshotEventId then
                                let! state = 
                                    Cache.StateCacheRefactored<'A>.Instance.GestState (lastCacheEventId, aggregateId)
                                return (lastCacheEventId, state)
                            else
                                let! (eventId, snapshot) = 
                                    tryGetSnapshotByIdAndDeserializeRefactored<'A> lastSnapshotId version storageName storage zero
                                return (eventId, snapshot)
                    }
            }
            |> Async.RunSynchronously

    let inline private snapEventIdStateAndEvents<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (static member Lock: obj)
        and 'E :> Event<'A>
        and 'A: (member Serialize: ISerializer -> string)
        and 'A: (static member Deserialize: ISerializer -> Json -> Result<'A, string>)
        and 'E: (static member Deserialize: ISerializer -> Json -> Result<'E, string>)
        and 'E: (member Serialize: ISerializer -> string)
        >
        (storage: IEventStore) = 
        log.Debug "snapIdStateAndEvents"
        async {
            return
                result {
                    let! (eventId, state) = getLastSnapshotOrStateCache<'A> storage
                    let! events = storage.GetEventsAfterId 'A.Version eventId 'A.StorageName
                    let result =
                        (eventId, state, events)
                    return result
                }
        }
        |> Async.RunSynchronously

    let inline  snapEventIdStateAndEventsRefactored<'A, 'E
        when 'A :> Aggregate and 'E :> Event<'A>
        and 'A: (static member Deserialize: ISerializer -> Json -> Result<'A, string>)
        >
        (h: 'A)
        (storage: IEventStore)
        (zero: 'A)
        = 
        log.Debug "snapIdStateAndEventsRefactored"
        async {
            return
                result {
                    let! (eventId, state) = getLastSnapshotOrStateCacheRefactored<'A> h.Id h.Version h.StorageName  storage zero 
                    let! events = storage.GetEventsAfterIdRefactored h.Version h.StorageName h.Id eventId
                    let result =
                        (eventId, state, events)
                    return result
                }
        }
        |> Async.RunSynchronously
                
    let inline getFreshState<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (static member Lock: obj)
        and 'A: (static member Deserialize: ISerializer -> Json -> Result<'A, string>)
        and 'A: (member Serialize: ISerializer -> Json)
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: ISerializer -> Json -> Result<'E, string>)
        and 'E: (member Serialize: ISerializer -> string)
        >
        (storage: IEventStore): Result<EventId * 'A * Option<KafkaOffset> * Option<KafkaPartitionId>, string> = 
            log.Debug "getState"
            let computeNewState =
                fun () ->
                    result {
                        let! (_, state, events) = snapEventIdStateAndEvents<'A, 'E> storage
                        let! deserEvents =
                            events 
                            |>> snd 
                            |> catchErrors (fun x -> 'E.Deserialize (serializer, x))
                        let! newState = 
                            deserEvents |> evolve<'A, 'E> state
                        return newState
                    }
            let (lastEventId, kafkaOffSet, kafkaPartition) = storage.TryGetLastEventIdWithKafkaOffSet 'A.Version 'A.StorageName |> Option.defaultValue (0, None, None)
            let state = StateCache<'A>.Instance.Memoize computeNewState lastEventId
            match state with
            | Ok state -> 
                (lastEventId, state, kafkaOffSet, kafkaPartition) |> Ok
            | Error e -> 
                log.Error (sprintf "getState: %s" e)
                Error e

    let inline getFreshStateRefactored<'A, 'E
        when 'A :> Aggregate and 'E :> Event<'A>
        and 'A: (static member Deserialize: ISerializer -> Json -> Result<'A, string>)
        and 'E: (static member Deserialize: ISerializer -> Json -> Result<'E, string>)
        >
        (h: 'A)
        (storage: IEventStore)
        (zero: 'A)
        : Result<EventId * 'A * Option<KafkaOffset> * Option<KafkaPartitionId>, string> = 
            log.Debug "getStateRefactored"
            let computeNewState =
                fun () ->
                    result {
                        let! (_, state, events) = snapEventIdStateAndEventsRefactored<'A, 'E> h storage zero
                        let! deserEvents =
                            events 
                            |>> snd 
                            |> catchErrors (fun x -> 'E.Deserialize (serializer, x))
                        let! newState = 
                            deserEvents |> evolve<'A, 'E> state
                        return newState
                    }
            let (lastEventId, kafkaOffSet, kafkaPartition) = storage.TryGetLastEventIdByAggregateIdWithKafkaOffSet h.Version h.StorageName h.Id |> Option.defaultValue (0, None, None)
            let state = StateCacheRefactored<'A>.Instance.Memoize computeNewState (lastEventId, h.Id)
            match state with
            | Ok state -> 
                (lastEventId, state, kafkaOffSet, kafkaPartition) |> Ok
            | Error e -> 
                log.Error (sprintf "getStateRefactored: %s" e)
                Error e