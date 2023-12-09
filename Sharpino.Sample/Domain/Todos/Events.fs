namespace Sharpino.Sample.Todos

open System
open Sharpino.Sample.Entities.Todos
open Sharpino.Sample.Entities.Categories
open Sharpino.Sample.TodosCluster
open Sharpino.Sample.Shared.Entities
open Sharpino.Core
open Sharpino.Definitions
open Sharpino.Utils

module TodoEvents =
    type TodoEvent =
        | TodoAdded of Todo
        | TodoRemoved of Guid
        | CategoryAdded of Category
        | CategoryRemoved of Guid
        | TagRefRemoved of Guid
            interface Event<TodosContext> with
                member this.Process (x: TodosContext) =
                    match this with
                    | TodoAdded (t: Todo) ->
                        t |> x.AddTodo
                    | TodoRemoved (g: Guid) ->
                        g |> x.RemoveTodo
                    | CategoryAdded (c: Category) ->
                        c |> x.AddCategory
                    | CategoryRemoved (g: Guid) ->  
                        g |> x.RemoveCategory
                    | TagRefRemoved (g: Guid) ->            
                        g |> x.RemoveTagReference
        member this.Serialize(serializer: ISerializer) =
            this
            |> serializer.Serialize
        static member Deserialize (serializer: ISerializer, json: Json) =
            serializer.Deserialize<TodoEvent> json


    [<UpgradedVersion>]
    type TodoEvent' =
        | TodoAdded of Todo
        | TodoRemoved of Guid
        | TagRefRemoved of Guid
        | CategoryRefRemoved of Guid
        | TodosAdded of List<Todo>
            interface Event<TodosContextUpgraded> with
                member this.Process (x: TodosContextUpgraded) =
                    match this with
                    | TodoAdded (t: Todo) ->
                        x.AddTodo t
                    | TodoRemoved (g: Guid) ->
                        x.RemoveTodo g
                    | TagRefRemoved (g: Guid) ->            
                        x.RemoveTagReference g
                    | CategoryRefRemoved (g: Guid) ->
                        x.RemoveCategoryReference g
                    | TodosAdded (ts: List<Todo>) ->
                        x.AddTodos ts
        member this.Serialize(serializer: ISerializer) =
            this
            |> serializer.Serialize

        static member Deserialize (serializer: ISerializer, json: Json) =
            serializer.Deserialize<TodoEvent'> json