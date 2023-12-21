
namespace Sharpino.Sample.Categories

open System
open Sharpino.Core
open Sharpino.Utils
open Sharpino.Definitions

open Sharpino.Sample.Entities.Categories
open Sharpino.Sample.CategoriesContext
open Sharpino.Sample.Shared.Entities

module CategoriesEvents =
    type CategoryEvent =
        | CategoryAdded of Category
        | CategoryRemoved of Guid
        | CategoriesAdded of List<Category>
        | PingDone of unit
            interface Event<CategoriesContext> with
                member this.Process (x: CategoriesContext) =
                    match this with
                    | CategoryAdded (c: Category) ->
                        x.AddCategory c
                    | CategoryRemoved (g: Guid) ->
                        x.RemoveCategory g
                    | CategoriesAdded (cs: List<Category>) ->
                        x.AddCategories cs
                    | PingDone () ->
                        x.Ping()
        member this.Serialize(serializer: ISerializer) =
            this
            |> serializer.Serialize

        static member Deserialize (serializer: ISerializer, json: Json) =
            serializer.Deserialize<CategoryEvent> json