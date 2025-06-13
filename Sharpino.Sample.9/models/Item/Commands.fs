namespace Sharpino.Sample._9
open Sharpino.Sample._9.Item
open Sharpino.Sample._9.Events
open Sharpino.Core  
module Commands =

    module ItemCommands =
        type ItemCommands =
            | Rename of string
            | ChangeDescription of string
            | DecrementReferenceCounter of int
            | IncrementReferenceCounter of int
            interface AggregateCommand<Item, ItemEvent> with
                member this.Execute (item: Item) =
                    match this with
                    | Rename name ->
                        item.Rename name
                        |> Result.map (fun x -> (x, [Renamed x.Name]))
                    | ChangeDescription description ->
                        item.ChangeDescription description
                        |> Result.map (fun x -> (x, [ChangedDescription x.Description]))
                    | DecrementReferenceCounter i ->
                        item.DecrementReferenceCounter i 
                        |> Result.map (fun x -> (x, [ReferenceCounterDecremented i]))
                    | IncrementReferenceCounter i ->
                        item.IncrementReferenceCounter i
                        |> Result.map (fun x -> (x, [ReferenceCounterIncremented i] ))
                member this.Undoer =
                    None        