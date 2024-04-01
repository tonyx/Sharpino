
namespace Tonyx.Sharpino.Pub
open Tonyx.Sharpino.Pub.Dish
open FSharpPlus
open FsToolkit.ErrorHandling
open Sharpino.Definitions
open Sharpino.Utils
open Sharpino.Core
open System
open Tonyx.Sharpino.Pub.Ingredient

module DishEvents =
    type DishEvents =
        | IngredientReceiptItemAdded of IngredientReceiptItem
        | IngredientReceiptItemRemoved of Guid
        | IngredientReceiptItemUpdated of IngredientReceiptItem

            interface Event<Dish> with
                member this.Process (x: Dish) =
                    match this with
                    | IngredientReceiptItemAdded item ->
                        x.AddIngredientReceiptItem item
                    | IngredientReceiptItemRemoved id ->
                        x.RemoveIngredientReceiptItem id
                    | IngredientReceiptItemUpdated item ->
                        x.UpdateIngredientReceiptItem item     
                          
        static member Deserialize (serializer: ISerializer, json: Json): Result<DishEvents, string>  =
            serializer.Deserialize<DishEvents> json    
        member this.Serialize (serializer: ISerializer) =   
            this
            |> serializer.Serialize



