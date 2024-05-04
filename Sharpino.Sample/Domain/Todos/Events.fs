namespace Sharpino.Sample.Todos

open System
open Sharpino.Sample.Entities.Todos
open Sharpino.Sample.Entities.Categories
open Sharpino.Sample.TodosContext
open Sharpino.Sample.Shared.Entities
open Sharpino.Sample.Commons
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
        | PingDone of unit
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
                    | PingDone () ->
                        x.Ping()
        member this.Serialize =
            this
            |> serializer.Serialize
        static member Deserialize json =
            serializer.Deserialize<TodoEvent> json


    [<UpgradedVersion>]
    type TodoEvent' =
        | TodoAdded of Todo
        | TodoRemoved of Guid
        | TagRefRemoved of Guid
        | CategoryRefRemoved of Guid
        | TodosAdded of List<Todo>
        | PingDone of unit
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
                    | PingDone () ->
                        x.Ping()
        member this.Serialize =
            this
            |> serializer.Serialize

        static member Deserialize  json =
            serializer.Deserialize<TodoEvent'> json