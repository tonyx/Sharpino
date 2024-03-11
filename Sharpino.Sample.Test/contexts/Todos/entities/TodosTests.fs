
module Tests.Sharpino.Sample.Todos.Models.TodoTests
open Tests.Sharpino.Shared

open Expecto
open System
open FSharp.Core

open Sharpino.Sample.Entities.Todos
open Sharpino.Utils

[<Tests>]
let todosModelTests =
    testList "todos model tests" [
        testCase "add todo - Ok" <| fun _ ->
            let todo = mkTodo (Guid.NewGuid()) "test" [] []
            let todos = Todos.Zero.AddTodo todo
            Expect.isOk todos "should be ok"
            let result = todos.OkValue
            Expect.equal (result.Todos.GetAll() |> List.length) 1 "should be equal"

        testCase "add and remove a todo - Ok" <| fun _ ->
            let id = Guid.NewGuid()
            let todo = mkTodo id "test" [] []
            let todos = Todos.Zero.AddTodo todo
            Expect.isOk todos "should be ok"
            let todos' = todos.OkValue
            Expect.equal (todos'.Todos.GetAll() |> List.length) 1 "should be equal"
            let todos'' = todos'.RemoveTodo id
            Expect.isOk todos'' "should be ok"
            let result = todos''.OkValue
            Expect.equal (result.Todos.GetAll() |> List.length) 0 "should be equal"

        testCase "try removing an unexisting todo - Ko" <| fun _ ->
            let id = Guid.NewGuid()
            let todo = mkTodo id "test" [] []
            let todos = Todos.Zero.AddTodo todo
            Expect.isOk todos "should be ok"
            let todos' = todos.OkValue
            Expect.equal (todos'.GetTodos() |> List.length) 1 "should be equal"

            let unexistingId = Guid.NewGuid()
            let result = todos'.RemoveTodo unexistingId

            Expect.isError result "should be error"
            let errMsg = result |> getError
            printf "%s" errMsg
            Expect.equal errMsg (sprintf "A todo with id '%A' does not exist" unexistingId) "should be equal"
    ]