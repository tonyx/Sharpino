namespace Tonyx.EventSourcing.Sample.Todos

open System
open Tonyx.EventSourcing.Core

open Tonyx.EventSourcing.Sample.Todos.TodoEvents
open Tonyx.EventSourcing.Sample.Todos.Models.TodosModel
open Tonyx.EventSourcing.Sample.Todos.Models.CategoriesModel
open Tonyx.EventSourcing.Sample.TodosAggregate
open Tonyx.EventSourcing.Cache

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
                    match

                        EventCache<TodosAggregate>.Instance.Memoize (fun () -> x.AddTodo t) (x, [TodoAdded t]) with
                        | Ok _ -> [TodoAdded t] |> Ok
                        | Error x -> x |> Error
                | RemoveTodo g ->
                    match
                        EventCache<TodosAggregate>.Instance.Memoize (fun () -> x.RemoveTodo g) (x, [TodoRemoved g]) with
                        | Ok _ -> [TodoRemoved g] |> Ok
                        | Error x -> x |> Error
                | AddCategory c ->
                    match
                        EventCache<TodosAggregate>.Instance.Memoize (fun () -> x.AddCategory c) (x, [CategoryAdded c]) with
                        | Ok _ -> [CategoryAdded c] |> Ok
                        | Error x -> x |> Error
                | RemoveCategory g ->
                    match
                        EventCache<TodosAggregate>.Instance.Memoize (fun () -> x.RemoveCategory g) (x, [CategoryRemoved g]) with
                        | Ok _ -> [CategoryRemoved g] |> Ok
                        | Error x -> x |> Error
                | RemoveTagRef g ->
                    match
                        EventCache<TodosAggregate>.Instance.Memoize (fun () -> x.RemoveTagReference g) (x, [TagRefRemoved g]) with
                        | Ok _ -> [TagRefRemoved g] |> Ok
                        | Error x -> x |> Error
                | Add2Todos (t1, t2) -> 
                    let evolved =
                        fun () ->
                        [TodoAdded t1; TodoAdded t2]
                        |> evolve x
                    match
                        EventCache<TodosAggregate>.Instance.Memoize (fun () -> evolved()) (x, [TodoAdded t1; TodoAdded t2]) with
                        | Ok _ -> [TodoAdded t1; TodoAdded t2] |> Ok
                        | Error x -> x |> Error


