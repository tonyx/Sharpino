namespace Tonyx.EventSourcing
open FSharp.Core
open FSharpPlus
open FSharpPlus.Data

module Core =

    type Event<'A> =
        abstract member Process: 'A -> Result<'A, string>
    
    type Command<'A, 'E when 'E :> Event<'A>> =
        abstract member Execute: 'A -> Result<List<'E>, string>

    let inline evolve<'A, 'E when 'E :> Event<'A>> (h: 'A) (events: List<'E>) =
        events
        |> List.fold
            (fun (acc: Result<'A, string>) (e: 'E) ->
                match acc with
                    | Error err -> Error err // this should never happen for events that are already persisted
                    | Ok h -> h |> e.Process
            ) (h |> Ok)
