module Tests.Sharpino.Sample02.Sample.TodoTests

open Expecto
open System
open FSharp.Core

open Tests.Sharpino.Shared

open Sharpino.Sample.TodosAggregate
open Sharpino.Sample.Entities.Todos
open FsToolkit.ErrorHandling

[<Tests>]
let todosAggregateUpgrade02Tests =
    testList "todos aggregate upgrade tests" [
        testCase "add todo - Ok" <| fun _ ->

            let todo = mkTodo (Guid.NewGuid()) "test" [] []
            let aggregate = TodosAggregate'.Zero.AddTodo todo
            Expect.isOk aggregate "should be ok"
            let result = aggregate.OkValue
            Expect.equal (result.GetTodos() |> List.length) 1 "should be equal"
            Expect.isTrue true "should be true"

        testCase "add and remove a todo - Ok" <| fun _ ->
            let id = Guid.NewGuid()
            let todo = mkTodo id "test" [] []
            let aggregate = TodosAggregate'.Zero.AddTodo todo |> Result.get
            Expect.equal (aggregate.GetTodos() |> List.length) 1 "should be equal"
            let aggregate' = aggregate.RemoveTodo id 
            Expect.isOk aggregate' "should be ok"
            let result = aggregate'.OkValue
            Expect.equal (result.GetTodos() |> List.length) 0 "should be equal"

        testCase "add todo with any category refererence - Ok" <| fun _ ->
            let categoryId = Guid.NewGuid()
            let todo = mkTodo (Guid.NewGuid()) "test" [categoryId] []
            let aggregate = TodosAggregate'.Zero.AddTodo todo
            Expect.isOk aggregate "should be ok"
            let result = aggregate.OkValue
            Expect.equal (result.GetTodos() |> List.length) 1 "should be equal"
            Expect.isTrue true "should be true"

        testCase "remove a category reference affects any todo that references that category - Ok" <| fun _ ->
            let categoryId = Guid.NewGuid()
            let todo = mkTodo (Guid.NewGuid()) "test" [categoryId] []
            let aggregate = TodosAggregate'.Zero.AddTodo todo |> Result.get

            let aggregate' = aggregate.RemoveCategoryReference categoryId
            Expect.isOk aggregate' "should be ok"
            let result = aggregate'.OkValue
            let todo' =  result.GetTodos() |> List.head
            Expect.equal todo'.CategoryIds [] "should be equal"

        testCase "remove a category reference affects any todo that references that category 2 - Ok" <| fun _ ->
            let categoryId1 = Guid.NewGuid()
            let categoryId2 = Guid.NewGuid()
            let categoryId3 = Guid.NewGuid()
            let todo1 = mkTodo (Guid.NewGuid()) "test" [categoryId1; categoryId2] []
            let todo2 = mkTodo (Guid.NewGuid()) "test2" [categoryId1; categoryId2; categoryId3] []

            let aggregate =
                ResultCE.result {
                    let! aggregate = TodosAggregate'.Zero.AddTodo todo1
                    let! result = aggregate.AddTodo todo2
                    return result
                }   
            Expect.isOk aggregate "should be ok"
            let aggregate' = aggregate.OkValue  
            let aggregate'' = aggregate'.RemoveCategoryReference categoryId1 |> Result.get

            let todo1' =  aggregate''.GetTodos() |> List.item 0
            let todo2' =  aggregate''.GetTodos() |> List.item 1

            Expect.isFalse (todo1'.CategoryIds |> List.contains categoryId1) "should contain"
            Expect.isFalse (todo2'.CategoryIds |> List.contains categoryId1) "should contain"
    ]