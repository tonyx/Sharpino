namespace Sharpino

open FSharp.Core
open FSharpPlus

open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions
open Sharpino.Core
open Sharpino.Storage
open Sharpino.Cache

open FsToolkit.ErrorHandling
open System

module StateView =
    let logger: ILogger ref = ref NullLogger.Instance
    let setLogger (newLogger: ILogger) =
        logger := newLogger

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
            Async.RunSynchronously(
                async {
                    let result =
                        let snapshot = storage.TryGetAggregateSnapshotById version storageName aggregateId id
                        match snapshot |>> snd with
                        | Some snapshot' ->
                            let deserSnapshot = 'A.Deserialize  snapshot'
                            let eventId = snapshot |>> fst
                            match deserSnapshot, eventId with
                            | Ok deserSnapshot, Some evId  ->
                                (evId, deserSnapshot) |> Ok
                            | Error e, _->
                                logger.Value.LogError (sprintf "deserialization error %A for snapshot %A" e snapshot')
                                Error (sprintf "deserialization error %A for snapshot %A" e snapshot')
                            | _ ->
                                logger.Value.LogError (sprintf "deserialization error for snapshot %A" snapshot')
                                Error (sprintf "deserialization error for snapshot %A" snapshot')
                        | None ->
                            Error "not found"
                    return result
                }, Commons.generalAsyncTimeOut)
   
    let inline private getLastSnapshot<'A, 'F 
        when 'A: (static member Zero: 'A) 
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (member Serialize: 'F)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        >
        (storage: IEventStore<'F>) =
            result
                {
                    let lastSnapshot = storage.TryGetLastSnapshot 'A.Version 'A.StorageName
                    match lastSnapshot with
                    | Some (_, evId, snapshot) -> 
                        let! deserializedSnapshot = 'A.Deserialize snapshot
                        return (evId, deserializedSnapshot)
                    | _ -> 
                        return (0, 'A.Zero)
                }
                
    let inline private getLastSnapshotOrStateCache<'A, 'F
        when 'A: (static member Zero: 'A) 
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (member Serialize: 'F)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        >
        (storage: IEventStore<'F>) =
            logger.Value.LogDebug (sprintf "getLastSnapshotOrStateCache %s - %s" 'A.Version 'A.StorageName)
            Async.RunSynchronously
                (async {
                    return
                        result {
                            let state = 
                                match Cache.StateCache2<'A>.Instance.GetEventIdAndState () with
                                | Some (evId, state) -> (evId, state) |> Ok
                                | _ ->
                                    getLastSnapshot<'A, 'F> storage
                            return! state
                        }
                    }, Commons.generalAsyncTimeOut)
                            

    let inline private getLastAggregateSnapshotOrStateCache<'A, 'F 
        when 'A :> Aggregate<'F> and
        'A: (static member Deserialize: 'F -> Result<'A, string>)
        >
        (aggregateId: Guid)
        (version: string)
        (storageName: string)
        (storage: IEventStore<'F>) 
        =
            logger.Value.LogDebug (sprintf "getLastAggregateSnapshotOrStateCache %A - %s - %s" aggregateId version storageName)
            Async.RunSynchronously
                (async {
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
                }, Commons.generalAsyncTimeOut)

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
        logger.Value.LogDebug (sprintf "snapIdStateAndEvents %s - %s" 'A.Version 'A.StorageName)
        Async.RunSynchronously
            (async {
                return
                    result {
                        let! (eventId, state: 'A) = getLastSnapshotOrStateCache<'A, 'F> storage
                        let! events = storage.GetEventsAfterId 'A.Version eventId 'A.StorageName
                        let res =
                            (eventId, state, events)
                        return res
                    }
            }, Commons.generalAsyncTimeOut)

    let inline snapAggregateEventIdStateAndEvents<'A, 'E, 'F
        when 'A :> Aggregate<'F> and 'E :> Event<'A> and
        'A: (static member Deserialize: 'F -> Result<'A, string>) and
        'A: (static member StorageName: string) and
        'A: (static member Version: string)>
        (id: Guid)
        (eventStore: IEventStore<'F>)
        = 
        logger.Value.LogDebug (sprintf "snapAggregateEventIdStateAndEvents %A - %s - %s" id 'A.Version 'A.StorageName)
        
        async {
            return
                result {
                    let! eventIdAndState = getLastAggregateSnapshotOrStateCache<'A, 'F> id 'A.Version 'A.StorageName eventStore
                    match eventIdAndState with
                    | None -> 
                        return! Error (sprintf "There is no aggregate of version %A, name %A with id %A" 'A.Version 'A.StorageName id)
                    | Some (Some eventId, state) ->
                        let! events = eventStore.GetAggregateEventsAfterId 'A.Version 'A.StorageName id eventId
                        let result =
                            (eventId |> Some, state, events)
                        return result
                    | Some (None, state) ->
                        let! events = eventStore.GetAggregateEvents 'A.Version 'A.StorageName id 
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
        (eventStore: IEventStore<'F>) =
            Async.RunSynchronously
                (async {
                    let result =        
                        let computeNewState =
                            fun () ->
                                result {
            
                                    let! (_, state, events) = snapEventIdStateAndEvents<'A, 'E, 'F> eventStore
                                    let! deserEvents =
                                        events 
                                        |>> snd 
                                        |> List.traverseResultM (fun x -> 'E.Deserialize x)
                                    let! newState = 
                                        deserEvents |> evolve<'A, 'E> state
                                    return newState
                                }
            
                        let lastEventId = eventStore.TryGetLastEventId 'A.Version 'A.StorageName |> Option.defaultValue 0
                        let state = StateCache2<'A>.Instance.Memoize computeNewState lastEventId
                        match state with
                        | Ok state' -> 
                            (lastEventId, state') |> Ok
                        | Error e ->
                            logger.Value.LogError (sprintf "getState: %A" e)
                            Error e
                    return result
                }, Commons.generalAsyncTimeOut)

    let inline getAggregateFreshState<'A, 'E, 'F
        when 'A :> Aggregate<'F> and 'E :> Event<'A>
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        >
        (id: Guid)
        (eventStore: IEventStore<'F>)
        =
            logger.Value.LogDebug (sprintf "getAggregateFreshState %A - %s - %s" id 'A.Version 'A.StorageName)
            let computeNewState =
                fun () ->
                    result {
                        let! (_, state, events) = snapAggregateEventIdStateAndEvents<'A, 'E, 'F> id eventStore
                        let! deserEvents =
                            events 
                            |>> snd 
                            |> List.traverseResultM (fun x -> 'E.Deserialize x)
                        let! newState = 
                            deserEvents |> evolve<'A, 'E> state
                        return newState
                    }
            let lastEventId = eventStore.TryGetLastAggregateEventId 'A.Version 'A.StorageName  id |> Option.defaultValue 0
            logger.Value.LogDebug (sprintf "getAggregateFreshState %A - %s - %s" id 'A.Version 'A.StorageName)
            let state = AggregateCache<'A, 'F>.Instance.Memoize computeNewState (lastEventId, id)
            match state with
            | Ok state -> 
                (lastEventId, state) |> Ok
            | Error e ->
                logger.Value.LogError (sprintf "getAggregateFreshState: %s" e)
                Error e
   
    let inline getFilteredEventsInATimeInterval<'A, 'E, 'F
        when 'A: (static member Zero: 'A)
        and 'E :> Event<'A>
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        >
        (eventStore: IEventStore<'F>)
        (start: DateTime)
        (end_: DateTime)
        (predicate: 'E -> bool)
        =
            logger.Value.LogDebug (sprintf "getFilteredEventsInATimeInterval %A - %s" 'A.Version 'A.StorageName)
            result
                {
                    let! allEventsInTimeInterval = eventStore.GetEventsInATimeInterval 'A.Version 'A.StorageName start end_
                    let! deserEvents =
                        allEventsInTimeInterval
                        |>> snd 
                        |> List.traverseResultM (fun x -> 'E.Deserialize x)
                    let filteredEvents = 
                        deserEvents
                        |> List.filter predicate
                    return filteredEvents
                }
     
    let inline getFilteredAggregateEventsInATimeInterval<'A, 'E, 'F
        when 'A :> Aggregate<'F> and 'E :> Event<'A>
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        >
        (id: Guid)
        (eventStore: IEventStore<'F>)
        (start: DateTime)
        (end_: DateTime)
        (predicate: 'E -> bool)
        =
            logger.Value.LogDebug (sprintf "getFilteredAggregateEventsInATimeInterval %A - %s - %s" id 'A.Version 'A.StorageName)
            result
                {
                    let! allEventsInTimeInterval = eventStore.GetAggregateEventsInATimeInterval 'A.Version 'A.StorageName id start end_
                    let! deserEvents =
                        allEventsInTimeInterval
                        |>> snd 
                        |> List.traverseResultM (fun x -> 'E.Deserialize x)
                    let filteredEvents = 
                        deserEvents
                        |> List.filter predicate
                    return filteredEvents
                }
    
    let inline getFilteredMultipleAggregateEventsInATimeInterval<'A, 'E, 'F            
        when 'A :> Aggregate<'F> and 'E :> Event<'A>
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        >
        (ids: List<Guid>)
        (eventStore: IEventStore<'F>)
        (start: DateTime)
        (end_: DateTime)
        (predicate: 'E -> bool)
        =
            logger.Value.LogDebug (sprintf "getFilteredMultipleAggregateEventsInATimeInterval - %s - %s" 'A.Version 'A.StorageName)
            result
                {
                    let! allEventsInAtimeInterval = eventStore.GetMultipleAggregateEventsInATimeInterval 'A.Version 'A.StorageName ids start end_
                    let! deserEvents =
                        allEventsInAtimeInterval
                        |>> (fun (_, aggregateId, e) -> (aggregateId, e))
                        |> List.traverseResultM (fun (id, x) -> 'E.Deserialize x |> Result.map (fun x -> (id, x)))
                    let aggregatesIdsAndEvents =
                        deserEvents
                        |> List.filter (fun (_, y) -> predicate y)
                    return aggregatesIdsAndEvents    
                }
                
    let inline getAllFilteredAggregateEventsInATimeInterval<'A, 'E, 'F
        when 'A :> Aggregate<'F> and 'E :> Event<'A>
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        >
        (eventStore: IEventStore<'F>)
        (start: DateTime)
        (end_: DateTime)
        (predicate: 'E -> bool)
        =
            logger.Value.LogDebug (sprintf "getAllFilteredAggregateEventsInATimeInterval %A - %s " 'A.Version 'A.StorageName)
            result
                {
                    let! allEventsInTimeInterval = eventStore.GetAllAggregateEventsInATimeInterval 'A.Version 'A.StorageName start end_
                    let! deserEvents =
                        allEventsInTimeInterval
                        |>> snd 
                        |> List.traverseResultM (fun x -> 'E.Deserialize x)
                    let filteredEvents = 
                        deserEvents
                        |> List.filter predicate
                    return filteredEvents
                }
    
    let inline getFilteredAggregateSnapshotsInATimeInterval<'A, 'F
        when 'A :> Aggregate<'F>
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        >
        (eventStore: IEventStore<'F>)
        (start: DateTime)
        (end_: DateTime)
        (predicate: 'A -> bool)
        =
            logger.Value.LogDebug (sprintf "getfilteredAggregateSnapshotsInATimeInterval %A - %s - %s" id 'A.Version 'A.StorageName)
            result
                {
                    let! allSnapshotsInTimeInterval = eventStore.GetAggregateSnapshotsInATimeInterval 'A.Version 'A.StorageName start end_
                    let! usersGot =
                        allSnapshotsInTimeInterval
                        |>> (fun (_, _, date, snapshot) -> (date, snapshot))
                        |> List.traverseResultM (fun (date, x) -> 'A.Deserialize x |> Result.map (fun x -> (date, x)))
                    let result = 
                        usersGot
                        |> List.filter (fun (_, x) -> predicate x)
                    return result     
                }
    
    let inline getFilteredAggregateStatesInATimeInterval<'A, 'E, 'F
        when 'A :> Aggregate<'F>
        and 'E :> Event<'A>
        and 'E : (static member Deserialize: 'F -> Result<'E, string>)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        >
        (eventStore: IEventStore<'F>)
        (start: DateTime)
        (end_: DateTime)
        (initialStateFilter: 'A -> bool)
        (currentStateFilter: 'A -> bool)
        =
            logger.Value.LogDebug (sprintf "getfilteredAggregateStatesInATimeInterval %A - %s - %s" id 'A.Version 'A.StorageName)
            result
                {
                    let! allInitialStates =
                        getFilteredAggregateSnapshotsInATimeInterval<'A, 'F> eventStore start end_ initialStateFilter
                        |> Result.map (fun x -> x |>> snd)
                   
                    let ids = allInitialStates |>> fun x -> x.Id
                    let allStates =
                        ids
                        |> List.distinct
                        |>> (fun id -> getAggregateFreshState<'A, 'E, 'F> id eventStore)
                    
                    let okStates = allStates |> List.filter (fun x -> x.IsOk) |> List.map (fun x -> x.OkValue)
                    let errors = allStates |> List.filter (fun x -> x.IsError)
                    if errors.Length > 0 then
                        let errors = errors |> List.fold (fun acc x -> acc + x.ToString()+", ") ""
                        return! (Error errors)
                    else 
                        return okStates |> List.filter (fun (_, x)  -> currentStateFilter x)
                } 
    
    let inline getInitialAggregateSnapshot<'A, 'F
        when 'A :> Aggregate<'F>
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        >
        (id: Guid)
        (eventStore: IEventStore<'F>)
        =
            logger.Value.LogDebug (sprintf "getInitialAggregateSnapshot %A - %s - %s" id 'A.Version 'A.StorageName)
            eventStore.TryGetFirstSnapshot 'A.Version 'A.StorageName id
            >>= (fun x -> 'A.Deserialize (snd x))
            
    