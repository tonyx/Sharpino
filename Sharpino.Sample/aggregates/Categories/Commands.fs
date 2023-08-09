
namespace Sharpino.Sample.Categories

open System
open Sharpino.Core
open Sharpino.Cache

open Sharpino.Sample.Models.CategoriesModel
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
                    EventCache<CategoriesAggregate>.Instance.Memoize (fun () -> x.AddCategory c) (x, [CategoryAdded c]) 
                    |> Result.map (fun _ -> [CategoryAdded c])
                | RemoveCategory g ->
                    EventCache<CategoriesAggregate>.Instance.Memoize (fun () -> x.RemoveCategory g) (x, [CategoryRemoved g]) 
                    |> Result.map (fun _ -> [CategoryRemoved g])
                | AddCategories cs ->
                    EventCache<CategoriesAggregate>.Instance.Memoize (fun () -> x.AddCategories cs) (x, [CategoriesAdded cs])
                    |> Result.map (fun _ -> [CategoriesAdded cs])
            member this.Undoer = None