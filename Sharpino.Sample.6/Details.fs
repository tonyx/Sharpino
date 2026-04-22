namespace Tonyx.Sharpino.Pub

open Sharpino.Cache
open Sharpino.CommandHandler
open Tonyx.Sharpino.Pub.Commons 
open Sharpino.Definitions
open System
open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Sharpino
open Sharpino.Lib.Core.Commons
open Sharpino.Storage
open Sharpino.Core
open Sharpino.Utils
open System.Threading
open System.Threading.Tasks

module Details =
    type DishDetails =
        {
            Dish: Dish
            Ingredients: List<Ingredient>
            Refresher: unit -> Result<Dish * List<Ingredient>, string>
        }
        member this.Refresh () =
            result {
                let! dish, ingredients = this.Refresher()
                return { this with Dish = dish; Ingredients = ingredients }
            }
        member this.RefreshAsync (_: Option<CancellationToken>) =
            this.Refresh() |> Task.FromResult

        interface RefreshableAsync<DishDetails> with
            member this.RefreshAsync ct =
                this.RefreshAsync ct
                
                
                