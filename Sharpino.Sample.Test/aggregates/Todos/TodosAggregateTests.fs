
module Tests.Sharpino.Sample.TodosTests


open Expecto
open System
open FSharp.Core

open Tests.Sharpino.Shared

open Sharpino.Sample.TodosCluster
open Sharpino.Sample.Entities.Todos
open Sharpino.Sample.Entities.Categories
open Sharpino.Utils
open FsToolkit.ErrorHandling

[<Tests>]
let todosAggregateTests =
    testList "todos aggregate tests" [

        testCase "add todo - Ok" <| fun _ ->
            let todo = mkTodo (Guid.NewGuid()) "test" [] []
            let aggregate = TodosCluster.Zero.AddTodo todo
            Expect.isOk aggregate "should be ok"
            let result = aggregate.OkValue
            Expect.equal (result.GetTodos() |> List.length) 1 "should be equal"

        testCase "add category - Ok" <| fun _ ->
            let category = mkCategory (Guid.NewGuid()) "test"
            let aggregate = TodosCluster.Zero.AddCategory category
            Expect.isOk aggregate "should be ok"
            let result = aggregate.OkValue
            Expect.equal (result.GetCategories() |> List.length) 1 "should be equal"

        testCase "add a todo rererencing an unexisting category - Ko" <| fun _ ->
            let guid = Guid.NewGuid()
            let category = mkCategory guid "test"
            let aggregate = TodosCluster.Zero.AddCategory category |> Result.get
            let anotherGuid = Guid.NewGuid()
            let todo = mkTodo (Guid.NewGuid()) "test" [anotherGuid] []
            let aggregate' = aggregate.AddTodo todo
            Expect.isError aggregate' "should be error"
            let errMsg = aggregate' |> getError
            Expect.equal errMsg (sprintf "A category with id '%A' does not exist" anotherGuid) "should be equal"

        testCase "add a todo referencing an existing category - Ok" <| fun _ ->
            let category = mkCategory (Guid.NewGuid()) "test"
            let aggregate = TodosCluster.Zero.AddCategory category |> Result.get
            let todo = mkTodo (Guid.NewGuid()) "test" [category.Id] []
            let result = aggregate.AddTodo todo |> Result.get
            Expect.equal (result.GetTodos()) [todo] "should be equal"

        testCase "add a todo referencing an existing category 2 - Ok" <| fun _ ->
            let category1 = mkCategory (Guid.NewGuid()) "test1"
            let category2 = mkCategory (Guid.NewGuid()) "test2"
            let aggregate = TodosCluster.Zero.AddCategory category1
            Expect.isOk aggregate "should be ok"

            let aggregateWithCategories =
                (
                    ResultCE.result {
                        let! aggregate = TodosCluster.Zero.AddCategory category1 
                        let! result = aggregate.AddCategory category2
                        return result
                    }
                ).OkValue


            let todo = mkTodo (Guid.NewGuid()) "test" [category1.Id] []
            let aggregate'' = aggregateWithCategories.AddTodo todo
            Expect.isOk aggregate'' "should be ok"
            let result = aggregate''.OkValue
            Expect.equal (result.GetTodos() |> List.length) 1 "should be equal"

        testCase "add a todo referencing all the existing categories - Ok" <| fun _ ->
            let category1 = mkCategory (Guid.NewGuid()) "test1"
            let category2 = mkCategory (Guid.NewGuid()) "test2"
            let aggregateWithCategories =
                (
                    ResultCE.result {
                        let! aggregate = TodosCluster.Zero.AddCategory category1 
                        let! result = aggregate.AddCategory category2
                        return result
                    }
                ).OkValue

            let todo = mkTodo (Guid.NewGuid()) "test" [category1.Id; category2.Id] []   
            let aggregate = aggregateWithCategories.AddTodo todo
            Expect.isOk aggregate "should be ok"

            let result = aggregate.OkValue 
            Expect.equal (result.GetTodos() |> List.length) 1 "should be equal"

        testCase "add a todo referencing an existing and an unexisting category - Ko" <| fun _ ->
            let category1 = mkCategory (Guid.NewGuid()) "test1"
            let category2 = mkCategory (Guid.NewGuid()) "test2"
            let aggregateWithCategories =
                (
                    ResultCE.result {
                        let! aggregate = TodosCluster.Zero.AddCategory category1 
                        let! result = aggregate.AddCategory category2
                        return result
                    }
                ).OkValue

            let newGuid = Guid.NewGuid()
            let todo = mkTodo (Guid.NewGuid()) "test" [category1.Id; newGuid] []
            let result = aggregateWithCategories.AddTodo todo
            Expect.isError result "should be error"

            let errMsg = result |> getError
            Expect.equal errMsg (sprintf "A category with id '%A' does not exist" newGuid) "should be equal"

        testCase "when remove a category, all references to it should be removed from todos - Ok" <| fun _ ->
            let categoryId = Guid.NewGuid()
            let category = mkCategory categoryId "test"
            let aggregate = (TodosCluster.Zero.AddCategory category) |> Result.get
            let todo = mkTodo (Guid.NewGuid()) "test" [categoryId] []
            let aggregate' = aggregate.AddTodo todo  |> Result.get

            Expect.equal (aggregate'.GetTodos() |> List.length) 1 "should be equal"
            let result = aggregate'.RemoveCategory categoryId |> Result.get

            Expect.equal (result.GetTodos() |> List.length) 1 "should be equal"
            Expect.equal (result.GetTodos() |> List.head).CategoryIds [] "should be equal"

        testCase "when remove a category, all references to it should be removed from todos 2 - Ok" <| fun _ ->
            let categoryId = Guid.NewGuid()
            let categoryId2 = Guid.NewGuid()
            let category = mkCategory categoryId "test"
            let category2 = mkCategory categoryId2 "test2"
            let aggregate = (TodosCluster.Zero.AddCategory category) |> Result.get
            let aggregate2 = (aggregate.AddCategory category2) |> Result.get
            let todo = mkTodo (Guid.NewGuid()) "test" [categoryId; categoryId2] []
            let aggregate' = aggregate2.AddTodo todo  |> Result.get

            Expect.equal (aggregate'.GetTodos() |> List.length) 1 "should be equal"
            let result = aggregate'.RemoveCategory categoryId |> Result.get

            Expect.equal (result.GetTodos() |> List.length) 1 "should be equal"
            Expect.equal (result.GetTodos() |> List.head).CategoryIds [categoryId2] "should be equal"

        testCase "when remove a category, all references to it should be removed from todos 3 - Ok" <| fun _ ->
            let categoryId1 = Guid.NewGuid()
            let categoryId2 = Guid.NewGuid()
            let category1 = mkCategory categoryId1 "test"

            let category2 = mkCategory categoryId2 "test2"

            let aggregate1 = (TodosCluster.Zero.AddCategory category1).OkValue 
            let aggregate2 = (aggregate1.AddCategory category2).OkValue 

            let todo1 = mkTodo (Guid.NewGuid()) "test" [categoryId1; categoryId2] []

            let todo2 = mkTodo (Guid.NewGuid()) "another test" [categoryId1; categoryId2] []

            let aggregate' = (aggregate2.AddTodo todo1).OkValue
            let aggregate'' = (aggregate'.AddTodo todo2).OkValue

            Expect.equal (aggregate''.GetTodos() |> List.length) 2 "should be equal"
            let result = aggregate''.RemoveCategory categoryId1 
            Expect.isOk result "should be ok"
            let result' = result.OkValue

            Expect.equal (result'.GetTodos() |> List.length) 2 "should be equal"
            Expect.equal (result'.GetTodos() |> List.item 0).CategoryIds [categoryId2] "should be equal"
            Expect.equal (result'.GetTodos() |> List.item 1).CategoryIds [categoryId2] "should be equal"
    ]
