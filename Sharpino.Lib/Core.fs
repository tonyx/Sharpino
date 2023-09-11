namespace Sharpino
open FSharp.Core
open FSharpPlus
open FSharpPlus.Data

module Core =

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
                match acc with
                    | Error err -> Error err 
                    | Ok h -> h |> e.Process
            ) (h |> Ok)

    // may be this can be refactored:
    let inline evolve<'A, 'E when 'E :> Event<'A>> (h: 'A) (events: List<'E>): Result<'A, string> =
        let rec evolveSkippingErrors (acc: Result<'A, string>) (events: List<'E>) (guard: 'A) =
            match acc, events with
            // if the accumulator is an error then skip it, and use the guard instead which was the 
            // latest valid value of the accumulator
            | Error err, _::es -> 
                // you may want to print or log this
                printf "warning 1: %A\n" err
                evolveSkippingErrors (guard |> Ok) es guard
            // if the accumulator is error and the list is empty then we are at the end, and so we just
            // get the guard as the latest valid value of the accumulator
            | Error err, [] -> 
                // you may want to print or log this
                printf "warning 2: %A\n" err
                guard |> Ok
            // if the accumulator is Ok and the list is not empty then we use a new guard as the value of the 
            // accumulator processed if is not error itself, otherwise we keep using the old guard
            | Ok state, e::es ->
                let newGuard = state |> e.Process
                match newGuard with
                | Error err -> 
                    // use your favorite logging library here
                    printf "warning 3: %A\n" err
                    evolveSkippingErrors (guard |> Ok) es guard
                | Ok h' ->
                    evolveSkippingErrors (h' |> Ok) es h'
            | Ok h, [] -> h |> Ok

        evolveSkippingErrors (h |> Ok) events h