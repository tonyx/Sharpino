
namespace Tonyx.EventSourcing.Sample_02.Categories

open System
open Tonyx.EventSourcing.Core
open Tonyx.EventSourcing.Cache

open Tonyx.EventSourcing.Sample.Todos.Models.CategoriesModel
open Tonyx.EventSourcing.Sample_02.CategoriesAggregate

module CategoriesEvents =
    type CategoryEvent =
        | CategoryAdded of Category
        | CategoryRemoved of Guid
            interface Event<CategoriesAggregate> with
                member this.Process (x: CategoriesAggregate) =
                    match this with
                    | CategoryAdded (c: Category) ->
                        EventCache<CategoriesAggregate>.Instance.Memoize (fun () -> x.AddCategory c) (x, [CategoryAdded c])
                    | CategoryRemoved (g: Guid) ->
                        EventCache<CategoriesAggregate>.Instance.Memoize (fun () -> x.RemoveCategory g) (x, [CategoryRemoved g])
