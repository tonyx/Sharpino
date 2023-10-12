namespace Sharpino.Sample
open Sharpino.Utils
open Sharpino.Definitions

open Sharpino
open Sharpino.Sample
open Sharpino.Sample.TodosAggregate
open Sharpino.Sample.Todos
open Sharpino.Sample.Entities.Categories
open Sharpino.Sample.Entities.Todos
open Sharpino.Sample.TagsAggregate
open Sharpino.Sample.Entities.Tags
open Sharpino.Sample.Categories
open Sharpino.Sample.Tags
open Sharpino.Utils
open Sharpino.Definitions
open Sharpino.Sample.CategoriesAggregate
open Sharpino.Sample.Categories.CategoriesEvents
open Sharpino.Sample.Todos.TodoEvents
open Sharpino.Sample.Tags.TagsEvents
open System.Runtime.CompilerServices
open FsToolkit.ErrorHandling
open FSharpPlus
open System.Runtime.CompilerServices


// try making it work
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
    type TodosAggregate with
        member this.Serialize (serializer: ISerializer) =
            this |> serializer.Serialize

    [<Extension>]
    type TodosAggregateExtension =
        [<Extension>]
        static member Serialize (this: TodosAggregate, serializer: ISerializer) =
            this |> serializer.Serialize


    type TodosAggregate' with
        member this.Serialize (serializer: ISerializer) =
            this |> serializer.Serialize
    type CategoriesAggregate with
        member this.Serialize (serializer: ISerializer) =
            this |> serializer.Serialize
    type TagsAggregate with
        member this.Serialize (serializer: ISerializer) =
            this |> serializer.Serialize
