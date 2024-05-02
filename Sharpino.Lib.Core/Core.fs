namespace Sharpino
open FSharp.Core
open FSharpPlus
open FSharpPlus.Data
open log4net
open log4net.Config
open System
open Sharpino.Utils

module Core =
    type AggregateId = Guid
    // let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    // enable for quick debugging
    // log4net.Config.BasicConfigurator.Configure() |> ignore
    // adding types for object based (no class level) aggregate type
    
    type Aggregate<'F> =
        abstract member StateId: Guid
        abstract member Id: Guid // use this one to be able to filter related events from same string
        abstract member Serialize: 'F
        abstract member Lock: obj
    
    type Event<'A> =
        abstract member Process: 'A -> Result<'A, string>

    type Undoer<'A, 'E when 'E :> Event<'A>> = 'A -> Result<List<'E>, string>

    type Command<'A, 'E when 'E :> Event<'A>> =
        abstract member Execute: 'A -> Result<List<'E>, string>
        abstract member Undoer: Option<'A -> Result<Undoer<'A, 'E>, string>>

    let inline evolveUNforgivingErrors<'A, 'E when 'E :> Event<'A>> (h: 'A) (events: List<'E>) =
        events
        |> List.fold
            (fun (acc: Result<'A, string>) (e: 'E) ->
                acc |> Result.bind (fun acc ->
                    e.Process acc
                )
            ) (h |> Ok)

    [<TailCall>]
    let rec evolveSkippingErrors (acc: Result<'A, string>) (events: List<'E>) (guard: 'A) =
        match acc, events with
        // if the accumulator is an error then skip it, and use the guard instead which was the 
        // latest valid value of the accumulator
        | Error err, _::es -> 
            // log.Info (sprintf "warning 1: %A" err)
            evolveSkippingErrors (guard |> Ok) es guard
        // if the accumulator is error and the list is empty then we are at the end, and so we just
        // get the guard as the latest valid value of the accumulator
        | Error err, [] -> 
            // log.Info (sprintf "warning 2: %An" err)
            guard |> Ok
        // if the accumulator is Ok and the list is not empty then we use a new guard as the value of the 
        // accumulator processed if is not error itself, otherwise we keep using the old guard
        | Ok state, e::es ->
            let newGuard = state |> (e :> Event<'A>).Process
            match newGuard with
            | Error err -> 
                // log.Info (sprintf "warning 3: %A" err)
                evolveSkippingErrors (guard |> Ok) es guard
            | Ok h' ->
                evolveSkippingErrors (h' |> Ok) es h'
        | Ok h, [] -> h |> Ok

    let inline evolve<'A, 'E when 'E :> Event<'A>> (h: 'A) (events: List<'E>): Result<'A, string> =
        evolveSkippingErrors (h |> Ok) events h 
