namespace Tonyx.EventSourcing.Sample.Todos

open System
open Tonyx.EventSourcing.Sample.Todos.Models.TodosModel
open Tonyx.EventSourcing.Sample.Todos.Models.CategoriesModel
open Tonyx.EventSourcing.Sample.TodosAggregate
open Tonyx.EventSourcing.Core
open Tonyx.EventSourcing.Cache
open Microsoft.FSharp.Quotations

module TodoEvents =
    type TodoEvent =
        | TodoAdded of Todo
        | TodoRemoved of Guid
        | CategoryAdded of Category
        | CategoryRemoved of Guid
        | TagRefRemoved of Guid
        | ExperimentalTodoAdded of Expr<bool> * Todo
            interface Event<TodosAggregate> with
                member this.Process (x: TodosAggregate ) =
                    match this with
                    | TodoAdded (t: Todo) ->
                        EventCache<TodosAggregate>.Instance.Memoize (fun () -> x.AddTodo t) (x, [TodoAdded t])
                    | TodoRemoved (g: Guid) ->
                        EventCache<TodosAggregate>.Instance.Memoize (fun () -> x.RemoveTodo g) (x, [TodoRemoved g])
                    | CategoryAdded (c: Category) ->
                        EventCache<TodosAggregate>.Instance.Memoize (fun () -> x.AddCategory c) (x, [CategoryAdded c])
                    | CategoryRemoved (g: Guid) ->  
                        EventCache<TodosAggregate>.Instance.Memoize (fun () -> x.RemoveCategory g) (x, [CategoryRemoved g])
                    | TagRefRemoved (g: Guid) ->            
                        EventCache<TodosAggregate>.Instance.Memoize (fun () -> x.RemoveTagReference g) (x, [TagRefRemoved g])
                    | ExperimentalTodoAdded (c: Expr<bool>, t: Todo) ->
                        EventCache<TodosAggregate>.Instance.Memoize (fun () -> x.ExperimentalAddTodo c t) (x, [ExperimentalTodoAdded (c, t)])

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
                        EventCache<TodosAggregate'>.Instance.Memoize (fun () -> x.AddTodo t) (x, [TodoAdded t])
                    | TodoRemoved (g: Guid) ->
                        EventCache<TodosAggregate'>.Instance.Memoize (fun () -> x.RemoveTodo g) (x, [TodoRemoved g])
                    | TagRefRemoved (g: Guid) ->            
                        EventCache<TodosAggregate'>.Instance.Memoize (fun () -> x.RemoveTagReference g) (x, [TagRefRemoved g])
                    | CategoryRefRemoved (g: Guid) ->
                        EventCache<TodosAggregate'>.Instance.Memoize (fun () -> x.RemoveCategoryReference g) (x, [CategoryRefRemoved g])
                    | TodosAdded (ts: List<Todo>) ->
                        EventCache<TodosAggregate'>.Instance.Memoize (fun () -> x.AddTodos ts) (x, [TodosAdded ts])