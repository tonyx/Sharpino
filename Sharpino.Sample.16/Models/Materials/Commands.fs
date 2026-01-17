namespace Sharpino.Template.Models
open Sharpino.Template.Commons
open Sharpino.Core
open System.Text.Json
open FsToolkit.ErrorHandling
open sharpino.Template.Models

type MaterialCommands =
    | Consume of Quantity
    | Add of Quantity
    interface AggregateCommand<Material, MaterialEvents> with
        member this.Execute (state: Material) =
            match this with
            | Consume quantity ->
                state.Consume quantity
                |> Result.map (fun s -> s, [Consumed quantity])
            | Add quantity ->
                state.Add quantity
                |> Result.map (fun s -> s, [Added quantity])
        member this.Undoer =
            None