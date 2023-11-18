namespace Sharpino.Sample
open Sharpino.Utils
open Sharpino.Sample.TodosCluster
open Sharpino.Sample.Entities.Categories
open Sharpino.Sample.Entities.Todos
open Sharpino.Sample.TagsCluster
open Sharpino.Sample.Entities.Tags
open Sharpino.Sample.CategoriesCluster
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

    // aggregates
    type TodosCluster with
        member this.Serialize (serializer: ISerializer) =
            this |> serializer.Serialize
    type TodosAggregate' with
        member this.Serialize (serializer: ISerializer) =
            this |> serializer.Serialize
    type CategoriesCluster with
        member this.Serialize (serializer: ISerializer) =
            this |> serializer.Serialize
    type TagsCluster with
        member this.Serialize (serializer: ISerializer) =
            this |> serializer.Serialize
