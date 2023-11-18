
namespace Sharpino.Sample.Categories

open System
open Sharpino.Core
open Sharpino.Utils
open Sharpino.Definitions

open Sharpino.Sample.Entities.Categories
open Sharpino.Sample.CategoriesCluster
open Sharpino.Sample.Shared.Entities

module CategoriesEvents =
    type CategoryEvent =
        | CategoryAdded of Category
        | CategoryRemoved of Guid
        | CategoriesAdded of List<Category>
            interface Event<CategoriesCluster> with
                member this.Process (x: CategoriesCluster) =
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