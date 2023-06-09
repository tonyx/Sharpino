namespace Tonyx.EventSourcing.Sample.Todos

open System
open Tonyx.EventSourcing.Sample.Todos.Models.TodosModel
open Tonyx.EventSourcing.Sample.Todos.Models.CategoriesModel
open Tonyx.EventSourcing.Sample.TodosAggregate
open Tonyx.EventSourcing.Core
open Tonyx.EventSourcing.Cache
open Tonyx.EventSourcing.Utils

module TodoEvents =
    type TodoEvent =
        | TodoAdded of Todo
        | TodoRemoved of Guid
        | CategoryAdded of Category
        | CategoryRemoved of Guid
        | TagRefRemoved of Guid
            interface Event<TodosAggregate> with
                member this.Process (x: TodosAggregate ) =
                    match this with
                    | TodoAdded (t: Todo) ->
                        EventCache<TodosAggregate>.Instance.Memoize (fun () -> x.AddTodo t) (x, [this])
                    | TodoRemoved (g: Guid) ->
                        EventCache<TodosAggregate>.Instance.Memoize (fun () -> x.RemoveTodo g) (x, [this])
                    | CategoryAdded (c: Category) ->
                        EventCache<TodosAggregate>.Instance.Memoize (fun () -> x.AddCategory c) (x, [this])
                    | CategoryRemoved (g: Guid) ->  
                        EventCache<TodosAggregate>.Instance.Memoize (fun () -> x.RemoveCategory g) (x, [this])
                    | TagRefRemoved (g: Guid) ->            
                        EventCache<TodosAggregate>.Instance.Memoize (fun () -> x.RemoveTagReference g) (x, [this])


    [<UpgradeToVersion>]
    type TodoEvent' =
        | TodoAdded of Todo
        | TodoRemoved of Guid
        | TagRefRemoved of Guid
        | CategoryRefRemoved of Guid
        | TodosAdded of List<Todo>
            interface Event<TodosAggregate'> with
                member this.Process (x: TodosAggregate' ) =
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