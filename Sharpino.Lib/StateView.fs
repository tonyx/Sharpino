namespace Sharpino

open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open FSharp.Core
open FSharpPlus

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions
open Sharpino.Core
open Sharpino.Definitions
open Sharpino.Storage
open Sharpino.Cache

open FsToolkit.ErrorHandling
open System

module StateView =
    let cancellationTokenSourceExpiration = 100000

    let inline private unboxCacheState<'A> (stateValue: obj) : Result<'A, string> =
        match stateValue with
        | :? 'A as state -> Ok state
        | :? System.Text.Json.JsonElement as jsonElement ->
            try
                System.Text.Json.JsonSerializer.Deserialize<'A>(jsonElement, Cache.jsonOptions) |> Ok
            with ex ->
                Error (sprintf "unboxCacheState: failed to deserialize JsonElement to %s using System.Text.Json. Error: %s" (typeof<'A>.FullName) ex.Message)
        | x -> 
             try
                Ok (x :?> 'A)
             with _ ->
                Error (sprintf "unboxCacheState: cannot cast %s to %s" (x.GetType().FullName) (typeof<'A>.FullName))

    let logger = Cache.loggerFactory.CreateLogger("Sharpino.StateView")
    
    [<Obsolete("This method is deprecated and will be removed in a future version. Please config log on appsettings.json")>]
    let setLogger (newLogger: ILogger) =
        ()

    let inline private tryGetAggregateSnapshot<'A, 'F
        when 
        'A: (static member Deserialize: 'F -> Result<'A, string>) and
        'A: (static member StorageName: string) and
        'A: (static member Version: string)
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
                                logger.LogError (sprintf "deserialization error %A for snapshot %A" e snapshot')
                                Error (sprintf "deserialization error %A for snapshot %A" e snapshot')
                            | _ ->
                                logger.LogError (sprintf "deserialization error for snapshot %A" snapshot')
                                Error (sprintf "deserialization error for snapshot %A" snapshot')
                        | None ->
                            Error (sprintf "snapshot not found. Stream %A %A, aggregate id %A" 'A.Version 'A.StorageName aggregateId)
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
            logger.LogDebug (sprintf "getLastSnapshotOrStateCache %s - %s" 'A.Version 'A.StorageName)
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
                            

    let inline  getLastAggregateSnapshot<'A, 'F 
        when 
        'A: (static member Deserialize: 'F -> Result<'A, string>) and
        'A: (static member StorageName: string) and
        'A: (static member Version: string)
        >
        (aggregateId: Guid)
        (version: string)
        (storageName: string)
        (storage: IEventStore<'F>)
        :Result<(Option<Definitions.EventId> * 'A) option,string>
        =
            logger.LogDebug (sprintf "getLastAggregateSnapshot %A - %s - %s" aggregateId version storageName)
            Async.RunSynchronously
                (async {
                    return
                        result {
                            let found = storage.TryGetLastAggregateSnapshot version storageName aggregateId
                            match found with
                            | Ok (eventId, snapshot) ->
                                let deserialized = 'A.Deserialize snapshot
                                match deserialized with
                                | Ok deserialized -> return (eventId , deserialized) |> Some 
                                | Error e -> return! Error e
                            | Error e -> return! Error e
                        }
                }, Commons.generalAsyncTimeOut)
                
    let inline  getLastAggregateSnapshotAsync<'A, 'F 
        when 
        'A: (static member Deserialize: 'F -> Result<'A, string>) and
        'A: (static member StorageName: string) and
        'A: (static member Version: string)
        >
        (aggregateId: Guid)
        (version: string)
        (storageName: string)
        (storage: IEventStore<'F>)
        (ct: Option<CancellationToken>) : TaskResult<(Option<Definitions.EventId> * 'A) ,string> =
            logger.LogDebug (sprintf "getLastAggregateSnapshotAsync %A - %s - %s" aggregateId version storageName)
            let ct = ct |> Option.defaultValue CancellationToken.None
            taskResult
                {
                    let result =
                        let found = 
                            storage.TryGetLastAggregateSnapshotAsync(version, storageName, aggregateId, ct)
                            |> Async.AwaitTask
                            |> Async.RunSynchronously
                            
                        match found with
                        | Ok (eventId, snapshot) ->
                            let deserialized = 'A.Deserialize snapshot
                            match deserialized with
                            | Ok deserialized ->  (eventId , deserialized) |> Ok
                            | Error e ->  Error e 
                        | Error e ->  Error e
                    return! result
                }
                
    // todo: it's safe to remove
    let inline private getLastAggregateSnapshotOrStateCache<'A, 'F 
        when 
        'A: (static member Deserialize: 'F -> Result<'A, string>) and
        'A: (static member StorageName: string) and
        'A: (static member Version: string)
        >
        (aggregateId: Guid)
        (version: string)
        (storageName: string)
        (storage: IEventStore<'F>) 
        =
            logger.LogDebug (sprintf "getLastAggregateSnapshotOrStateCache %A - %s - %s" aggregateId version storageName)
            Async.RunSynchronously
                (async {
                    return
                        result {
                            let lastCacheEventId = Cache.AggregateCache3.Instance.LastEventId(aggregateId) |> Option.defaultValue 0
                            let (snapshotEventId, lastSnapshotId) = storage.TryGetLastSnapshotIdByAggregateId version storageName aggregateId |> Option.defaultValue (None, 0)
                            if (lastSnapshotId = 0 && lastCacheEventId = 0) then
                                return None
                            else
                                if 
                                    snapshotEventId.IsSome && lastCacheEventId >= snapshotEventId.Value then
                                    let! state = 
                                        Cache.AggregateCache3.Instance.GetState aggregateId
                                    let! unboxedState = unboxCacheState<'A> state
                                    return (lastCacheEventId |> Some, unboxedState) |> Some 
                                else
                                    let! (eventId, snapshot) = 
                                        tryGetAggregateSnapshot<'A, 'F > aggregateId lastSnapshotId version storageName storage 
                                    return (eventId, snapshot) |> Some 
                        }
                }, Commons.generalAsyncTimeOut)
                
    let inline private getLastHistoryAggregateSnapshotOrStateCache<'A, 'F 
        when 
        'A: (static member Deserialize: 'F -> Result<'A, string>) and
        'A: (static member StorageName: string) and
        'A: (static member Version: string)
        >
        (aggregateId: Guid)
        (version: string)
        (storageName: string)
        (storage: IEventStore<'F>) 
        =
            logger.LogDebug (sprintf "getLastHistoryAggregateSnapshotOrStateCache %A - %s - %s" aggregateId version storageName)
            Async.RunSynchronously
                (async {
                    return
                        result {
                            let lastCacheEventId = Cache.AggregateCache3.Instance.LastEventId(aggregateId) |> Option.defaultValue 0
                            let (snapshotEventId, lastSnapshotId) = storage.TryGetLastHistorySnapshotIdByAggregateId version storageName aggregateId |> Option.defaultValue (None, 0)
                            if (lastSnapshotId = 0 && lastCacheEventId = 0) then
                                return None
                            else
                                if 
                                    snapshotEventId.IsSome && lastCacheEventId >= snapshotEventId.Value then
                                    let! state = 
                                        Cache.AggregateCache3.Instance.GetState aggregateId
                                    let! unboxedState = unboxCacheState<'A> state
                                    return (lastCacheEventId |> Some, unboxedState) |> Some 
                                else
                                    let! (eventId, snapshot) = 
                                        tryGetAggregateSnapshot<'A, 'F > aggregateId lastSnapshotId version storageName storage 
                                    return (eventId, snapshot) |> Some 
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
        logger.LogDebug (sprintf "snapIdStateAndEvents %s - %s" 'A.Version 'A.StorageName)
        Async.RunSynchronously
            (async {
                return
                    result {
                        let! (eventId, state: 'A) = getLastSnapshotOrStateCache<'A, 'F> storage //getLastSnapshotOStateCacher<'A, 'F> storage
                        let! events = storage.GetEventsAfterId 'A.Version eventId 'A.StorageName
                        let res =
                            (eventId, state, events)
                        return res
                    }
            }, Commons.generalAsyncTimeOut)

    let inline snapAggregateEventIdStateAndEvents<'A, 'E, 'F
        when 'E :> Event<'A> and
        'A: (static member Deserialize: 'F -> Result<'A, string>) and
        'A: (static member StorageName: string) and
        'A: (static member Version: string)>
        (id: Guid)
        (eventStore: IEventStore<'F>)
        = 
        logger.LogDebug (sprintf "snapAggregateEventIdStateAndEvents %A - %s - %s" id 'A.Version 'A.StorageName)
        
        async {
            return
                result {
                    let! eventIdAndState = getLastAggregateSnapshot<'A, 'F> id 'A.Version 'A.StorageName eventStore
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

    let inline snapAggregateEventIdStateAndEventsAsync<'A, 'E, 'F
        when 'E :> Event<'A> and
        'A: (static member Deserialize: 'F -> Result<'A, string>) and
        'A: (static member StorageName: string) and
        'A: (static member Version: string)>
        (id: Guid)
        (eventStore: IEventStore<'F>)
        (ct: Option<CancellationToken>) : TaskResult<Option<Definitions.EventId> * 'A * List<Definitions.EventId * 'F>, string> =
            logger.LogDebug (sprintf "snapAggregateEventIdStateAndEventsAsync %A - %s - %s" id 'A.Version 'A.StorageName)
            taskResult
                {
                    let! eventIdAndState = 
                        getLastAggregateSnapshotAsync<'A, 'F> id 'A.Version 'A.StorageName eventStore ct
                    match eventIdAndState with
                    | Some eventId, (state: 'A) ->
                        let! events = eventStore.GetAggregateEventsAfterIdAsync ('A.Version, 'A.StorageName, id, eventId, ct |> Option.defaultValue CancellationToken.None)
                        let result = (Some eventId, state, events)
                        return result
                    | None, (state: 'A) ->
                        let! events = eventStore.GetAggregateEventsAsync ('A.Version, 'A.StorageName, id, ct |> Option.defaultValue CancellationToken.None)
                        let result = (None, state, events)
                        return result
                }
    
    let inline snapHistoryAggregateEventIdStateAndEvents<'A, 'E, 'F
        when 'E :> Event<'A> and
        'A: (static member Deserialize: 'F -> Result<'A, string>) and
        'A: (static member StorageName: string) and
        'A: (static member Version: string)>
        (id: Guid)
        (eventStore: IEventStore<'F>)
        = 
        logger.LogDebug (sprintf "snapHistoryAggregateEventIdStateAndEvents %A - %s - %s" id 'A.Version 'A.StorageName)
        
        async {
            return
                result {
                    let! eventIdAndState = getLastHistoryAggregateSnapshotOrStateCache<'A, 'F> id 'A.Version 'A.StorageName eventStore
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
                            logger.LogError (sprintf "getState: %A" e)
                            Error e
                    return result
                }, Commons.generalAsyncTimeOut)

    let inline getAggregateFreshState<'A, 'E, 'F
        when 'E :> Event<'A>
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        >
        (id: Guid)
        (eventStore: IEventStore<'F>)
        =
            logger.LogDebug (sprintf "getAggregateFreshState %A - %s - %s" id 'A.Version 'A.StorageName)
            let computeNewStateAndLatestEventId =
                fun () ->
                    result {
                        let! (eventId, state, events) = snapAggregateEventIdStateAndEvents<'A, 'E, 'F> id eventStore
                        let! deserEvents =
                            events 
                            |>> snd 
                            |> List.traverseResultM (fun x -> 'E.Deserialize x)
                        let! unboxedState = unboxCacheState<'A> state
                        let! newState = 
                            deserEvents |> evolve<'A, 'E> unboxedState
                        let lastEventId =
                            if (events.Length > 0)
                                then (events |> List.last |> fst)
                            else (eventId |> Option.defaultValue 0)
                        let result = (lastEventId, newState |> box)
                        return result 
                    }
            let state = AggregateCache3.Instance.Memoize computeNewStateAndLatestEventId id
            match state with
            | Ok (eventId, stateValue) ->
                result {
                    let! unboxedState = unboxCacheState<'A> stateValue
                    return (eventId, unboxedState)
                }
            | Error e ->
                logger.LogDebug (sprintf "getAggregateFreshState: %s" e)
                Error e

    let inline getAggregateFreshStateAsync<'A, 'E, 'F
        when 'E :> Event<'A>
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        >
        (id: Guid)
        (eventStore: IEventStore<'F>)
        (ct: Option<CancellationToken>) : TaskResult<EventId * 'A, string> =
            logger.LogDebug (sprintf "getAggregateFreshStateAsync %A - %s - %s" id 'A.Version 'A.StorageName)
            let computeNewStateAndLatestEventId (token: Option<CancellationToken>) =
                taskResult {
                    let! (eventId, state, events) = snapAggregateEventIdStateAndEventsAsync<'A, 'E, 'F> id eventStore token
                    let! deserEvents =
                        events 
                        |>> snd 
                        |> List.traverseResultM (fun x -> 'E.Deserialize x)
                    let! unboxedState = unboxCacheState<'A> state
                    let! newState = 
                        deserEvents |> evolve<'A, 'E> unboxedState   
                    let lastEventId =
                        if (events.Length > 0)
                            then (events |> List.last |> fst)
                        else (eventId |> Option.defaultValue 0)
                    let result = (lastEventId, newState |> box)
                    return result 
                }
            taskResult {
                let! (eventId, stateValue) = AggregateCache3.Instance.MemoizeAsync computeNewStateAndLatestEventId id ct
                let! unboxedState = unboxCacheState<'A> stateValue
                return (eventId, unboxedState)
            }

    let inline getRefreshableDetails<'A>
        (refreshableDetailsBuilder: unit -> Result<Refreshable<'A> * List<Guid>, string>)
        (key: DetailsCacheKey) =
        
        let result = DetailsCache.Instance.Memoize refreshableDetailsBuilder key
        match result with
        | Error e -> Error e
        | Ok res -> unboxCacheState<'A> res

    // todo:
    let inline getRefreshableDetailsAsync<'A>
        (refreshableDetailsBuilder: Option<CancellationToken> -> Result<Refreshable<'A> * List<Guid>, string>)
        (key: DetailsCacheKey) 
        (ct: Option<CancellationToken>)
        =
        
        let result = DetailsCache.Instance.MemoizeAsync (fun _ -> refreshableDetailsBuilder ct) key ct
        match result with
        | Error e -> Error e
        | Ok res -> unboxCacheState<'A> res
    
    let inline getHistoryAggregateFreshState<'A, 'E, 'F
        when 'E :> Event<'A>
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        >
        (id: Guid)
        (eventStore: IEventStore<'F>)
        =
            logger.LogDebug (sprintf "getHistoryAggregateFreshState %A - %s - %s" id 'A.Version 'A.StorageName)
            let computeNewState =
                fun () ->
                    result {
                        let! (_, state, events) = snapHistoryAggregateEventIdStateAndEvents<'A, 'E, 'F> id eventStore
                        let! deserEvents =
                            events 
                            |>> snd 
                            |> List.traverseResultM (fun x -> 'E.Deserialize x)
                        let! unboxedState = unboxCacheState<'A> state
                        let! newState = 
                            deserEvents |> evolve<'A, 'E> unboxedState
                        return newState
                    }
            
            let lastEventId = eventStore.TryGetLastAggregateEventId 'A.Version 'A.StorageName id |> Option.defaultValue 0
            let state = computeNewState ()
            
            match state with
            | Ok state -> 
                (lastEventId, state) |> Ok
            | Error e ->
                logger.LogError (sprintf "getAggregateFreshState: %s" e)
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
            logger.LogDebug (sprintf "getFilteredEventsInATimeInterval %A - %s" 'A.Version 'A.StorageName)
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
        when 'E :> Event<'A>
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
            logger.LogDebug (sprintf "getFilteredAggregateEventsInATimeInterval %A - %s - %s" id 'A.Version 'A.StorageName)
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
                
    let inline getFilteredAggregateEventsInATimeIntervalAsync<'A, 'E, 'F
        when 'E :> Event<'A>
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
        (ct: Option<CancellationToken>)
        =
            logger.LogDebug (sprintf "getFilteredAggregateEventsInATimeInterval %A - %s - %s" id 'A.Version 'A.StorageName)
            taskResult
                {
                    let! allEventsInTimeInterval =
                        match ct with
                        | Some ct -> eventStore.GetAggregateEventsInATimeIntervalAsync ('A.Version, 'A.StorageName, id, start, end_, ct)
                        | None -> eventStore.GetAggregateEventsInATimeIntervalAsync ('A.Version, 'A.StorageName, id, start, end_)
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
        when 'E :> Event<'A>
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
            logger.LogDebug (sprintf "getFilteredMultipleAggregateEventsInATimeInterval - %s - %s" 'A.Version 'A.StorageName)
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
                
    let inline getFilteredMultipleAggregateEventsInATimeIntervalAsync<'A, 'E, 'F            
        when 'E :> Event<'A>
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
        (ct: Option<CancellationToken>)
        =
            logger.LogDebug (sprintf "getFilteredMultipleAggregateEventsInATimeInterval - %s - %s" 'A.Version 'A.StorageName)
            taskResult
                {
                    let! allEventsInAtimeInterval =
                        match ct with
                        | Some ct ->
                            eventStore.GetMultipleAggregateEventsInATimeIntervalAsync ('A.Version, 'A.StorageName, ids, start, end_, ct)
                        | None ->
                            eventStore.GetMultipleAggregateEventsInATimeIntervalAsync ('A.Version, 'A.StorageName, ids, start, end_)    
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
        when 'E :> Event<'A>
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
            logger.LogDebug (sprintf "getAllFilteredAggregateEventsInATimeInterval %A - %s " 'A.Version 'A.StorageName)
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
   
    let inline GetAllAggregateEventsInATimeIntervalAsync<'A, 'E, 'F
        when 'E :> Event<'A>
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        >
        (eventStore: IEventStore<'F>)
        (start: DateTime)
        (end_: DateTime)
        (ct: Option<CancellationToken>)
        = 
        logger.LogDebug (sprintf "getAllAggregateEventsInATimeIntervalAsync %A - %s " 'A.Version 'A.StorageName)
        taskResult
            {
                let! allEventsInTimeInterval =
                    match ct with
                    | Some ct -> eventStore.GetAllAggregateEventsInATimeIntervalAsync('A.Version, 'A.StorageName, start, end_, ct)
                    | None -> eventStore.GetAllAggregateEventsInATimeIntervalAsync('A.Version, 'A.StorageName, start, end_)
                let result = ResizeArray<EventId * AggregateId * 'E>()
                if (allEventsInTimeInterval.Count = 0)
                then return result
                else
                    let errors = StringBuilder()
                    let _ =
                        [ 0 .. allEventsInTimeInterval.Count  - 1 ]
                        |> List.iter (fun i ->
                            let (eventId, aggregateId, deserEvent) = (allEventsInTimeInterval.Item i)
                            match 'E.Deserialize deserEvent with
                            | Ok desEvent ->
                                result.Add(eventId, aggregateId, desEvent)
                            | Error err ->
                                errors.AppendLine(err) |> ignore
                        )
                    if errors.Length > 0
                    then
                        return! Error (errors.ToString())
                    else    
                        return result
            }
    
    [<Obsolete "getFilteredAggregateStatesInATimeInterval2 instead ">]
    let inline getFilteredAggregateSnapshotsInATimeInterval<'A, 'F
        when 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        >
        (eventStore: IEventStore<'F>)
        (start: DateTime)
        (end_: DateTime)
        (predicate: 'A -> bool)
        =
            logger.LogDebug (sprintf "getFilteredAggregateSnapshotsInATimeInterval %A - %s - %s" id 'A.Version 'A.StorageName)
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
    
    [<Obsolete "use getFilteredAggregateStatesInATimeInterval2">]
    let inline getFilteredAggregateStatesInATimeInterval<'A, 'E, 'F
        when 'E :> Event<'A>
        and 'A: (member Id: Guid)
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
            logger.LogDebug (sprintf "getFilteredAggregateStatesInATimeInterval %A - %s - %s" id 'A.Version 'A.StorageName)
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
                
    let inline getFilteredAggregateStatesInATimeInterval2<'A, 'E, 'F
        when 
        'E :> Event<'A>
        and 'E : (static member Deserialize: 'F -> Result<'E, string>)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        >
        (eventStore: IEventStore<'F>)
        (start: DateTime)
        (end_: DateTime)
        (predicate: 'A -> bool)
        =
            logger.LogDebug (sprintf "getfilteredAggregateStatesInATimeInterval %A - %s - %s" id 'A.Version 'A.StorageName)
            result
                {
                    let! ids =
                        eventStore.GetAggregateIdsInATimeInterval 'A.Version 'A.StorageName start end_
                    let allStates =
                        ids |>> (fun id -> getAggregateFreshState<'A, 'E, 'F> id eventStore) 
                    
                    let states =
                        allStates
                        |> List.filter _.IsOk
                        |> List.map (fun x -> x.OkValue)
                    
                    return
                        states
                        |> List.filter (fun (_, x)  -> predicate x)
                        |> List.map (fun (id, x) -> (id, x))
                }
    
    // todo: open issue https://github.com/tonyx/Sharpino/issues/57
    let inline getAggregateStatesInATimeInterval<'A, 'E, 'F
        when 'E :> Event<'A>
        and 'E : (static member Deserialize: 'F -> Result<'E, string>)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        >
        (eventStore: IEventStore<'F>)
        (start: DateTime)
        (end_: DateTime)
        =
            logger.LogDebug (sprintf "getAggregateStatesInATimeInterval %A - %s - %s" id 'A.Version 'A.StorageName)
            result
                {
                    let! ids =
                        eventStore.GetAggregateIdsInATimeInterval 'A.Version 'A.StorageName start end_
                    let allStates =
                        ids |>> (fun id -> getAggregateFreshState<'A, 'E, 'F> id eventStore)
                        |> List.filter _.IsOk
                        |> List.map (fun x -> x.OkValue |> fun (id, x) -> (id, x))
                    return allStates    
                }
                
    // todo: open issue https://github.com/tonyx/Sharpino/issues/57
    let inline getAggregateStatesInATimeIntervalAsync<'A, 'E, 'F
        when 'E :> Event<'A>
        and 'E : (static member Deserialize: 'F -> Result<'E, string>)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        >
        (eventStore: IEventStore<'F>)
        (start: DateTime)
        (end_: DateTime)
        (ct: Option<CancellationToken>)
        =
            logger.LogDebug (sprintf "getAggregateStatesInATimeInterval %A - %s - %s" id 'A.Version 'A.StorageName)
            taskResult
                {
                    use cts = CancellationTokenSource.CreateLinkedTokenSource
                                  (defaultArg ct (new CancellationTokenSource(Commons.generalAsyncTimeOut)).Token)
                    cts.CancelAfter cancellationTokenSourceExpiration
                    let! ids =
                        eventStore.GetAggregateIdsInATimeIntervalAsync('A.Version, 'A.StorageName, start, end_, cts.Token)
                    let allStates =
                        ids |>> (fun id -> getAggregateFreshState<'A, 'E, 'F> id eventStore)
                        
                    let result =
                        allStates
                        |> List.filter _.IsOk
                        |> List.map (fun x -> x.OkValue |> fun (id, x) -> (id, x))
                    return result
                }
                
    // A valid reason for throwing errors is that theoretically if you have an id of an "undeleted" aggregate, it could be possible that later you cannot retrieve it anymore because, for example, in the mean time it becomes "deleted"
    let inline getAllAggregateStates<'A, 'E, 'F
        when 'E :> Event<'A>
        and 'E : (static member Deserialize: 'F -> Result<'E, string>)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        >
        (eventStore: IEventStore<'F>) =
            logger.LogDebug (sprintf "getAggregateStates %A - %s - %s" id 'A.Version 'A.StorageName)
            result
                {
                    let! ids =
                        eventStore.GetUndeletedAggregateIds 'A.Version 'A.StorageName
                    let allStates =
                        ids
                        |>> (fun id -> getAggregateFreshState<'A, 'E, 'F> id eventStore)
                    let result =
                        allStates
                        |> List.filter _.IsOk
                        |> List.map (fun x -> x.OkValue |> fun (id, x) -> (id, x))
                    return result
                }

    let inline getAllFilteredAggregateStates<'A, 'E, 'F
        when 'E :> Event<'A>
        and 'E : (static member Deserialize: 'F -> Result<'E, string>)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        >
        (predicate: 'A -> bool)
        (eventStore: IEventStore<'F>) =
            logger.LogDebug (sprintf "getAllFilteredAggregateStates %A - %s - %s" id 'A.Version 'A.StorageName)
            result
                {
                    let! ids =
                        eventStore.GetUndeletedAggregateIds 'A.Version 'A.StorageName
                    let allStates =
                        ids
                        |>> (fun id -> getAggregateFreshState<'A, 'E, 'F> id eventStore)
                    let result =
                        allStates 
                        |> List.filter _.IsOk
                        |> List.map (fun x -> x.OkValue)
                        |> List.filter (fun (_, x) -> predicate x)
                    return result
                }

    // A valid reason for throwing errors is that theoretically if you have an id of an "undeleted" aggregate, it could be possible that later you cannot retrieve it anymore because, for example, in the mean time it becomes "deleted"
    let inline getAllAggregateStatesAsync<'A, 'E, 'F
        when 'E :> Event<'A>
        and 'E : (static member Deserialize: 'F -> Result<'E, string>)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        >
        (eventStore: IEventStore<'F>)
        (ct: Option<CancellationToken>) =
            logger.LogDebug (sprintf "getAllAggregateStatesAsync - %s - %s\n" 'A.Version 'A.StorageName)
            taskResult
                {
                    use cts = CancellationTokenSource.CreateLinkedTokenSource
                                  (defaultArg ct (new CancellationTokenSource(Commons.generalAsyncTimeOut)).Token)
                    cts.CancelAfter cancellationTokenSourceExpiration
                    let! (ids: Guid list) =
                        eventStore.GetUndeletedAggregateIdsAsync('A.Version, 'A.StorageName, cts.Token)
                    
                    let! (results: Result<EventId * 'A, string> array) = 
                         ids 
                         |> List.map (fun id -> getAggregateFreshStateAsync<'A, 'E, 'F> id eventStore (Some cts.Token))
                         |> Task.WhenAll
                         |> Task.map Ok
                    
                    let result =
                        results
                        |> Array.toList
                        |> List.choose (function | Ok x -> Some x | Error _ -> None)
                    return result    
                }

    let inline getAllFilteredAggregateStatesAsync<'A, 'E, 'F
        when 'E :> Event<'A>
        and 'E : (static member Deserialize: 'F -> Result<'E, string>)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        >
        (predicate: 'A -> bool)
        (eventStore: IEventStore<'F>)
        (ct: Option<CancellationToken>) =
            logger.LogDebug (sprintf "getAllFilteredAggregateStatesAsync - %s - %s\n" 'A.Version 'A.StorageName)
            taskResult
                {
                    use cts = CancellationTokenSource.CreateLinkedTokenSource
                                  (defaultArg ct (new CancellationTokenSource(Commons.generalAsyncTimeOut)).Token)
                    cts.CancelAfter cancellationTokenSourceExpiration
                    let! (ids: Guid list) =
                        eventStore.GetUndeletedAggregateIdsAsync('A.Version, 'A.StorageName, cts.Token)
                    
                    let! (results: Result<EventId * 'A, string> array) = 
                         ids 
                         |> List.map (fun id -> getAggregateFreshStateAsync<'A, 'E, 'F> id eventStore (Some cts.Token))
                         |> Task.WhenAll
                         |> Task.map Ok
                    
                    let result =
                        results
                        |> Array.toList
                        |> List.choose (function | Ok (eventId, state) when predicate state -> Some (eventId, state) | _ -> None)
                    return result    
                }
    
    [<Obsolete "if you use this you will need all the after-refactoring upcast chain">]
    let inline getInitialAggregateSnapshot<'A, 'F
        when 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        >
        (id: Guid)
        (eventStore: IEventStore<'F>)
        =
            logger.LogDebug (sprintf "getInitialAggregateSnapshot %A - %s - %s" id 'A.Version 'A.StorageName)
            eventStore.TryGetFirstSnapshot 'A.Version 'A.StorageName id
            >>= (fun x -> 'A.Deserialize (snd x))
            
    