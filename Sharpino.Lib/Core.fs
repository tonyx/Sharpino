namespace Sharpino
open FSharp.Core
open FSharpPlus
open FSharpPlus.Data

module Core =

    type Event<'A> =
        abstract member Process: 'A -> Result<'A, string>

    type Undoer<'A, 'E> = 'A -> Result<List<'E>, string>

    type Command<'A, 'E when 'E :> Event<'A>> =
        abstract member Execute: 'A -> Result<List<'E>, string>
        abstract member Undo: Option<'A -> Result<Undoer<'A, 'E>, string>>

    let inline evolveUNforgivingErrors<'A, 'E when 'E :> Event<'A>> (h: 'A) (events: List<'E>) =
        events
        |> List.fold
            (fun (acc: Result<'A, string>) (e: 'E) ->
                match acc with
                    | Error err -> Error err 
                    | Ok h -> h |> e.Process
            ) (h |> Ok)

    let inline evolve<'A, 'E when 'E :> Event<'A>> (h: 'A) (events: List<'E>): Result<'A, string> =
        let rec evolveSkippingErrors (acc: Result<'A, string>) (events: List<'E>) (guard: 'A) =
            match acc, events with
            | Error err, _::es -> 
                // you may want to print or log this
                // printf "warning: %A\n" err
                evolveSkippingErrors (guard |> Ok) es guard
            | Error err, [] -> 
                // you may want to print or log this
                // printf "warning: %A\n" err
                guard |> Ok
            | Ok state, e::es ->
                let newGuard = state |> e.Process
                match newGuard with
                | Error err -> 
                    // use your favorite logging library here
                    // printf "warning: %A\n" err
                    evolveSkippingErrors (guard |> Ok) es guard
                | Ok h' ->
                    evolveSkippingErrors (h' |> Ok) es h'
            | Ok h, [] -> h |> Ok

        evolveSkippingErrors (h |> Ok) events h
