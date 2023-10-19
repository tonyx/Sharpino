namespace Sharpino.Sample.Todos

open System
open Sharpino.Sample.Entities.Todos
open Sharpino.Sample.Entities.Categories
open Sharpino.Sample.TodosAggregate
open Sharpino.Core
open Sharpino.Storage
open Sharpino.Cache
open Sharpino.Utils
open type Sharpino.Cache.EventCache<TodosAggregate>

module TodoEvents =
    type TodoEvent =
        | TodoAdded of Todo
        | TodoRemoved of Guid
        | CategoryAdded of Category
        | CategoryRemoved of Guid
        | TagRefRemoved of Guid
            interface Event<TodosAggregate> with
                member this.Process (x: TodosAggregate) =
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
            interface Event<TodosAggregate'> with
                member this.Process (x: TodosAggregate') =
                    match this with
                    | TodoAdded (t: Todo) ->
                        EventCache<TodosAggregate'>.Instance.Memoize (fun () -> x.AddTodo t) (x, [this])
                    | TodoRemoved (g: Guid) ->
                        EventCache<TodosAggregate'>.Instance.Memoize (fun () -> x.RemoveTodo g) (x, [this])
                    | TagRefRemoved (g: Guid) ->            
                        EventCache<TodosAggregate'>.Instance.Memoize (fun () -> x.RemoveTagReference g) (x, [this])
                    | CategoryRefRemoved (g: Guid) ->
                        EventCache<TodosAggregate'>.Instance.Memoize (fun () -> x.RemoveCategoryReference g) (x, [this])
                    | TodosAdded (ts: List<Todo>) ->
                        EventCache<TodosAggregate'>.Instance.Memoize (fun () -> x.AddTodos ts) (x, [this])
        member this.Serialize(serializer: ISerializer) =
            this
            |> serializer.Serialize

        static member Deserialize (serializer: ISerializer, json: Json) =
            serializer.Deserialize<TodoEvent'> json