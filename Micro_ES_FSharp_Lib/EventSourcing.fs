namespace Tonyx.EventSourcing
open Tonyx.EventSourcing.Utils
open FSharp.Core
open FSharpPlus
open FSharpPlus.Data

module Core =
    let ceResult = CeResultBuilder()

    type Event<'A> =
        abstract member Process: 'A -> Result<'A, string>
    
    type Command<'A, 'E when 'E :> Event<'A>> =
        abstract member Execute: 'A -> Result<List<'E>, string>

    let inline evolve<'A, 'E when 'E :> Event<'A>> (h: 'A) (events: List<'E>) =
        events
        |> List.fold
            (fun (acc: Result<'A, string>) (e: 'E) ->
                match acc with
                    | Error err -> Error err // this should never happen
                    | Ok h -> h |> e.Process
            ) (h |> Ok)
