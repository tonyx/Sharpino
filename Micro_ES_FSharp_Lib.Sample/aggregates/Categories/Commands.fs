
namespace Sharpino.EventSourcing.Sample.Categories

open System
open Sharpino.EventSourcing.Core
open Sharpino.EventSourcing.Cache

open Sharpino.EventSourcing.Sample.Todos.Models.CategoriesModel
open Sharpino.EventSourcing.Sample.Categories.CategoriesEvents

module CategoriesCommands =
    open Sharpino.EventSourcing.Sample.CategoriesAggregate
    type CategoryCommand =
        | AddCategory of Category
        | RemoveCategory of Guid
        | AddCategories of List<Category>

        interface Command<CategoriesAggregate, CategoryEvent> with
            member this.Execute (x: CategoriesAggregate) =
                match this with
                | AddCategory c ->
                    match
                        EventCache<CategoriesAggregate>.Instance.Memoize (fun () -> x.AddCategory c) (x, [CategoryAdded c]) with
                        | Ok _ -> [CategoryAdded c] |> Ok
                        | Error x -> x |> Error
                | RemoveCategory g ->
                    match
                        EventCache<CategoriesAggregate>.Instance.Memoize (fun () -> x.RemoveCategory g) (x, [CategoryRemoved g]) with
                        | Ok _ -> [CategoryRemoved g] |> Ok
                        | Error x -> x |> Error
                | AddCategories cs ->
                    match
                        EventCache<CategoriesAggregate>.Instance.Memoize (fun () -> x.AddCategories cs) (x, [CategoriesAdded cs]) with
                        | Ok _ -> [CategoriesAdded cs] |> Ok
                        | Error x -> x |> Error