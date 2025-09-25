
namespace Tonyx.Sharpino.Pub
open Tonyx.Sharpino.Pub.Commons
open FSharpPlus
open FsToolkit.ErrorHandling
open Sharpino.Definitions
open Sharpino.Utils
open Sharpino.Core
open System

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
    static member Deserialize x =
        serializer.Deserialize<DishEvents> x    
    member this.Serialize =
        this
        |> serializer.Serialize



