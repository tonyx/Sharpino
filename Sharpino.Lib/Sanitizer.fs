namespace Sharpino

open System
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
                    
                    let evOpt = storage.TryGetEvent 'A.Version eventId 'A.StorageName
                    let corrIdOpt = evOpt |> Option.bind (fun e -> e.CorrelationId)
                    
                    actions <- PatchedToNoOp(eventId, corrIdOpt) :: actions
                    
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
            
            if not actions.IsEmpty then
                AggregateCache3.Instance.Clean aggregateId
                
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
            return! sanitizeAggregateStreamWithInitialState<'A, 'E, 'F> storage aggregateId initialState noOpEvent compensator
        }

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
