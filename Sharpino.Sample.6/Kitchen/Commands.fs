
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
                        |> Result.map (fun x -> (x, [KitchenEvents.DishReferenceAdded id]))
                    | AddIngredientReference id ->
                        let res = x.AddIngredientReference id
                        res
                        |> Result.map (fun _ -> (res.OkValue, [KitchenEvents.IngredientReferenceAdded id]))
                    | RemoveIngredientReference id ->
                        let res = x.RemoveIngredientReference id
                        res
                        |> Result.map (fun _ -> (res.OkValue, [KitchenEvents.IngredientReferenceRemoved id]))
                    | RemoveDishReference id ->
                        let res = x.RemoveDishReference id
                        res
                        |> Result.map (fun _ -> (res.OkValue, [KitchenEvents.DishReferenceRemoved id]))
                    | AddSupplierReference id ->
                        let res = x.AddSupplierReference id
                        res 
                        |> Result.map (fun _ -> (res.OkValue, [KitchenEvents.SupplierReferenceAdded id]))
                    | RemoveSupplierReference id ->
                        let res = x.RemoveSupplierReference id
                        res
                        |> Result.map (fun _ -> (res.OkValue, [KitchenEvents.SupplierReferenceRemoved id]))
                member this.Undoer = None
