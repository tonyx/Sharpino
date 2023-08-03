namespace Sharpino.Sample.Todos

open System
open Sharpino.Core
open Sharpino.Utils

open Sharpino.Sample.Todos.TodoEvents
open Sharpino.Sample.Models.TodosModel
open Sharpino.Sample.Models.CategoriesModel
open Sharpino.Sample.TodosAggregate
open Sharpino.Cache

module TodoCommands =
    type TodoCommand =
        | AddTodo of Todo
        | RemoveTodo of Guid
        | AddCategory of Category
        | RemoveCategory of Guid
        | RemoveTagRef of Guid
        | Add2Todos of Todo * Todo

        interface Command<TodosAggregate, TodoEvent> with
            member this.Execute (x: TodosAggregate) =
                match this with
                | AddTodo t -> 
                    match EventCache<TodosAggregate>.Instance.Memoize (fun () -> x.AddTodo t) (x, [TodoEvent.TodoAdded t]) with
                    | Ok _ -> [TodoEvent.TodoAdded t] |> Ok
                    | Error x -> x |> Error
                | RemoveTodo g ->
                    match
                        EventCache<TodosAggregate>.Instance.Memoize (fun () -> x.RemoveTodo g) (x, [TodoEvent.TodoRemoved g]) with
                        | Ok _ -> [TodoEvent.TodoRemoved g] |> Ok
                        | Error x -> x |> Error
                | AddCategory c ->
                    match
                        EventCache<TodosAggregate>.Instance.Memoize (fun () -> x.AddCategory c) (x, [TodoEvent.CategoryAdded c]) with
                        | Ok _ -> [TodoEvent.CategoryAdded c] |> Ok
                        | Error x -> x |> Error
                | RemoveCategory g ->
                    match
                        EventCache<TodosAggregate>.Instance.Memoize (fun () -> x.RemoveCategory g) (x, [TodoEvent.CategoryRemoved g]) with
                        | Ok _ -> [CategoryRemoved g] |> Ok
                        | Error x -> x |> Error
                | RemoveTagRef g ->
                    match
                        EventCache<TodosAggregate>.Instance.Memoize (fun () -> x.RemoveTagReference g) (x, [TodoEvent.TagRefRemoved g]) with
                        | Ok _ -> [TodoEvent.TagRefRemoved g] |> Ok
                        | Error x -> x |> Error
                | Add2Todos (t1, t2) -> 
                    let evolved =
                        fun () ->
                        [TodoEvent.TodoAdded t1; TodoEvent.TodoAdded t2]
                        |> evolve x
                    match EventCache<TodosAggregate>.Instance.Memoize (fun () -> evolved()) (x, [TodoEvent.TodoAdded t1; TodoEvent.TodoAdded t2]) with
                        | Ok _ -> [TodoEvent.TodoAdded t1; TodoEvent.TodoAdded t2] |> Ok
                        | Error x -> x |> Error
            member this.Undo = None

    [<UpgradedVersion>]
    type TodoCommand' =
        | AddTodo of Todo
        | RemoveTodo of Guid
        | RemoveTagRef of Guid
        | RemoveCategoryRef of Guid
        | Add2Todos of Todo * Todo
        | AddTodos of List<Todo>

        interface Command<TodosAggregate', TodoEvent'> with
            member this.Execute (x: TodosAggregate') =
                match this with
                | AddTodo t ->
                    match
                        EventCache<TodosAggregate'>.Instance.Memoize (fun () -> x.AddTodo t) (x, [TodoEvent'.TodoAdded t]) with
                        | Ok _ -> [TodoAdded t] |> Ok
                        | Error x -> x |> Error
                | RemoveTodo g ->
                    match
                        EventCache<TodosAggregate'>.Instance.Memoize (fun () -> x.RemoveTodo g) (x, [TodoEvent'.TodoRemoved g]) with
                        | Ok _ -> [TodoRemoved g] |> Ok
                        | Error x -> x |> Error
                | RemoveTagRef g ->
                    match
                        EventCache<TodosAggregate'>.Instance.Memoize (fun () -> x.RemoveTagReference g) (x, [TodoEvent'.TagRefRemoved g]) with
                        | Ok _ -> [TagRefRemoved g] |> Ok
                        | Error x -> x |> Error
                | Add2Todos (t1, t2) -> 
                    let evolved =
                        fun () ->
                        [TodoAdded t1; TodoAdded t2]
                        |> evolve x
                    match
                        EventCache<TodosAggregate'>.Instance.Memoize (fun () -> evolved()) (x, [TodoEvent'.TodoAdded t1; TodoEvent'.TodoAdded t2]) with
                        | Ok _ -> [TodoAdded t1; TodoAdded t2] |> Ok
                        | Error x -> x |> Error
                | RemoveCategoryRef g ->
                    match
                        EventCache<TodosAggregate'>.Instance.Memoize (fun () -> x.RemoveCategoryReference g) (x, [TodoEvent'.CategoryRefRemoved g]) with
                        | Ok _ -> [CategoryRefRemoved g] |> Ok
                        | Error x -> x |> Error
                | AddTodos ts ->
                    match
                        EventCache<TodosAggregate'>.Instance.Memoize (fun () -> x.AddTodos ts) (x, [TodoEvent'.TodosAdded ts]) with
                        | Ok _ -> [TodoEvent'.TodosAdded ts] |> Ok
                        | Error x -> x |> Error
            member this.Undo = None

