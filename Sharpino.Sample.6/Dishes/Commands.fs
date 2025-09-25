
namespace Tonyx.Sharpino.Pub
open FSharpPlus
open FsToolkit.ErrorHandling
open Sharpino.Definitions
open Sharpino.Utils
open Sharpino.Core
open System

type DishCommands =
    | AddIngredient of Guid
    | RemoveIngredient of Guid
        interface AggregateCommand<Dish, DishEvents> with
            member this.Execute (x: Dish) =
                match this with
                | AddIngredient id ->
                    x.AddIngredient id
                    |> Result.map (fun s -> (s, [DishEvents.IngredientAdded id]))
                | RemoveIngredient id ->
                    x.RemoveIngredient id 
                    |> Result.map (fun s -> (s, [DishEvents.IngredientRemoved id]))
            member this.Undoer = None

