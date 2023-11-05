
namespace Sharpino.Sample.Categories

open System
open Sharpino.Core
open Sharpino.Cache
open Sharpino.Utils
open Sharpino.Storage
open Sharpino.Definitions

open Sharpino.Sample.Entities.Categories
open Sharpino.Sample.CategoriesAggregate

module CategoriesEvents =
    type CategoryEvent =
        | CategoryAdded of Category
        | CategoryRemoved of Guid
        | CategoriesAdded of List<Category>
            interface Event<CategoriesAggregate> with
                member this.Process (x: CategoriesAggregate) =
                    match this with
                    | CategoryAdded (c: Category) ->
                        x.AddCategory c
                    | CategoryRemoved (g: Guid) ->
                        x.RemoveCategory g
                    | CategoriesAdded (cs: List<Category>) ->
                        x.AddCategories cs
        member this.Serialize(serializer: ISerializer) =
            this
            |> serializer.Serialize

        static member Deserialize (serializer: ISerializer, json: Json) =
            serializer.Deserialize<CategoryEvent> json