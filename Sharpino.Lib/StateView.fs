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
    let serializer = Utils.JsonSerializer(Utils.serSettings) :> Utils.ISerializer
    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)

    let inline private tryGetSnapshotByIdAndDeserialize<'A, 'F
        when 'A: (static member Zero: 'A) 
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (member Serialize: 'F)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        >
        (id: int)
        (storage: IEventStore<'F>) =
            let snapshot = storage.TryGetSnapshotById 'A.Version 'A.StorageName id
            match snapshot |>> snd with
            | Some snapshot' ->
                let deserSnapshot = 'A.Deserialize  snapshot'
                let eventid = snapshot |>> fst
                match deserSnapshot with
                | Ok deserSnapshot -> (eventid.Value, deserSnapshot) |> Ok
                | Error e -> 
                    log.Error (sprintf "deserialization error %A for snapshot %A" e snapshot')
                    Error (sprintf "deserialization error %A for snapshot %A" e snapshot')
            | None ->
                (0, 'A.Zero) |> Ok

    let inline private tryGetAggregateSnapshot<'A, 'F
        when 'A :> Aggregate<'F> and
        'A: (static member Deserialize: 'F -> Result<'A, string>)
        >
        (aggregateId: Guid)
        (id: int)
        (version: string)
        (storageName: string)
        (storage: IEventStore<'F>) 
        =
            let snapshot = storage.TryGetAggregateSnapshotById version storageName aggregateId id
            match snapshot |>> snd with
            | Some snapshot' ->
                let deserSnapshot = 'A.Deserialize  snapshot'
                let eventId = snapshot |>> fst
                match deserSnapshot, eventId with
                | Ok deserSnapshot, Some evId  ->
                    (evId, deserSnapshot) |> Ok
                | Error e, _-> 
                    log.Error (sprintf "deserialization error %A for snapshot %A" e snapshot')
                    Error (sprintf "deserialization error %A for snapshot %A" e snapshot')
                | _ -> 
                    log.Error (sprintf "deserialization error for snapshot %A" snapshot')
                    Error (sprintf "deserialization error for snapshot %A" snapshot')
            | None ->
                Error "not found"

    let inline private getLastSnapshotOrStateCache<'A, 'F
        when 'A: (static member Zero: 'A) 
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (member Serialize: 'F)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        >
        (storage: IEventStore<'F>) =
            log.Debug (sprintf "getLastSnapshotOrStateCache %s - %s" 'A.Version 'A.StorageName)
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
                                    tryGetSnapshotByIdAndDeserialize<'A, 'F> lastSnapshotId storage
                                return (eventId, snapshot)
                    }
            }
            |> Async.RunSynchronously

    let inline private getLastAggregateSnapshotOrStateCache<'A, 'F 
        when 'A :> Aggregate<'F> and
        'A: (static member Deserialize: 'F -> Result<'A, string>)
        >
        (aggregateId: Guid)
        (version: string)
        (storageName: string)
        (storage: IEventStore<'F>) 
        =
            log.Debug (sprintf "getLastAggregateSnapshotOrStateCache %A - %s - %s" aggregateId version storageName)
            async {
                return
                    result {
                        let lastCacheEventId = Cache.AggregateCache<'A, 'F>.Instance.LastEventId(aggregateId) |> Option.defaultValue 0
                        let (snapshotEventId, lastSnapshotId) = storage.TryGetLastSnapshotIdByAggregateId version storageName aggregateId |> Option.defaultValue (None, 0)
                        if (lastSnapshotId = 0 && lastCacheEventId = 0) then
                            return None
                        else
                            if 
                                snapshotEventId.IsSome && lastCacheEventId >= snapshotEventId.Value then
                                let! state = 
                                    Cache.AggregateCache<'A, 'F>.Instance.GetState (lastCacheEventId, aggregateId)
                                return (lastCacheEventId |> Some, state) |> Some 
                            else
                                let! (eventId, snapshot) = 
                                    tryGetAggregateSnapshot<'A, 'F > aggregateId lastSnapshotId version storageName storage 
                                return (eventId , snapshot) |> Some 
                    }
            }
            |> Async.RunSynchronously

    let inline snapEventIdStateAndEvents<'A, 'E, 'F
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'E :> Event<'A>
        and 'A: (member Serialize:  'F)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'E: (member Serialize: 'F)
        >
        (storage: IEventStore<'F>) = 
        log.Debug (sprintf "snapIdStateAndEvents %s - %s" 'A.Version 'A.StorageName)
        async {
            return
                result {
                    let! (eventId, state: 'A) = getLastSnapshotOrStateCache<'A, 'F> storage
                    let! events = storage.GetEventsAfterId 'A.Version eventId 'A.StorageName
                    let res =
                        (eventId, state, events)
                    return res
                }
        }
        |> Async.RunSynchronously

    let inline snapAggregateEventIdStateAndEvents<'A, 'E, 'F
        when 'A :> Aggregate<'F> and 'E :> Event<'A> and
        'A: (static member Deserialize: 'F -> Result<'A, string>) and
        'A: (static member StorageName: string) and
        'A: (static member Version: string)>
        (id: Guid)
        (storage: IEventStore<'F>)
        = 
        log.Debug (sprintf "snapAggregateEventIdStateAndEvents %A - %s - %s" id 'A.Version 'A.StorageName)
        async {
            return
                result {
                    let! eventIdAndState = getLastAggregateSnapshotOrStateCache<'A, 'F> id 'A.Version 'A.StorageName storage
                    match eventIdAndState with
                    | None -> 
                        return! Error (sprintf "There is no aggregate of version %A, name %A with id %A" 'A.Version 'A.StorageName id)
                    | Some (Some eventId, state) ->
                        let! events = storage.GetAggregateEventsAfterId 'A.Version 'A.StorageName id eventId
                        let result =
                            (eventId |> Some, state, events)
                        return result
                    | Some (None, state) ->
                        let! events = storage.GetAggregateEvents 'A.Version 'A.StorageName id 
                        let result =
                            (None, state, events)
                        return result
                }
        }
        |> Async.RunSynchronously
        
    let inline getFreshState<'A, 'E, 'F
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'A: (member Serialize: 'F)
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'E: (member Serialize: 'F)
        >
        // (storage: IEventStore<'F>): Result<EventId * 'A * Option<KafkaOffset> * Option<KafkaPartitionId>, string> = 
        (storage: IEventStore<'F>) =
            log.Debug (sprintf "getFreshState %s - %s" 'A.Version 'A.StorageName)
            let computeNewState =
                fun () ->
                    result {
                        let! (_, state, events) = snapEventIdStateAndEvents<'A, 'E, 'F> storage
                        let! deserEvents =
                            events 
                            |>> snd 
                            |> List.traverseResultM (fun x -> 'E.Deserialize x)
                        let! newState = 
                            deserEvents |> evolve<'A, 'E> state
                        return newState
                    }

            // remove kafka info always
            // let (lastEventId, _, _) = storage.TryGetLastEventIdWithKafkaOffSet 'A.Version 'A.StorageName |> Option.defaultValue (0, None, None)
            let lastEventId = storage.TryGetLastEventId 'A.Version 'A.StorageName |> Option.defaultValue 0
            let state = StateCache<'A>.Instance.Memoize computeNewState lastEventId
            match state with
            | Ok state' -> 
                // (lastEventId, state', None, None) |> Ok
                (lastEventId, state') |> Ok
            | Error e -> 
                log.Error (sprintf "getState: %s" e)
                Error e

    let inline getAggregateFreshState<'A, 'E, 'F
        when 'A :> Aggregate<'F> and 'E :> Event<'A>
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        >
        (id: Guid)
        (storage: IEventStore<'F>)
        =
            log.Debug (sprintf "getAggregateFreshState %A - %s - %s" id 'A.Version 'A.StorageName)
            let computeNewState =
                fun () ->
                    result { 
                        let! (_, state, events) = snapAggregateEventIdStateAndEvents<'A, 'E, 'F> id storage
                        let! deserEvents =
                            events 
                            |>> snd 
                            |> List.traverseResultM (fun x -> 'E.Deserialize x)
                        let! newState = 
                            deserEvents |> evolve<'A, 'E> state
                        return newState
                    }
            let lastEventId = storage.TryGetLastAggregateEventId 'A.Version 'A.StorageName  id |> Option.defaultValue 0
            let state = AggregateCache<'A, 'F>.Instance.Memoize computeNewState (lastEventId, id)
            match state with
            | Ok state -> 
                (lastEventId, state) |> Ok
            | Error e -> 
                log.Error (sprintf "getAggregateFreshState: %s" e)
                Error e