
namespace Tonyx.EventSourcing.Sample_02.Categories

open System
open Tonyx.EventSourcing.Core
open Tonyx.EventSourcing.Cache

open Tonyx.EventSourcing.Sample_02.Categories.Models.CategoriesModel
open Tonyx.EventSourcing.Sample_02.Categories.CategoriesEvents

module CategoriesCommands =
    open Tonyx.EventSourcing.Sample_02.CategoriesAggregate
    type CategoryCommand =
        | AddCategory of Category
        | RemoveCategory of Guid

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