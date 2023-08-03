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

    let inline evolve<'A, 'E when 'E :> Event<'A>> (h: 'A) (events: List<'E>) =
        events
        |> List.fold
            (fun (acc: Result<'A, string>) (e: 'E) ->
                match acc with
                    | Error err -> Error err 
                    | Ok h -> h |> e.Process
            ) (h |> Ok)
