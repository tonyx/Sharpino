
namespace Sharpino.Sample.Categories

open System
open Sharpino.Core

open Sharpino.Sample.Entities.Categories
open Sharpino.Sample.CategoriesContext
open Sharpino.Sample.Categories.CategoriesEvents
open Sharpino.Sample.Shared.Entities

module CategoriesCommands =
    type CategoryCommand =
        | AddCategory of Category
        | RemoveCategory of Guid
        | AddCategories of List<Category>
        | Ping of unit

        interface Command<CategoriesContext, CategoryEvent> with
            member this.Execute (x: CategoriesContext) = 
                match this with
                | AddCategory c ->
                    x.AddCategory c
                    |> Result.map (fun _ -> [CategoryAdded c])
                | RemoveCategory g ->
                    x.RemoveCategory g
                    |> Result.map (fun _ -> [CategoryRemoved g])
                | AddCategories cs ->
                    x.AddCategories cs
                    |> Result.map (fun _ -> [CategoriesAdded cs])
                | Ping () ->
                    x.Ping()
                    |> Result.map (fun _ -> [PingDone ()])
            member this.Undoer = None