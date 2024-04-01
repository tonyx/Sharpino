
namespace Tonyx.Sharpino.Pub
open Tonyx.Sharpino.Pub.Dish
open Tonyx.Sharpino.Pub.DishEvents
open FSharpPlus
open FsToolkit.ErrorHandling
open Sharpino.Definitions
open Sharpino.Utils
open Sharpino.Core
open System
open Tonyx.Sharpino.Pub.Ingredient

module DishCommands =
    type DishCommands =
        | AddIngredientReceiptItem of IngredientReceiptItem
        | RemoveIngredientReceiptItem of Guid
        | UpdateIngredientReceiptItem of IngredientReceiptItem
        
            interface Command<Dish, DishEvents> with
                member this.Execute (x: Dish) =
                    match this with
                    | AddIngredientReceiptItem ingredientReceiptitem ->
                        x.AddIngredientReceiptItem ingredientReceiptitem
                        |> Result.map (fun _ -> [DishEvents.IngredientReceiptItemAdded ingredientReceiptitem])
                    | RemoveIngredientReceiptItem id ->
                        x.RemoveIngredientReceiptItem id
                        |> Result.map (fun _ -> [DishEvents.IngredientReceiptItemRemoved id])
                    | UpdateIngredientReceiptItem ingredientReceiptItem ->
                        x.UpdateIngredientReceiptItem ingredientReceiptItem
                        |> Result.map (fun _ -> [DishEvents.IngredientReceiptItemUpdated ingredientReceiptItem])
                            
                member this.Undoer = None

