
namespace Sharpino.Sample.Categories

open System
open Sharpino.Core

open Sharpino.Sample.Entities.Categories
open Sharpino.Sample.CategoriesAggregate
open Sharpino.Sample.Categories.CategoriesEvents

module CategoriesCommands =
    type CategoryCommand =
        | AddCategory of Category
        | RemoveCategory of Guid
        | AddCategories of List<Category>

        interface Command<CategoriesAggregate, CategoryEvent> with
            member this.Execute (x: CategoriesAggregate) = 
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
            member this.Undoer = None