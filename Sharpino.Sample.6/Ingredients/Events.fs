
namespace Tonyx.Sharpino.Pub
open FSharpPlus
open FsToolkit.ErrorHandling
open Tonyx.Sharpino.Pub.Commons
open Sharpino.Definitions
open Sharpino.Utils
open Sharpino.Core
open System

type IngredientEvents =
    | AddIngredientType of IngredientType
    | RemoveIngredientType of IngredientType
    | AddMeasureType of MeasureType
    | RemoveMeasureType of MeasureType
        interface Event<Ingredient> with
            member this.Process (x: Ingredient) =
                match this with
                | AddIngredientType t ->
                    x.AddIngredientType t
                | RemoveIngredientType t ->
                    x.RemoveIngredientType t
                | RemoveMeasureType t ->
                    x.RemoveMeasureType t
                | AddMeasureType t ->
                    x.AddMeasureType t     
    static member Deserialize  json =
        serializer.Deserialize<IngredientEvents> json
    member this.Serialize =
        this
        |> serializer.Serialize    
