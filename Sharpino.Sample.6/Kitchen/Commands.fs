
namespace Tonyx.Sharpino.Pub
open Tonyx.Sharpino.Pub.Kitchen
open Tonyx.Sharpino.Pub.KitchenEvents
open FSharpPlus
open FsToolkit.ErrorHandling
open Sharpino.Definitions
open Sharpino.Utils
open Sharpino.Core
open System
module KitchenCommands =
    type KitchenCommands =
        | AddDishReference of Guid
        | AddIngredientReference of Guid
        | RemoveIngredientReference of Guid
        | RemoveDishReference of Guid
        | AddSupplierReference of Guid
        | RemoveSupplierReference of Guid
            interface Command<Kitchen, KitchenEvents> with
                member this.Execute (x: Kitchen) =
                    match this with
                    | AddDishReference id ->
                        x.AddDishReference id
                        |> Result.map (fun _ -> [KitchenEvents.DishReferenceAdded id])
                    | AddIngredientReference id ->
                        x.AddIngredientReference id
                        |> Result.map (fun _ -> [KitchenEvents.IngredientReferenceAdded id])
                    | RemoveIngredientReference id ->
                        x.RemoveIngredientReference id
                        |> Result.map (fun _ -> [KitchenEvents.IngredientReferenceRemoved id])
                    | RemoveDishReference id ->
                        x.RemoveDishReference id
                        |> Result.map (fun _ -> [KitchenEvents.DishReferenceRemoved id])
                    | AddSupplierReference id ->
                        x.AddSupplierReference id
                        |> Result.map (fun _ -> [KitchenEvents.SupplierReferenceAdded id])
                    | RemoveSupplierReference id ->
                        x.RemoveSupplierReference id
                        |> Result.map (fun _ -> [KitchenEvents.SupplierReferenceRemoved id])
                        
                member this.Undoer = None

