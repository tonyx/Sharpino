namespace sharpino.Template.Models
open Sharpino.Template.Commons
open Sharpino.Core
open System.Text.Json
open FsToolkit.ErrorHandling
open sharpino.Template.Models

type WorkOrderCommands =
    | Start of ProductId
    | Complete of ProductId
    | Fail of ProductId * Quantity
    interface AggregateCommand<WorkOrder, WorkOrderEvents> with
        member this.Execute (state: WorkOrder) =
            match this with
            | Start productId ->
                state.Start productId
                |> Result.map (fun s -> s, [Started productId])
            | Complete productId ->
                state.Complete productId
                |> Result.map (fun s -> s, [Completed productId])
            | Fail (productId, quantity) ->
                state.Fail productId quantity
                |> Result.map (fun s -> s, [Failed (productId, quantity)])
                
        member this.Undoer = None        