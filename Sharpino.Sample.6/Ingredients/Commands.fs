namespace Tonyx.Sharpino.Pub
open FSharpPlus
open FsToolkit.ErrorHandling
open Tonyx.Sharpino.Pub.Ingredient
open Tonyx.Sharpino.Pub.IngredientEvents
open Sharpino.Definitions
open Sharpino.Utils
open Sharpino.Core
open System


module IngredientCommands =
    type IngredientCommands =
        | AddIngredientType of IngredientType
        | RemoveIngredientType of IngredientType
        | AddMeasureType of MeasureType
        | RemoveMeasureType of MeasureType
            interface AggregateCommand<Ingredient, IngredientEvents> with
                member this.Execute (x: Ingredient) =
                    match this with
                    | AddIngredientType t ->
                        x.AddIngredientType t
                        |> Result.map (fun x -> (x, [IngredientEvents.AddIngredientType t]))
                    | RemoveIngredientType t ->
                        x.RemoveIngredientType t
                        |> Result.map (fun x -> (x, [IngredientEvents.RemoveIngredientType t]))
                    | AddMeasureType t ->
                        x.AddMeasureType t
                        |> Result.map (fun x -> (x, [IngredientEvents.AddMeasureType t]))
                    | RemoveMeasureType t ->
                        x.RemoveMeasureType t
                        |> Result.map (fun x -> (x, [IngredientEvents.RemoveMeasureType t]))
                member this.Undoer = None