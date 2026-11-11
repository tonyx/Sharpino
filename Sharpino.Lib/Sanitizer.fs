namespace Sharpino

open System
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Sharpino.Core
open Sharpino.Definitions
open Sharpino.Storage
open Sharpino.CommandHandler
open Sharpino.Cache
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
