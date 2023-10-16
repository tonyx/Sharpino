namespace Sharpino.Sample.Todos

open System
open Sharpino.Core
open Sharpino.Utils

open Sharpino.Sample.Todos.TodoEvents
open Sharpino.Sample.Entities.Todos
open Sharpino.Sample.Entities.Categories
open Sharpino.Sample.TodosAggregate
open type Sharpino.Cache.EventCache<TodosAggregate>
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
                    Instance.Memoize (fun () -> x.AddTodo t) (x, [TodoEvent.TodoAdded t])
                    |> Result.map (fun _ -> [TodoEvent.TodoAdded t])
                | RemoveTodo g ->
                    Instance.Memoize (fun () -> x.RemoveTodo g) (x, [TodoEvent.TodoRemoved g])
                    |> Result.map (fun _ -> [TodoEvent.TodoRemoved g])
                | AddCategory c ->
                    Instance.Memoize (fun () -> x.AddCategory c) (x, [TodoEvent.CategoryAdded c])
                    |> Result.map (fun _ -> [TodoEvent.CategoryAdded c])
                | RemoveCategory g ->
                    Instance.Memoize (fun () -> x.RemoveCategory g) (x, [TodoEvent.CategoryRemoved g]) 
                    |> Result.map (fun _ -> [TodoEvent.CategoryRemoved g])
                | RemoveTagRef g ->
                    EventCache<TodosAggregate>.Instance.Memoize (fun () -> x.RemoveTagReference g) (x, [TodoEvent.TagRefRemoved g])
                    |> Result.map (fun _ -> [TodoEvent.TagRefRemoved g])
                | Add2Todos (t1, t2) -> 
                    let evolved =
                        fun () ->
                        [TodoEvent.TodoAdded t1; TodoEvent.TodoAdded t2]
                        |> evolveUNforgivingErrors x
                    EventCache<TodosAggregate>.Instance.Memoize (fun () -> evolved()) (x, [TodoEvent.TodoAdded t1; TodoEvent.TodoAdded t2])
                    |> Result.map (fun _ -> [TodoEvent.TodoAdded t1; TodoEvent.TodoAdded t2])
            member this.Undoer = None

    open type Sharpino.Cache.EventCache<TodosAggregate'>
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
                        Instance.Memoize (fun () -> x.AddTodo t) (x, [TodoEvent'.TodoAdded t]) with
                        | Ok _ -> [TodoAdded t] |> Ok
                        | Error x -> x |> Error
                | RemoveTodo g ->
                    match
                        Instance.Memoize (fun () -> x.RemoveTodo g) (x, [TodoEvent'.TodoRemoved g]) with
                        | Ok _ -> [TodoRemoved g] |> Ok
                        | Error x -> x |> Error
                | RemoveTagRef g ->
                    match
                        Instance.Memoize (fun () -> x.RemoveTagReference g) (x, [TodoEvent'.TagRefRemoved g]) with
                        | Ok _ -> [TagRefRemoved g] |> Ok
                        | Error x -> x |> Error
                | Add2Todos (t1, t2) -> 
                    let evolved =
                        fun () ->
                        [TodoAdded t1; TodoAdded t2]
                        |> evolveUNforgivingErrors x
                    match
                        Instance.Memoize (fun () -> evolved()) (x, [TodoEvent'.TodoAdded t1; TodoEvent'.TodoAdded t2]) with
                        | Ok _ -> [TodoAdded t1; TodoAdded t2] |> Ok
                        | Error x -> x |> Error
                | RemoveCategoryRef g ->
                    match
                        Instance.Memoize (fun () -> x.RemoveCategoryReference g) (x, [TodoEvent'.CategoryRefRemoved g]) with
                        | Ok _ -> [CategoryRefRemoved g] |> Ok
                        | Error x -> x |> Error
                | AddTodos ts ->
                    match
                        Instance.Memoize (fun () -> x.AddTodos ts) (x, [TodoEvent'.TodosAdded ts]) with
                        | Ok _ -> [TodoEvent'.TodosAdded ts] |> Ok
                        | Error x -> x |> Error
            member this.Undoer = None

