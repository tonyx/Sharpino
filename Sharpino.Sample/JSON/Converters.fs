namespace Sharpino.Sample
open Sharpino.Utils
open Sharpino.Sample.TodosContext
open Sharpino.Sample.Entities.Categories
open Sharpino.Sample.Entities.Todos
open Sharpino.Sample.TagsContext
open Sharpino.Sample.Entities.Tags
open Sharpino.Sample.CategoriesContext
open Sharpino.Sample.Categories.CategoriesEvents
open Sharpino.Sample.Todos.TodoEvents
open Sharpino.Sample.Tags.TagsEvents
open Sharpino.Sample.Shared.Entities

// this is to remind you that you wanto to extend entities behavior
// for json in a separate file like this but it does not work yet.
module Converters =
    // entities
    type Todo with
        member this.Serialize (serializer: ISerializer) =
            this |> serializer.Serialize 
    type Category with
        member this.Serialize (serializer: ISerializer) =
            this |> serializer.Serialize 
    type Tag with
        member this.Serialize (serializer: ISerializer) =
            this |> serializer.Serialize
    // events
    type TodoEvent with
        member this.Serialize (serializer: ISerializer) =
            this |> serializer.Serialize
    type TodoEvent' with
        member this.Serialize (serializer: ISerializer) =
            this |> serializer.Serialize
    type CategoryEvent with
        member this.Serialize (serializer: ISerializer) =
            this |> serializer.Serialize
    type TagEvent with
        member this.Serialize (serializer: ISerializer) =
            this |> serializer.Serialize

    // contexts
    type TodosContext with
        member this.Serialize (serializer: ISerializer) =
            this |> serializer.Serialize
    type TodosContextUpgraded with
        member this.Serialize (serializer: ISerializer) =
            this |> serializer.Serialize
    type CategoriesContext with
        member this.Serialize (serializer: ISerializer) =
            this |> serializer.Serialize
    type TagsContext with
        member this.Serialize (serializer: ISerializer) =
            this |> serializer.Serialize
