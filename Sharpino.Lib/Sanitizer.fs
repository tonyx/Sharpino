namespace Sharpino

open System
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Sharpino.Core
open Sharpino.Definitions
open Sharpino.Storage
open Sharpino.CommandHandler
open Sharpino.Cache
open Sharpino.StateView
open FsToolkit.ErrorHandling

module Sanitizer =
    let logger =
        try
            builder.Services.BuildServiceProvider().GetRequiredService<ILoggerFactory>().CreateLogger("Sharpino.Sanitizer")
        with _ ->
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance :> ILogger

    type SanitizationAction =
        | PatchedToNoOp of EventId: EventId * CorrelationId: Option<Guid>
        | Compensated of TargetAggregateId: Guid * CorrelationId: Guid * CausationEventId: EventId * Description: string

    type StreamSanitizationReport =
        { AggregateId: Option<Guid>
          StreamName: string
          Version: string
          IsClean: bool
          Actions: List<SanitizationAction>
          FinalEventId: EventId }

    let aggregateLocks = ConcurrentDictionary<Guid, SemaphoreSlim>()
    let aggregateLastSanitized = ConcurrentDictionary<Guid, DateTimeOffset * StreamSanitizationReport>()
    let contextLocks = ConcurrentDictionary<string, SemaphoreSlim>()
    let contextLastSanitized = ConcurrentDictionary<string, DateTimeOffset * StreamSanitizationReport>()

    let mutable sanitizerCooldownMs =
        try
            config.GetValue<int>("SanitizerCooldownMs", 5000)
        with _ -> 5000

    let setCooldownMs (ms: int) = sanitizerCooldownMs <- ms

    let resetCooldowns () =
        aggregateLastSanitized.Clear()
        contextLastSanitized.Clear()

    let inline sanitizeAggregateStreamWithInitialStateAsync<'A, 'E, 'F
        when 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (member Serialize: 'F)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'E: (member Serialize: 'F)
        >
        (storage: IEventStore<'F>)
        (aggregateId: Guid)
        (initialState: 'A)
        (noOpEvent: 'E)
        (compensator: Option<Guid -> EventId -> TaskResult<string, string>>)
        : TaskResult<StreamSanitizationReport, string> =
        taskResult {
            logger.LogInformation(sprintf "Starting aggregate sanitization for %s %s (aggregateId: %A)" 'A.Version 'A.StorageName aggregateId)
            let! rawEvents = storage.GetAggregateEventsAfterIdAsync('A.Version, 'A.StorageName, aggregateId, 0)
            
            let mutable currentState = initialState
            let mutable actions = []
            let mutable lastEventId = 0
            
            for (eventId, rawEvent) in rawEvents do
                lastEventId <- eventId
                let deserializedOpt = 'E.Deserialize rawEvent
                
                let processResult =
                    match deserializedOpt with
                    | Ok ev -> ev.Process currentState
                    | Error err -> Error (sprintf "Deserialization failed for event %d: %s" eventId err)
                    
                match processResult with
                | Ok nextState ->
                    currentState <- nextState
                | Error errMsg ->
                    logger.LogWarning(sprintf "Anomaly detected in aggregate %A at event %d: %s. Applying NoOp patch." aggregateId eventId errMsg)
                    
                    let noOpSerialized = noOpEvent.Serialize
                    let! _ = storage.PatchEventWithNoOpAsync('A.Version, 'A.StorageName, aggregateId, eventId, noOpSerialized)
                    
                    let! _ = storage.DeleteSnapshotsFromEventIdAsync('A.Version, 'A.StorageName, aggregateId, eventId)
                    AggregateCache3.Instance.Clean aggregateId
                    DetailsCache.Instance.EvictDependentDetails aggregateId
                    
                    let! evOpt = storage.TryGetEventAsync('A.Version, eventId, 'A.StorageName)
                    let corrIdOpt = evOpt |> Option.bind (fun e -> e.CorrelationId)
                    
                    actions <- PatchedToNoOp(eventId, corrIdOpt) :: actions
                    
                    match corrIdOpt with
                    | Some corrId ->
                        let! (relatedEventsRes: Result<List<Guid * EventId * 'F>, string>) =
                            task {
                                let! r = storage.FindEventsByCorrelationIdAsync('A.Version, 'A.StorageName, corrId)
                                return Ok r
                            }
                        match relatedEventsRes with
                        | Ok relatedEvents ->
                            let relatedAggIds =
                                relatedEvents
                                |> List.map (fun (id, _, _) -> id)
                                |> List.distinct
                            for relId in relatedAggIds do
                                if relId <> aggregateId then
                                    AggregateCache3.Instance.Clean relId
                                    DetailsCache.Instance.EvictDependentDetails relId
                        | Error _ -> ()
                    | None -> ()

                    match corrIdOpt, compensator with
                    | Some corrId, Some comp ->
                        logger.LogInformation(sprintf "Triggering ex-post compensator for correlationId %A, poisoned event %d" corrId eventId)
                        let! (compRes: Result<string, string>) =
                            task {
                                let! r = comp corrId eventId
                                return Ok r
                            }
                        match compRes with
                        | Ok desc ->
                            actions <- Compensated(aggregateId, corrId, eventId, desc) :: actions
                        | Error compErr ->
                            logger.LogError(sprintf "Compensator failed for correlationId %A: %s" corrId compErr)
                    | _ -> ()
                    
                    match noOpEvent.Process currentState with
                    | Ok stateAfterNoOp ->
                        currentState <- stateAfterNoOp
                    | Error e ->
                        logger.LogCritical(sprintf "NoOp event itself failed to process! %s" e)
            
            // Always evict AggregateCache3 (L1 + L2) and dependent DetailsCache entries at the end of sanitization
            AggregateCache3.Instance.Clean aggregateId
            DetailsCache.Instance.EvictDependentDetails aggregateId
                
            let isClean = actions.IsEmpty
            let report =
                { AggregateId = Some aggregateId
                  StreamName = 'A.StorageName
                  Version = 'A.Version
                  IsClean = isClean
                  Actions = actions |> List.rev
                  FinalEventId = lastEventId }
            return report
        }

    let inline sanitizeAggregateStreamAsync<'A, 'E, 'F
        when 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (member Serialize: 'F)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'E: (member Serialize: 'F)
        >
        (storage: IEventStore<'F>)
        (aggregateId: Guid)
        (noOpEvent: 'E)
        (compensator: Option<Guid -> EventId -> TaskResult<string, string>>)
        : TaskResult<StreamSanitizationReport, string> =
        task {
            let sem = aggregateLocks.GetOrAdd(aggregateId, fun _ -> new SemaphoreSlim(1, 1))
            do! sem.WaitAsync()
            try
                let now = DateTimeOffset.UtcNow
                match aggregateLastSanitized.TryGetValue(aggregateId) with
                | true, (lastTime, lastReport) when sanitizerCooldownMs > 0 && (now - lastTime).TotalMilliseconds < float sanitizerCooldownMs ->
                    logger.LogInformation(sprintf "Skipping aggregate sanitization for %s %s (aggregateId: %A) due to cooldown (%dms)" 'A.Version 'A.StorageName aggregateId sanitizerCooldownMs)
                    return Ok { lastReport with IsClean = true; Actions = [] }
                | _ ->
                    let! res =
                        taskResult {
                            let! (_, firstSnap) = storage.TryGetFirstSnapshotAsync('A.Version, 'A.StorageName, aggregateId)
                            let! initialState = 'A.Deserialize firstSnap
                            let! report = sanitizeAggregateStreamWithInitialStateAsync<'A, 'E, 'F> storage aggregateId initialState noOpEvent compensator
                            AggregateCache3.Instance.Clean aggregateId
                            DetailsCache.Instance.EvictDependentDetails aggregateId
                            return report
                        }
                    match res with
                    | Ok report ->
                        aggregateLastSanitized.[aggregateId] <- (DateTimeOffset.UtcNow, report)
                    | Error _ -> ()
                    return res
            finally
                sem.Release() |> ignore
        }

    let inline sanitizeContextStreamAsync<'A, 'E, 'F
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (member Serialize: 'F)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'E: (member Serialize: 'F)
        >
        (storage: IEventStore<'F>)
        (noOpEvent: 'E)
        : TaskResult<StreamSanitizationReport, string> =
        task {
            let contextKey = sprintf "%s_%s" 'A.Version 'A.StorageName
            let sem = contextLocks.GetOrAdd(contextKey, fun _ -> new SemaphoreSlim(1, 1))
            do! sem.WaitAsync()
            try
                let now = DateTimeOffset.UtcNow
                match contextLastSanitized.TryGetValue(contextKey) with
                | true, (lastTime, lastReport) when sanitizerCooldownMs > 0 && (now - lastTime).TotalMilliseconds < float sanitizerCooldownMs ->
                    logger.LogInformation(sprintf "Skipping context sanitization for %s %s due to cooldown (%dms)" 'A.Version 'A.StorageName sanitizerCooldownMs)
                    return Ok { lastReport with IsClean = true; Actions = [] }
                | _ ->
                    let! res =
                        taskResult {
                            logger.LogInformation(sprintf "Starting context sanitization for %s %s" 'A.Version 'A.StorageName)
                            let! rawEvents = storage.GetEventsAfterIdAsync('A.Version, 0, 'A.StorageName)
                            
                            let mutable currentState = 'A.Zero
                            let mutable actions = []
                            let mutable lastEventId = 0
                            
                            for (eventId, rawEvent) in rawEvents do
                                lastEventId <- eventId
                                let deserializedOpt = 'E.Deserialize rawEvent
                                
                                let processResult =
                                    match deserializedOpt with
                                    | Ok ev -> ev.Process currentState
                                    | Error err -> Error (sprintf "Deserialization failed for event %d: %s" eventId err)
                                    
                                match processResult with
                                | Ok nextState ->
                                    currentState <- nextState
                                | Error errMsg ->
                                    logger.LogWarning(sprintf "Anomaly detected in context %s at event %d: %s. Applying NoOp patch." 'A.StorageName eventId errMsg)
                                    
                                    let noOpSerialized = noOpEvent.Serialize
                                    let! _ = storage.PatchContextEventWithNoOpAsync('A.Version, 'A.StorageName, eventId, noOpSerialized)
                                    
                                    StateCache2<'A>.Instance.Invalidate()
                                    actions <- PatchedToNoOp(eventId, None) :: actions
                                    
                                    match noOpEvent.Process currentState with
                                    | Ok stateAfterNoOp ->
                                        currentState <- stateAfterNoOp
                                    | Error e ->
                                        logger.LogCritical(sprintf "NoOp event itself failed to process! %s" e)
                                        
                            if not actions.IsEmpty then
                                StateCache2<'A>.Instance.Invalidate()
                                
                            let isClean = actions.IsEmpty
                            let report =
                                { AggregateId = None
                                  StreamName = 'A.StorageName
                                  Version = 'A.Version
                                  IsClean = isClean
                                  Actions = actions |> List.rev
                                  FinalEventId = lastEventId }
                            return report
                        }
                    match res with
                    | Ok report ->
                        contextLastSanitized.[contextKey] <- (DateTimeOffset.UtcNow, report)
                    | Error _ -> ()
                    return res
            finally
                sem.Release() |> ignore
        }

    let inline sanitizeAllAggregatesAsync<'A, 'E, 'F
        when 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (member Serialize: 'F)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'E: (member Serialize: 'F)
        >
        (storage: IEventStore<'F>)
        (noOpEvent: 'E)
        (compensator: Option<Guid -> EventId -> TaskResult<string, string>>)
        : TaskResult<List<StreamSanitizationReport>, string> =
        taskResult {
            let! ids = storage.GetAggregateIdsAsync('A.Version, 'A.StorageName)
            let reports = ResizeArray<StreamSanitizationReport>()
            for id in ids do
                let! report = sanitizeAggregateStreamAsync<'A, 'E, 'F> storage id noOpEvent compensator
                reports.Add(report)
            return reports |> List.ofSeq
        }

    [<Obsolete("Use sanitizeAggregateStreamWithInitialStateAsync instead")>]
    let inline sanitizeAggregateStreamWithInitialState<'A, 'E, 'F
        when 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (member Serialize: 'F)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'E: (member Serialize: 'F)
        >
        (storage: IEventStore<'F>)
        (aggregateId: Guid)
        (initialState: 'A)
        (noOpEvent: 'E)
        (compensator: Option<Guid -> EventId -> Result<string, string>>)
        : Result<StreamSanitizationReport, string> =
        result {
            logger.LogInformation(sprintf "Starting aggregate sanitization for %s %s (aggregateId: %A)" 'A.Version 'A.StorageName aggregateId)
            let! rawEvents = storage.GetAggregateEventsAfterId 'A.Version 'A.StorageName aggregateId 0
            
            let mutable currentState = initialState
            let mutable actions = []
            let mutable lastEventId = 0
            
            for (eventId, rawEvent) in rawEvents do
                lastEventId <- eventId
                let deserializedOpt = 'E.Deserialize rawEvent
                
                let processResult =
                    match deserializedOpt with
                    | Ok ev -> ev.Process currentState
                    | Error err -> Error (sprintf "Deserialization failed for event %d: %s" eventId err)
                    
                match processResult with
                | Ok nextState ->
                    currentState <- nextState
                | Error errMsg ->
                    logger.LogWarning(sprintf "Anomaly detected in aggregate %A at event %d: %s. Applying NoOp patch." aggregateId eventId errMsg)
                    
                    let noOpSerialized = noOpEvent.Serialize
                    let! _ = storage.PatchEventWithNoOp 'A.Version 'A.StorageName aggregateId eventId noOpSerialized
                    
                    let! _ = storage.DeleteSnapshotsFromEventId 'A.Version 'A.StorageName aggregateId eventId
                    AggregateCache3.Instance.Clean aggregateId
                    DetailsCache.Instance.EvictDependentDetails aggregateId
                    
                    let evOpt = storage.TryGetEvent 'A.Version eventId 'A.StorageName
                    let corrIdOpt = evOpt |> Option.bind (fun e -> e.CorrelationId)
                    
                    actions <- PatchedToNoOp(eventId, corrIdOpt) :: actions
                    
                    match corrIdOpt with
                    | Some corrId ->
                        match storage.FindEventsByCorrelationId 'A.Version 'A.StorageName corrId with
                        | Ok relatedEvents ->
                            let relatedAggIds =
                                relatedEvents
                                |> List.map (fun (id, _, _) -> id)
                                |> List.distinct
                            for relId in relatedAggIds do
                                if relId <> aggregateId then
                                    AggregateCache3.Instance.Clean relId
                                    DetailsCache.Instance.EvictDependentDetails relId
                        | Error _ -> ()
                    | None -> ()

                    match corrIdOpt, compensator with
                    | Some corrId, Some comp ->
                        logger.LogInformation(sprintf "Triggering ex-post compensator for correlationId %A, poisoned event %d" corrId eventId)
                        match comp corrId eventId with
                        | Ok desc ->
                            actions <- Compensated(aggregateId, corrId, eventId, desc) :: actions
                        | Error compErr ->
                            logger.LogError(sprintf "Compensator failed for correlationId %A: %s" corrId compErr)
                    | _ -> ()
                    
                    match noOpEvent.Process currentState with
                    | Ok stateAfterNoOp ->
                        currentState <- stateAfterNoOp
                    | Error e ->
                        logger.LogCritical(sprintf "NoOp event itself failed to process! %s" e)
            
            // Always evict AggregateCache3 (L1 + L2) and dependent DetailsCache entries at the end of sanitization
            AggregateCache3.Instance.Clean aggregateId
            DetailsCache.Instance.EvictDependentDetails aggregateId
                
            let isClean = actions.IsEmpty
            let report =
                { AggregateId = Some aggregateId
                  StreamName = 'A.StorageName
                  Version = 'A.Version
                  IsClean = isClean
                  Actions = actions |> List.rev
                  FinalEventId = lastEventId }
            return report
        }

    [<Obsolete("Use sanitizeAggregateStreamAsync instead")>]
    let inline sanitizeAggregateStream<'A, 'E, 'F
        when 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (member Serialize: 'F)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'E: (member Serialize: 'F)
        >
        (storage: IEventStore<'F>)
        (aggregateId: Guid)
        (noOpEvent: 'E)
        (compensator: Option<Guid -> EventId -> Result<string, string>>)
        : Result<StreamSanitizationReport, string> =
        result {
            let! (_, firstSnap) = storage.TryGetFirstSnapshot 'A.Version 'A.StorageName aggregateId
            let! initialState = 'A.Deserialize firstSnap
            let! report = sanitizeAggregateStreamWithInitialState<'A, 'E, 'F> storage aggregateId initialState noOpEvent compensator
            AggregateCache3.Instance.Clean aggregateId
            DetailsCache.Instance.EvictDependentDetails aggregateId
            return report
        }

    [<Obsolete("Use sanitizeContextStreamAsync instead")>]
    let inline sanitizeContextStream<'A, 'E, 'F
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (member Serialize: 'F)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'E: (member Serialize: 'F)
        >
        (storage: IEventStore<'F>)
        (noOpEvent: 'E)
        : Result<StreamSanitizationReport, string> =
        result {
            logger.LogInformation(sprintf "Starting context sanitization for %s %s" 'A.Version 'A.StorageName)
            let! rawEvents = storage.GetEventsAfterId 'A.Version 0 'A.StorageName
            
            let mutable currentState = 'A.Zero
            let mutable actions = []
            let mutable lastEventId = 0
            
            for (eventId, rawEvent) in rawEvents do
                lastEventId <- eventId
                let deserializedOpt = 'E.Deserialize rawEvent
                
                let processResult =
                    match deserializedOpt with
                    | Ok ev -> ev.Process currentState
                    | Error err -> Error (sprintf "Deserialization failed for event %d: %s" eventId err)
                    
                match processResult with
                | Ok nextState ->
                    currentState <- nextState
                | Error errMsg ->
                    logger.LogWarning(sprintf "Anomaly detected in context %s at event %d: %s. Applying NoOp patch." 'A.StorageName eventId errMsg)
                    
                    let noOpSerialized = noOpEvent.Serialize
                    let! _ = storage.PatchContextEventWithNoOp 'A.Version 'A.StorageName eventId noOpSerialized
                    
                    StateCache2<'A>.Instance.Invalidate()
                    actions <- PatchedToNoOp(eventId, None) :: actions
                    
                    match noOpEvent.Process currentState with
                    | Ok stateAfterNoOp ->
                        currentState <- stateAfterNoOp
                    | Error e ->
                        logger.LogCritical(sprintf "NoOp event itself failed to process! %s" e)
                        
            if not actions.IsEmpty then
                StateCache2<'A>.Instance.Invalidate()
                
            let isClean = actions.IsEmpty
            let report =
                { AggregateId = None
                  StreamName = 'A.StorageName
                  Version = 'A.Version
                  IsClean = isClean
                  Actions = actions |> List.rev
                  FinalEventId = lastEventId }
            return report
        }

    [<Obsolete("Use sanitizeAllAggregatesAsync instead")>]
    let inline sanitizeAllAggregates<'A, 'E, 'F
        when 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (member Serialize: 'F)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'E: (member Serialize: 'F)
        >
        (storage: IEventStore<'F>)
        (noOpEvent: 'E)
        (compensator: Option<Guid -> EventId -> Result<string, string>>)
        : Result<List<StreamSanitizationReport>, string> =
        result {
            let! ids = storage.GetAggregateIds 'A.Version 'A.StorageName
            let reports = ResizeArray<StreamSanitizationReport>()
            for id in ids do
                let! report = sanitizeAggregateStream<'A, 'E, 'F> storage id noOpEvent compensator
                reports.Add(report)
            return reports |> List.ofSeq
        }

    type SanitizationMode =
        | SingleEventPatch
        | FullStreamSanitize

    let patchSingleAggregateEventByMetaAsync<'F>
        (storage: IEventStore<'F>)
        (version: string)
        (storageName: string)
        (aggregateId: Guid)
        (eventId: EventId)
        (noOpSerialized: 'F)
        (compensator: Option<Guid -> EventId -> TaskResult<string, string>>)
        : TaskResult<StreamSanitizationReport, string> =
        taskResult {
            logger.LogWarning(sprintf "Fast-patching single aggregate event %d for %s %s (aggregateId: %A) to NoOp." eventId version storageName aggregateId)
            let! _ = storage.PatchEventWithNoOpAsync(version, storageName, aggregateId, eventId, noOpSerialized)
            AggregateCache3.Instance.Clean aggregateId
            DetailsCache.Instance.EvictDependentDetails aggregateId

            let mutable actions = []
            match compensator with
            | None ->
                actions <- [ PatchedToNoOp(eventId, None) ]
            | Some comp ->
                let! evOpt = storage.TryGetEventAsync(version, eventId, storageName)
                let corrIdOpt = evOpt |> Option.bind (fun e -> e.CorrelationId)
                actions <- [ PatchedToNoOp(eventId, corrIdOpt) ]
                match corrIdOpt with
                | Some corrId ->
                    let! relatedEventsRes =
                        task {
                            let! r = storage.FindEventsByCorrelationIdAsync(version, storageName, corrId)
                            return Ok r
                        }
                    match relatedEventsRes with
                    | Ok relatedEvents ->
                        let relatedAggIds =
                            relatedEvents
                            |> List.map (fun (id, _, _) -> id)
                            |> List.distinct
                        for relId in relatedAggIds do
                            if relId <> aggregateId then
                                AggregateCache3.Instance.Clean relId
                                DetailsCache.Instance.EvictDependentDetails relId
                    | Error _ -> ()

                    logger.LogInformation(sprintf "Triggering ex-post compensator for correlationId %A, poisoned event %d" corrId eventId)
                    let! compRes =
                        task {
                            let! r = comp corrId eventId
                            return Ok r
                        }
                    match compRes with
                    | Ok desc ->
                        actions <- actions @ [ Compensated(aggregateId, corrId, eventId, desc) ]
                    | Error compErr ->
                        logger.LogError(sprintf "Compensator failed for correlationId %A: %s" corrId compErr)
                | None -> ()

            return
                { AggregateId = Some aggregateId
                  StreamName = storageName
                  Version = version
                  IsClean = false
                  Actions = actions
                  FinalEventId = eventId }
        }

    let inline patchSingleAggregateEventAsync<'A, 'E, 'F
        when 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'E :> Event<'A>
        and 'E: (member Serialize: 'F)
        >
        (storage: IEventStore<'F>)
        (aggregateId: Guid)
        (eventId: EventId)
        (noOpEvent: 'E)
        (compensator: Option<Guid -> EventId -> TaskResult<string, string>>)
        : TaskResult<StreamSanitizationReport, string> =
        patchSingleAggregateEventByMetaAsync storage 'A.Version 'A.StorageName aggregateId eventId noOpEvent.Serialize compensator

    let patchSingleContextEventByMetaAsync<'F>
        (storage: IEventStore<'F>)
        (version: string)
        (storageName: string)
        (eventId: EventId)
        (noOpSerialized: 'F)
        (invalidateCache: unit -> unit)
        : TaskResult<StreamSanitizationReport, string> =
        taskResult {
            logger.LogWarning(sprintf "Fast-patching single context event %d for %s %s to NoOp." eventId version storageName)
            let! _ = storage.PatchContextEventWithNoOpAsync(version, storageName, eventId, noOpSerialized)
            invalidateCache ()
            return
                { AggregateId = None
                  StreamName = storageName
                  Version = version
                  IsClean = false
                  Actions = [ PatchedToNoOp(eventId, None) ]
                  FinalEventId = eventId }
        }

    let inline patchSingleContextEventAsync<'A, 'E, 'F
        when 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'E :> Event<'A>
        and 'E: (member Serialize: 'F)
        >
        (storage: IEventStore<'F>)
        (eventId: EventId)
        (noOpEvent: 'E)
        : TaskResult<StreamSanitizationReport, string> =
        patchSingleContextEventByMetaAsync storage 'A.Version 'A.StorageName eventId noOpEvent.Serialize (fun () -> StateCache2<'A>.Instance.Invalidate())

    type private SanitizerMessage =
        | EnqueueAnomaly of AnomalyContext
        | Stop of AsyncReplyChannel<unit>

    /// Non-blocking, MailboxProcessor-backed singleton sanitizer that consumes evolve anomalies
    /// silently in the background, applies O(1) single-event NoOp patches by default (with optional
    /// fallback to full-stream sanitization), and cleans caches after each patch.
    type BackgroundSanitizer<'F>
        (
            storage: IEventStore<'F>,
            ?cooldownMs: int,
            ?maxQueueSize: int,
            ?onSanitized: StreamSanitizationReport -> unit,
            ?onError: AnomalyContext -> string -> unit
        ) =
        let effectiveCooldownMs = defaultArg cooldownMs sanitizerCooldownMs
        let effectiveMaxQueueSize = defaultArg maxQueueSize 10_000
        let handlers = ConcurrentDictionary<Type, AnomalyContext -> TaskResult<StreamSanitizationReport, string>>()
        let mutable isStopped = 0
        let mutable processedCount = 0L
        let mutable deduplicatedCount = 0L
        let mutable droppedCount = 0L

        let mailbox =
            MailboxProcessor<SanitizerMessage>.Start(fun inbox ->
                let recentPatches = System.Collections.Generic.Dictionary<Type * Option<Guid> * Option<EventId>, DateTimeOffset>()
                let pruneExpired (now: DateTimeOffset) =
                    if recentPatches.Count > 1000 && effectiveCooldownMs > 0 then
                        let expired =
                            recentPatches
                            |> Seq.filter (fun kv -> (now - kv.Value).TotalMilliseconds >= float effectiveCooldownMs)
                            |> Seq.map (fun kv -> kv.Key)
                            |> Seq.toList
                        for k in expired do
                            recentPatches.Remove(k) |> ignore

                let rec loop () =
                    async {
                        let! msg = inbox.Receive()
                        match msg with
                        | Stop reply ->
                            reply.Reply()
                            return ()
                        | EnqueueAnomaly ctx ->
                            let now = DateTimeOffset.UtcNow
                            pruneExpired now
                            let dedupeKey = (ctx.AggregateType, ctx.AggregateId, ctx.EventId)
                            let isDuplicate =
                                match recentPatches.TryGetValue(dedupeKey) with
                                | true, lastTime when effectiveCooldownMs > 0 && (now - lastTime).TotalMilliseconds < float effectiveCooldownMs ->
                                    true
                                | _ -> false

                            if isDuplicate then
                                Interlocked.Increment(&deduplicatedCount) |> ignore
                                logger.LogDebug(sprintf "BackgroundSanitizer deduplicated anomaly for %s (id: %A, event: %A)" ctx.AggregateType.Name ctx.AggregateId ctx.EventId)
                            else
                                match handlers.TryGetValue(ctx.AggregateType) with
                                | true, handler ->
                                    try
                                        let! res = handler ctx |> Async.AwaitTask
                                        match res with
                                        | Ok report ->
                                            recentPatches.[dedupeKey] <- DateTimeOffset.UtcNow
                                            Interlocked.Increment(&processedCount) |> ignore
                                            onSanitized |> Option.iter (fun cb -> cb report)
                                        | Error err ->
                                            logger.LogError(sprintf "BackgroundSanitizer failed for %s (id: %A, event: %A): %s" ctx.AggregateType.Name ctx.AggregateId ctx.EventId err)
                                            onError |> Option.iter (fun cb -> cb ctx err)
                                    with ex ->
                                        logger.LogError(ex, sprintf "BackgroundSanitizer exception for %s (id: %A, event: %A)" ctx.AggregateType.Name ctx.AggregateId ctx.EventId)
                                        onError |> Option.iter (fun cb -> cb ctx ex.Message)
                                | false, _ ->
                                    logger.LogWarning(sprintf "BackgroundSanitizer has no registered handler for type %s (id: %A, event: %A)" ctx.AggregateType.Name ctx.AggregateId ctx.EventId)
                            return! loop ()
                    }
                loop ()
            )

        member _.Storage = storage
        member _.ProcessedCount = Volatile.Read(&processedCount)
        member _.DeduplicatedCount = Volatile.Read(&deduplicatedCount)
        member _.DroppedCount = Volatile.Read(&droppedCount)
        member _.QueueLength = mailbox.CurrentQueueLength

        member _.RegisterHandler(aggregateType: Type, handler: AnomalyContext -> TaskResult<StreamSanitizationReport, string>) =
            handlers.[aggregateType] <- handler

        member inline this.RegisterAggregate<'A, 'E
            when 'A: (static member StorageName: string)
            and 'A: (static member Version: string)
            and 'A: (member Serialize: 'F)
            and 'A: (static member Deserialize: 'F -> Result<'A, string>)
            and 'E :> Event<'A>
            and 'E: (static member Deserialize: 'F -> Result<'E, string>)
            and 'E: (member Serialize: 'F)
            >
            (noOpEvent: 'E, ?mode: SanitizationMode, ?compensator: Guid -> EventId -> TaskResult<string, string>) =
            let effectiveMode = defaultArg mode SingleEventPatch
            let serializedNoOp = noOpEvent.Serialize
            let version = 'A.Version
            let storageName = 'A.StorageName
            let store = this.Storage
            let handler (ctx: AnomalyContext) =
                match ctx.AggregateId, ctx.EventId, effectiveMode with
                | Some aggId, Some evId, SingleEventPatch when evId > 0 ->
                    patchSingleAggregateEventByMetaAsync store version storageName aggId evId serializedNoOp compensator
                | Some aggId, _, _ ->
                    sanitizeAggregateStreamAsync<'A, 'E, 'F> store aggId noOpEvent compensator
                | None, _, _ ->
                    Task.FromResult(Error (sprintf "Missing AggregateId for aggregate type %s" typeof<'A>.Name))
            this.RegisterHandler(typeof<'A>, handler)

        member inline this.RegisterContext<'A, 'E
            when 'A: (static member Zero: 'A)
            and 'A: (static member StorageName: string)
            and 'A: (static member Version: string)
            and 'A: (member Serialize: 'F)
            and 'A: (static member Deserialize: 'F -> Result<'A, string>)
            and 'E :> Event<'A>
            and 'E: (static member Deserialize: 'F -> Result<'E, string>)
            and 'E: (member Serialize: 'F)
            >
            (noOpEvent: 'E, ?mode: SanitizationMode) =
            let effectiveMode = defaultArg mode SingleEventPatch
            let serializedNoOp = noOpEvent.Serialize
            let version = 'A.Version
            let storageName = 'A.StorageName
            let store = this.Storage
            let handler (ctx: AnomalyContext) =
                match ctx.EventId, effectiveMode with
                | Some evId, SingleEventPatch when evId > 0 ->
                    patchSingleContextEventByMetaAsync store version storageName evId serializedNoOp (fun () -> StateCache2<'A>.Instance.Invalidate())
                | _, _ ->
                    sanitizeContextStreamAsync<'A, 'E, 'F> store noOpEvent
            this.RegisterHandler(typeof<'A>, handler)

        member _.Enqueue(ctx: AnomalyContext) =
            if Volatile.Read(&isStopped) = 0 then
                if effectiveMaxQueueSize > 0 && mailbox.CurrentQueueLength >= effectiveMaxQueueSize then
                    Interlocked.Increment(&droppedCount) |> ignore
                    logger.LogWarning(sprintf "BackgroundSanitizer queue full (%d); dropping anomaly for %s (id: %A, event: %A)" effectiveMaxQueueSize ctx.AggregateType.Name ctx.AggregateId ctx.EventId)
                else
                    mailbox.Post(EnqueueAnomaly ctx)

        member this.AnomalyHandler : IEvolveAnomalyHandler =
            { new IEvolveAnomalyHandler with
                member _.OnEvolveAnomaly(ctx: AnomalyContext) =
                    this.Enqueue(ctx)
                member _.OnEvolveAnomalyAsync(ctx: AnomalyContext) =
                    this.Enqueue(ctx)
                    Task.FromResult(())
              interface IAnomalyRetryPolicy with
                member _.ShouldRetryInline = false }

        member this.InstallAsGlobalAnomalyHandler() =
            setAnomalyHandler(this.AnomalyHandler)

        member _.StopAsync() : Task =
            if Interlocked.Exchange(&isStopped, 1) = 0 then
                mailbox.PostAndAsyncReply(Stop) |> Async.StartAsTask :> Task
            else
                Task.CompletedTask

        interface IHostedService with
            member this.StartAsync(_ct: CancellationToken) =
                this.InstallAsGlobalAnomalyHandler()
                Task.CompletedTask
            member this.StopAsync(_ct: CancellationToken) =
                this.StopAsync()

        interface IDisposable with
            member this.Dispose() =
                this.StopAsync().GetAwaiter().GetResult()
