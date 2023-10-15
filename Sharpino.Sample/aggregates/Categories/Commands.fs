
namespace Sharpino.Sample.Categories

open System
open Sharpino.Core
open Sharpino.Cache

open Sharpino.Sample.Entities.Categories
open Sharpino.Sample.CategoriesAggregate
open Sharpino.Sample.Categories.CategoriesEvents
open type Sharpino.Cache.EventCache<CategoriesAggregate>

module CategoriesCommands =
    type CategoryCommand =
        | AddCategory of Category
        | RemoveCategory of Guid
        | AddCategories of List<Category>

        interface Command<CategoriesAggregate, CategoryEvent> with
            member this.Execute (x: CategoriesAggregate) = 
                match this with
                | AddCategory c ->
                    Instance.Memoize (fun () -> x.AddCategory c) (x, [CategoryAdded c]) 
                    |> Result.map (fun _ -> [CategoryAdded c])
                | RemoveCategory g ->
                    Instance.Memoize (fun () -> x.RemoveCategory g) (x, [CategoryRemoved g]) 
                    |> Result.map (fun _ -> [CategoryRemoved g])
                | AddCategories cs ->
                    Instance.Memoize (fun () -> x.AddCategories cs) (x, [CategoriesAdded cs])
                    |> Result.map (fun _ -> [CategoriesAdded cs])
            member this.Undoer = None