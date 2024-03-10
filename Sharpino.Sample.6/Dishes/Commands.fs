
namespace Tonyx.Sharpino.Pub
open Tonyx.Sharpino.Pub.Dish
open Tonyx.Sharpino.Pub.DishEvents
open FSharpPlus
open FsToolkit.ErrorHandling
open Sharpino.Definitions
open Sharpino.Utils
open Sharpino.Core
open System

module DishCommands =
    type DishCommands =
        | AddIngredient of Guid
        | RemoveIngredient of Guid
            interface Command<Dish, DishEvents> with
                member this.Execute (x: Dish) =
                    match this with
                    | AddIngredient id ->
                        x.AddIngredient id
                        |> Result.map (fun _ -> [DishEvents.IngredientAdded id])
                    | RemoveIngredient id ->
                        x.RemoveIngredient id
                        |> Result.map (fun _ -> [DishEvents.IngredientRemoved id])
                member this.Undoer = None

