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
        when 'A :> Aggregate and
        'A: (static member Deserialize: ISerializer -> Json -> Result<'A, string>)
        >
        (id: int)
        (version: string)
        (storageName: string)
        (storage: IEventStore) 
        =
            printf "tryGetSnapshotByIdAndDeserializeRefactored - 100\n"
            // let snapshot = storage.TryGetSnapshotById version storageName id
            // let snapshot = storage.TryGetSnapshotById version storageName id
            let snapshot = storage.TryGetAggregateSnapshotById version storageName id

            printf "ZZZZZZZZ: %A \n" snapshot

            printf "tryGetSnapshotByIdAndDeserializeRefactored - 200\n"
            match snapshot |>> snd with
            | Some snapshot' ->
                printf "HHHHHHHHH: %A\n" snapshot'
                let deserSnapshot = 'A.Deserialize (serializer, snapshot')
                let eventid = snapshot |>> fst
                match deserSnapshot with
                | Ok deserSnapshot -> 
                    printf "tryGetSnapshotByIdAndDeserializeRefactored - 300\n"
                    printf "QQQQQQQ. snapshot %A" snapshot'
                    printf "QQQQQQQ. ID. ++++. deserSnapshotId %A" deserSnapshot.Id
                    (eventid.Value, deserSnapshot) |> Ok
                | Error e -> 
                    log.Error (sprintf "deserialization error %A for snapshot %s" e snapshot')
                    printf "deserialization error %A for snapshot %s" e snapshot'
                    Error (sprintf "deserialization error %A for snapshot %s" e snapshot')
            | None ->
                Error "not found"
                // (0, 'A.Zero) |> Ok

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

    // let inline private getLastSnapshotOrStateCacheRefactored<'A 
    //     when 'A :> Aggregate and
    //     'A: (static member Deserialize: ISerializer -> Json -> Result<'A, string>)  and
    //     'A: (static member Zero: 'A)
    //     >
    //     (aggregateId: Guid)
    //     (version: string)
    //     (storageName: string)
    //     (storage: IEventStore) 
    //     =
    //         log.Debug "getLastSnapshot"
    //         async {
    //             return
    //                 result {
    //                     let lastCacheEventId = Cache.StateCacheRefactored<'A>.Instance.LastEventId(aggregateId) |> Option.defaultValue 0
    //                     let (snapshotEventId, lastSnapshotId) = storage.TryGetLastSnapshotIdByAggregateId version storageName aggregateId |> Option.defaultValue (None, 0)
    //                     if (lastSnapshotId = 0 && lastCacheEventId = 0) then
    //                         return (0, 'A.Zero)
    //                     else
    //                         if 
    //                             snapshotEventId.IsSome && lastCacheEventId >= snapshotEventId.Value then
    //                             let! state = 
    //                                 Cache.StateCacheRefactored<'A>.Instance.GestState (lastCacheEventId, aggregateId)
    //                             return (lastCacheEventId, state) 
    //                         else
    //                             let! (eventId, snapshot) = 
    //                                 tryGetSnapshotByIdAndDeserializeRefactored<'A> lastSnapshotId version storageName storage 
    //                             return (eventId, snapshot) 
    //                 }
    //         }
    //         |> Async.RunSynchronously

    let inline private getLastSnapshotOrStateCacheRefactoredRefactored<'A 
        when 'A :> Aggregate and
        'A: (static member Deserialize: ISerializer -> Json -> Result<'A, string>)
        >
        (aggregateId: Guid)
        (version: string)
        (storageName: string)
        (storage: IEventStore) 
        =
            log.Debug "getLastSnapshot"
            async {
                return
                    result {
                        printf "getLastSnapshotOrStateCacheRefactoredRefactored = 100\n"
                        let lastCacheEventId = Cache.StateCacheRefactored<'A>.Instance.LastEventId(aggregateId) |> Option.defaultValue 0
                        printf "getLastSnapshotOrStateCacheRefactoredRefactored = 200\n"
                        let (snapshotEventId, lastSnapshotId) = storage.TryGetLastSnapshotIdByAggregateId version storageName aggregateId |> Option.defaultValue (None, 0)
                        printf "getLastSnapshotOrStateCacheRefactoredRefactored = 300\n"
                        if (lastSnapshotId = 0 && lastCacheEventId = 0) then
                            return None
                        else
                            if 
                                snapshotEventId.IsSome && lastCacheEventId >= snapshotEventId.Value then
                                let! state = 
                                    Cache.StateCacheRefactored<'A>.Instance.GestState (lastCacheEventId, aggregateId)
                                return (lastCacheEventId |> Some, state) |> Some 
                            else
                                let! (eventId, snapshot) = 
                                    printf "getLastSnapshotOrStateCacheRefactoredRefactored = 400\n"
                                    tryGetSnapshotByIdAndDeserializeRefactored<'A> lastSnapshotId version storageName storage 

                                printf "YYYYYYYYY. snapshotId: %A\n" (snapshot.Id)
                                printf "YYYYYYYYY. snapshot %A\n" (snapshot.Serialize serializer)
                                return (eventId , snapshot) |> Some 
                    }
            }
            |> Async.RunSynchronously

    let inline snapEventIdStateAndEvents<'A, 'E
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

    // let inline snapEventIdStateAndEventsRefactored<'A, 'E
    //     when 'A :> Aggregate and 'E :> Event<'A> and
    //     'A: (static member Deserialize: ISerializer -> Json -> Result<'A, string>) and
    //     'A: (static member Zero: 'A) and
    //     'A: (static member StorageName: string) and
    //     'A: (static member Version: string) 
    //     >
    //     (id: Guid)
    //     (storage: IEventStore)
    //     = 
    //     log.Debug "snapIdStateAndEventsRefactored"
    //     async {
    //         return
    //             result {
    //                 let! (eventId, state) = getLastSnapshotOrStateCacheRefactored<'A> id 'A.Version 'A.StorageName storage
    //                 let! events = storage.GetEventsAfterIdRefactored 'A.Version 'A.StorageName id eventId
    //                 let result =
    //                     (eventId, state, events)
    //                 return result
    //             }
    //     }
    //     |> Async.RunSynchronously

    let inline snapEventIdStateAndEventsRefactoredRefactored<'A, 'E
        when 'A :> Aggregate and 'E :> Event<'A> and
        'A: (static member Deserialize: ISerializer -> Json -> Result<'A, string>) and
        'A: (static member StorageName: string) and
        'A: (static member Version: string)>
        (id: Guid)
        (storage: IEventStore)
        = 
        log.Debug "snapIdStateAndEventsRefactored"
        async {
            return
                result {
                    printf "snapEventIdStateAndEventsRefactoredRefactored - 100 \n"
                    let! eventIdAndState = getLastSnapshotOrStateCacheRefactoredRefactored<'A> id 'A.Version 'A.StorageName storage
                    printf "snapEventIdStateAndEventsRefactoredRefactored - 200 \n"
                    match eventIdAndState with
                    | None -> 
                        return! Error (sprintf "There is no aggregate of version %A, name %A with id %A" 'A.Version 'A.StorageName id)
                    | Some (eventId, state)  when eventId.IsSome ->
                        printf "snapEventIdStateAndEventsRefactoredRefactored - 300 \n"
                        let! events = storage.GetEventsAfterIdRefactored 'A.Version 'A.StorageName id eventId.Value
                        let result =
                            (eventId, state, events)
                        printf "snapEventIdStateAndEventsRefactoredRefactored - 400 \n"
                        return result
                    | Some (eventId, state) when eventId.IsNone ->
                        printf "snapEventIdStateAndEventsRefactoredRefactored - 500 \n"
                        printf "\n\nGGGGGGG, state %A\n\n" (state.Serialize serializer)
                        let! events = storage.GetEventsAfterNoneRefactored 'A.Version 'A.StorageName id 
                        let result =
                            (eventId, state, events)
                        printf "snapEventIdStateAndEventsRefactoredRefactored - 600 \n"
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
            | Ok state' -> 
                (lastEventId, state', kafkaOffSet, kafkaPartition) |> Ok
            | Error e -> 
                log.Error (sprintf "getState: %s" e)
                Error e

    let inline getFreshStateRefactored<'A, 'E
        when 'A :> Aggregate and 'E :> Event<'A>
        and 'A: (static member Deserialize: ISerializer -> Json -> Result<'A, string>)
        and 'E: (static member Deserialize: ISerializer -> Json -> Result<'E, string>)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        >
        (id: Guid)
        (storage: IEventStore)
        : Result<EventId * 'A * Option<KafkaOffset> * Option<KafkaPartitionId>, string> = 
            log.Debug "getStateRefactored"
            let computeNewState =
                fun () ->
                    result { 
                        printf "getFreshStateRefactored - 100\n"
                        let! (_, state, events) = snapEventIdStateAndEventsRefactoredRefactored<'A, 'E> id storage // zero
                        printf "XXXXX. state %A" (state.Serialize serializer)
                        printf "getFreshStateRefactored - 200\n"
                        let! deserEvents =
                            events 
                            |>> snd 
                            |> catchErrors (fun x -> 'E.Deserialize (serializer, x))
                        let! newState = 
                            deserEvents |> evolve<'A, 'E> state
                        printf "getFreshStateRefactored - 300\n"
                        return newState
                    }
            let (lastEventId, kafkaOffSet, kafkaPartition) = storage.TryGetLastEventIdByAggregateIdWithKafkaOffSet 'A.Version 'A.StorageName  id |> Option.defaultValue (0, None, None)
            let state = StateCacheRefactored<'A>.Instance.Memoize computeNewState (lastEventId, id)
            match state with
            | Ok state -> 
                (lastEventId, state, kafkaOffSet, kafkaPartition) |> Ok
            | Error e -> 
                log.Error (sprintf "getStateRefactored: %s" e)
                Error e