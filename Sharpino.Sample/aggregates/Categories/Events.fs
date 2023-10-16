
namespace Sharpino.Sample.Categories

open System
open Sharpino.Core
open Sharpino.Cache
open Sharpino.Utils
open Sharpino.Storage

open Sharpino.Sample.Entities.Categories
open Sharpino.Sample.CategoriesAggregate
open type Sharpino.Cache.EventCache<CategoriesAggregate>

module CategoriesEvents =
    type CategoryEvent =
        | CategoryAdded of Category
        | CategoryRemoved of Guid
        | CategoriesAdded of List<Category>
            interface Event<CategoriesAggregate> with
                member this.Process (x: CategoriesAggregate) =
                    match this with
                    | CategoryAdded (c: Category) ->
                        Instance.Memoize (fun () -> x.AddCategory c) (x, [this])
                    | CategoryRemoved (g: Guid) ->
                        Instance.Memoize (fun () -> x.RemoveCategory g) (x, [this])
                    | CategoriesAdded (cs: List<Category>) ->
                        Instance.Memoize (fun () -> x.AddCategories cs) (x, [this])
        member this.Serialize(serializer: ISerializer) =
            this
            |> serializer.Serialize

        static member Deserialize (serializer: ISerializer, json: Json) =
            serializer.Deserialize<CategoryEvent> json