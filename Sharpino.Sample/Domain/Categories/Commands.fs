
namespace Sharpino.Sample.Categories

open System
open Sharpino.Core

open Sharpino.Sample.Entities.Categories
open Sharpino.Sample.CategoriesCluster
open Sharpino.Sample.Categories.CategoriesEvents
open Sharpino.Sample.Shared.Entities

module CategoriesCommands =
    type CategoryCommand =
        | AddCategory of Category
        | RemoveCategory of Guid
        | AddCategories of List<Category>

        interface Command<CategoriesCluster, CategoryEvent> with
            member this.Execute (x: CategoriesCluster) = 
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