namespace Sharpino

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Console
open FsToolkit.ErrorHandling
open FSharp.Core
open Sharpino.Definitions

module Core =
    let builder = Host.CreateApplicationBuilder()
    let config = builder.Configuration
    let logger = builder.Services.BuildServiceProvider().GetRequiredService<ILoggerFactory>().CreateLogger("Sharpino.Core")

    [<Obsolete("This method is deprecated and will be removed in a future version. Please config log on appsettings.json")>]
    let setLogger (newLogger: Microsoft.Extensions.Logging.ILogger) =
        ()

    type EvolveError = {
        EventId: EventId
        Message: string
    }

    type AnomalyContext = {
        AggregateId: Option<AggregateId>
        EventId: Option<EventId>
        ErrorMessage: string
        AggregateType: Type
    }

    type IEvolveAnomalyHandler =
        [<Obsolete("Use OnEvolveAnomalyAsync instead")>]
        abstract member OnEvolveAnomaly: AnomalyContext -> unit
        abstract member OnEvolveAnomalyAsync: AnomalyContext -> Task<unit>

    type DefaultLoggingAnomalyHandler(logger: Microsoft.Extensions.Logging.ILogger) =
        interface IEvolveAnomalyHandler with
            member _.OnEvolveAnomaly(ctx) =
                logger.LogCritical(sprintf "Evolve anomaly on aggregate type %s (id: %A, event: %A): %s" ctx.AggregateType.Name ctx.AggregateId ctx.EventId ctx.ErrorMessage)
            member _.OnEvolveAnomalyAsync(ctx) =
                logger.LogCritical(sprintf "Evolve anomaly on aggregate type %s (id: %A, event: %A): %s" ctx.AggregateType.Name ctx.AggregateId ctx.EventId ctx.ErrorMessage)
                Task.FromResult(())

    let mutable currentAnomalyHandler: IEvolveAnomalyHandler = DefaultLoggingAnomalyHandler(logger)
    let getAnomalyHandler () = currentAnomalyHandler
    let setAnomalyHandler (handler: IEvolveAnomalyHandler) =
        currentAnomalyHandler <- handler
    type StateViewer<'A> = unit -> Result<EventId * 'A, string>
    type AggregateViewer<'A> = AggregateId -> Result<EventId * 'A,string>
    type AggregateViewerAsync<'A> = AggregateId -> Option<CancellationToken> -> TaskResult<EventId * 'A,string>
   
    [<Obsolete("no need for aggregates interface")>]
    type Aggregate<'F> =
        abstract member Id: AggregateId 
        abstract member Serialize: 'F
    
    type Event<'A> =
        abstract member Process: 'A -> Result<'A, string>

    type CommandUndoer<'A, 'E> =  Option<'A -> StateViewer<'A> -> Result<unit -> Result<List<'E>, string>, string>>
    type AggregateCommandUndoer<'A, 'E> = Option<'A -> AggregateViewer<'A> -> Result<unit -> Result<'A * List<'E>, string>, string>>
    type Command<'A, 'E when 'E :> Event<'A>> =
        abstract member Execute: 'A -> Result<'A * List<'E>, string>
        abstract member Undoer: CommandUndoer<'A, 'E>
        
    type AggregateCommand<'A, 'E when 'E :> Event<'A>> =
        abstract member Execute: 'A -> Result<'A * List<'E>, string>
        abstract member Undoer: AggregateCommandUndoer<'A, 'E>

    let inline evolveUNforgivingErrorsWithContext<'A, 'E when 'E :> Event<'A>> (h: 'A) (events: List<EventId * 'E>) : Result<'A, EvolveError> =
        events
        |> List.fold
            (fun (acc: Result<'A, EvolveError>) (eventId: EventId, e: 'E) ->
                acc |> Result.bind (fun s ->
                    e.Process s 
                    |> Result.mapError
                        (fun x ->
                            { EventId = eventId
                              Message = sprintf "Error processing event %A: %s" e x }
                        )
                )
            ) (h |> Ok)

    let inline evolveUNforgivingErrors<'A, 'E when 'E :> Event<'A>> (h: 'A) (events: List<'E>) : Result<'A, string> =
        events
        |> List.map (fun e -> (0, e))
        |> evolveUNforgivingErrorsWithContext<'A, 'E> h
        |> Result.mapError (fun err -> err.Message)

    [<Obsolete("evolveSkippingErrors is deprecated in favor of strict evolve and ex-post sanitization")>]
    [<TailCall>]
    let rec evolveSkippingErrors (acc: Result<'A, string>) (events: List<'E>) (guard: 'A) =
        match acc, events with
        // if the accumulator is an error then skip it, and use the guard instead which was the 
        // latest valid value of the accumulator
        | Error err, _::es ->
            logger.LogCritical (sprintf "Skipping error: %s" err)
            evolveSkippingErrors (guard |> Ok) es guard
        // if the accumulator is error and the list is empty then we are at the end, and so we just
        // get the guard as the latest valid value of the accumulator
        | Error err, [] -> 
            logger.LogCritical (sprintf "Skipping error: %s" err)
            guard |> Ok
        // if the accumulator is Ok and the list is not empty then we use a new guard as the value of the 
        // accumulator processed if is not error itself, otherwise we keep using the old guard
        | Ok state, e::es ->
            let newGuard = state |> (e :> Event<'A>).Process
            match newGuard with
            | Error err -> 
                logger.LogCritical (sprintf "Skipping error: %s" err)
                evolveSkippingErrors (guard |> Ok) es guard
            | Ok h' ->
                evolveSkippingErrors (h' |> Ok) es h'
        | Ok h, [] -> h |> Ok
        
    let inline evolve<'A, 'E when 'E :> Event<'A>> (h: 'A) (events: List<'E>): Result<'A, string> =
        evolveUNforgivingErrors h events

    let inline evolveWithContext<'A, 'E when 'E :> Event<'A>> (h: 'A) (events: List<EventId * 'E>): Result<'A, EvolveError> =
        evolveUNforgivingErrorsWithContext<'A, 'E> h events
