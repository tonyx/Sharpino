namespace Sharpino.Sample.Todos

open System
open Sharpino.Core
open Sharpino.Utils

open Sharpino.Sample.Todos.TodoEvents
open Sharpino.Sample.Entities.Todos
open Sharpino.Sample.Entities.Categories
open Sharpino.Sample.TodosContext
open Sharpino.Sample.Shared.Entities

module TodoCommands =
    type TodoCommand =
        | AddTodo of Todo
        | RemoveTodo of Guid
        | AddCategory of Category
        | RemoveCategory of Guid
        | RemoveTagRef of Guid
        | Add2Todos of Todo * Todo
        | Ping of unit

        interface Command<TodosContext, TodoEvent> with
            member this.Execute (x: TodosContext) =
                match this with
                | AddTodo t -> 
                    x.AddTodo t
                    |> Result.map (fun s -> (s, [TodoEvent.TodoAdded t]))
                | RemoveTodo g ->
                    x.RemoveTodo g
                    |> Result.map (fun s -> (s, [TodoEvent.TodoRemoved g]))
                | AddCategory c ->
                    x.AddCategory c
                    |> Result.map (fun s -> (s, [TodoEvent.CategoryAdded c]))
                | RemoveCategory g ->
                    x.RemoveCategory g
                    |> Result.map (fun s -> (s, [TodoEvent.CategoryRemoved g]))
                | RemoveTagRef g ->
                    x.RemoveTagReference g
                    |> Result.map (fun s -> (s, [TodoEvent.TagRefRemoved g]))
                | Add2Todos (t1, t2) -> 
                    [TodoEvent.TodoAdded t1; TodoEvent.TodoAdded t2]
                    |> evolveUNforgivingErrors x
                    |> Result.map (fun s -> (s, [TodoEvent.TodoAdded t1; TodoEvent.TodoAdded t2]))
                | Ping () ->
                    x.Ping()
                    |> Result.map (fun s -> (s, [TodoEvent.PingDone ()]))
            member this.Undoer = None

    [<UpgradedVersion>]
    type TodoCommand' =
        | AddTodo of Todo
        | RemoveTodo of Guid
        | RemoveTagRef of Guid
        | RemoveCategoryRef of Guid
        | Add2Todos of Todo * Todo
        | AddTodos of List<Todo>
        | Ping of unit

        interface Command<TodosContextUpgraded, TodoEvent'> with
            member this.Execute (x: TodosContextUpgraded) =
                match this with
                | AddTodo t ->
                    x.AddTodo t
                    |> Result.map (fun s -> (s, [TodoEvent'.TodoAdded t])) 
                | RemoveTodo g ->
                    x.RemoveTodo g 
                    |> Result.map (fun s -> (s, [TodoEvent'.TodoRemoved g]))
                | RemoveTagRef g ->
                    x.RemoveTagReference g 
                    |> Result.map (fun s -> (s, [TodoEvent'.TagRefRemoved g]))
                | Add2Todos (t1, t2) -> 
                    let evolved =
                        fun () ->
                        [TodoAdded t1; TodoAdded t2]
                        |> evolveUNforgivingErrors x
                    evolved()
                    |> Result.map (fun s -> (s, [TodoEvent'.TodoAdded t1; TodoEvent'.TodoAdded t2]))
                | RemoveCategoryRef g ->
                    x.RemoveCategoryReference g 
                    |> Result.map (fun s -> (s, [TodoEvent'.CategoryRefRemoved g]))
                | AddTodos ts ->
                    x.AddTodos ts 
                    |> Result.map (fun s -> (s, [TodoEvent'.TodosAdded ts]))
                | Ping () ->
                    x.Ping()
                    |> Result.map (fun s -> (s, [TodoEvent'.PingDone ()]))
            member this.Undoer = None

