
module Tests.Sharpino.Sample02.Todos.Models.TodoTests

open Expecto
open System
open FSharp.Core

open Tests.Sharpino.Shared
open Sharpino.Sample.Entities.Todos
open Sharpino.Utils
open Sharpino.Core
open Sharpino.EncriptUtils

[<Tests>]
let todosModelTests =
    let secretKeyIndex = "4b938de9-cb4b-4297-8687-865181836548"
    testList "todos model 02 tests" [
        testCase "add todo - Ok" <| fun _ ->
            let todo = { Id = Guid.NewGuid(); Description = "test" |> mkForgettable secretKeyIndex; CategoryIds = []; TagIds = []}
            let todos = Todos.Zero.AddTodo todo
            Expect.isOk todos "should be ok"
            let result = todos.OkValue
            Expect.equal (result.todos |> List.length) 1 "should be equal"

        testCase "add and remove a todo - Ok" <| fun _ ->
            let id = Guid.NewGuid()
            let todo = { Id = id; Description = "test" |> mkForgettable secretKeyIndex; CategoryIds = []; TagIds = []}
            let todos = Todos.Zero.AddTodo todo
            Expect.isOk todos "should be ok"
            let todos' = todos.OkValue
            Expect.equal (todos'.todos |> List.length) 1 "should be equal"
            let todos'' = todos'.RemoveTodo id
            Expect.isOk todos'' "should be ok"
            let result = todos''.OkValue
            Expect.equal (result.todos |> List.length) 0 "should be equal"

        testCase "try removing an unexisting todo - Ko" <| fun _ ->
            let id = Guid.NewGuid()
            let todo = { Id = id; Description = "test" |> mkForgettable secretKeyIndex ; CategoryIds = []; TagIds = []}
            let todos = Todos.Zero.AddTodo todo
            Expect.isOk todos "should be ok"
            let todos' = todos.OkValue
            Expect.equal (todos'.GetTodos() |> List.length) 1 "should be equal"
            let unexistingId = Guid.NewGuid()

            let result = todos'.RemoveTodo unexistingId
            Expect.isError result "should be error"
            let errMsg = result |> getError
            Expect.equal errMsg (sprintf "A Todo with id '%A' does not exist" unexistingId) "should be equal"
    ]