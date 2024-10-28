namespace Sharpino
open FSharp.Core
open FSharpPlus
open FSharpPlus.Data
open Sharpino.Definitions
open log4net
open log4net.Config
open System
open Sharpino.Utils

module Core =
    type AggregateId = Guid
    
    type StateViewer<'A> = unit -> Result<EventId * 'A, string>
    type AggregateViewer<'A> = Guid -> Result<EventId * 'A,string>
    
    type Aggregate<'F> =
        abstract member Id: Guid // use this one to be able to filter related events from same string
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
                    e.Process s
                )
            ) (h |> Ok)

    // [<TailCall>] // fight with .net version 7
    let rec evolveSkippingErrors (acc: Result<'A, string>) (events: List<'E>) (guard: 'A) =
        match acc, events with
        // if the accumulator is an error then skip it, and use the guard instead which was the 
        // latest valid value of the accumulator
        | Error err, _::es -> 
            evolveSkippingErrors (guard |> Ok) es guard
        // if the accumulator is error and the list is empty then we are at the end, and so we just
        // get the guard as the latest valid value of the accumulator
        | Error err, [] -> 
            guard |> Ok
        // if the accumulator is Ok and the list is not empty then we use a new guard as the value of the 
        // accumulator processed if is not error itself, otherwise we keep using the old guard
        | Ok state, e::es ->
            let newGuard = state |> (e :> Event<'A>).Process
            match newGuard with
            | Error err -> 
                evolveSkippingErrors (guard |> Ok) es guard
            | Ok h' ->
                evolveSkippingErrors (h' |> Ok) es h'
        | Ok h, [] -> h |> Ok
        
    let inline evolve<'A, 'E when 'E :> Event<'A>> (h: 'A) (events: List<'E>): Result<'A, string> =
        evolveSkippingErrors (h |> Ok) events h 
        // evolveUNforgivingErrors h events
