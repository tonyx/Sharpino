namespace Sharpino
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions
open FSharp.Core
open Sharpino.Definitions

module Core =
    let logger: Microsoft.Extensions.Logging.ILogger ref = ref NullLogger.Instance
    let setLogger (newLogger: Microsoft.Extensions.Logging.ILogger) =
        logger := newLogger
    type StateViewer<'A> = unit -> Result<EventId * 'A, string>
    type AggregateViewer<'A> = AggregateId -> Result<EventId * 'A,string>
   
    type Aggregate<'F> =
        abstract member Id: AggregateId 
        abstract member Serialize: 'F
    
    type Event<'A> =
        abstract member Process: 'A -> Result<'A, string>

    type CommandUndoer<'A, 'E> =  Option<'A -> StateViewer<'A> -> Result<unit -> Result<List<'E>, string>, string>>
    type AggregateCommandUndoer<'A, 'E> = Option<'A -> AggregateViewer<'A> -> Result<unit -> Result<List<'E>, string>, string>>
    type Command<'A, 'E when 'E :> Event<'A>> =
        abstract member Execute: 'A -> Result<'A * List<'E>, string>
        abstract member Undoer: CommandUndoer<'A, 'E>
        
    type AggregateCommand<'A, 'E when 'E :> Event<'A>> =
        abstract member Execute: 'A -> Result<'A * List<'E>, string>
        abstract member Undoer: AggregateCommandUndoer<'A, 'E>

    let inline evolveUNforgivingErrors<'A, 'E when 'E :> Event<'A>> (h: 'A) (events: List<'E>) =
        events
        |> List.fold
            (fun (acc: Result<'A, string>) (e: 'E) ->
                acc |> Result.bind (fun s ->
                    (e.Process s 
                        |> Result.mapError
                            (fun x ->
                                let msg = sprintf "Error processing event %A: %s" e x
                                logger.Value.LogError msg
                                msg
                            )
                    )
                )
            ) (h |> Ok)

    [<TailCall>]
    let rec evolveSkippingErrors (acc: Result<'A, string>) (events: List<'E>) (guard: 'A) =
        match acc, events with
        // if the accumulator is an error then skip it, and use the guard instead which was the 
        // latest valid value of the accumulator
        | Error err, _::es ->
            logger.Value.LogInformation (sprintf "Skipping error: %s" err)
            evolveSkippingErrors (guard |> Ok) es guard
        // if the accumulator is error and the list is empty then we are at the end, and so we just
        // get the guard as the latest valid value of the accumulator
        | Error err, [] -> 
            logger.Value.LogInformation (sprintf "Skipping error: %s" err)
            guard |> Ok
        // if the accumulator is Ok and the list is not empty then we use a new guard as the value of the 
        // accumulator processed if is not error itself, otherwise we keep using the old guard
        | Ok state, e::es ->
            let newGuard = state |> (e :> Event<'A>).Process
            match newGuard with
            | Error err -> 
                logger.Value.LogInformation (sprintf "Skipping error: %s" err)
                evolveSkippingErrors (guard |> Ok) es guard
            | Ok h' ->
                evolveSkippingErrors (h' |> Ok) es h'
        | Ok h, [] -> h |> Ok
        
    let inline evolve<'A, 'E when 'E :> Event<'A>> (h: 'A) (events: List<'E>): Result<'A, string> =
        #if EVOLVE_SKIPS_ERRORS
            evolveSkippingErrors (h |> Ok) events h 
        #else    
            evolveUNforgivingErrors h events
        #endif     
