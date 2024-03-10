
namespace Tonyx.Sharpino.Pub
open Tonyx.Sharpino.Pub.Dish
open FSharpPlus
open FsToolkit.ErrorHandling
open Sharpino.Definitions
open Sharpino.Utils
open Sharpino.Core
open System

module DishEvents =
    type DishEvents =
        | IngredientAdded of Guid
        | IngredientRemoved of Guid

            interface Event<Dish> with
                member this.Process (x: Dish) =
                    match this with
                    | IngredientAdded id ->
                        x.AddIngredient id
                    | IngredientRemoved id ->
                        x.RemoveIngredient id
        static member Deserialize (serializer: ISerializer, json: Json): Result<DishEvents, string>  =
            serializer.Deserialize<DishEvents> json    
        member this.Serialize (serializer: ISerializer) =   
            this
            |> serializer.Serialize



