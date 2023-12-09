
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
let todosContestTests =
    testList "todos context tests" [

        testCase "add todo - Ok" <| fun _ ->
            let todo = mkTodo (Guid.NewGuid()) "test" [] []
            let context = TodosContext.Zero.AddTodo todo
            Expect.isOk context "should be ok"
            let result = context.OkValue
            Expect.equal (result.GetTodos() |> List.length) 1 "should be equal"

        testCase "add category - Ok" <| fun _ ->
            let category = mkCategory (Guid.NewGuid()) "test"
            let context = TodosContext.Zero.AddCategory category
            Expect.isOk context "should be ok"
            let result = context.OkValue
            Expect.equal (result.GetCategories() |> List.length) 1 "should be equal"

        testCase "add a todo rererencing an unexisting category - Ko" <| fun _ ->
            let guid = Guid.NewGuid()
            let category = mkCategory guid "test"
            let context = TodosContext.Zero.AddCategory category |> Result.get
            let anotherGuid = Guid.NewGuid()
            let todo = mkTodo (Guid.NewGuid()) "test" [anotherGuid] []
            let context' = context.AddTodo todo
            Expect.isError context' "should be error"
            let errMsg = context' |> getError
            Expect.equal errMsg (sprintf "A category with id '%A' does not exist" anotherGuid) "should be equal"

        testCase "add a todo referencing an existing category - Ok" <| fun _ ->
            let category = mkCategory (Guid.NewGuid()) "test"
            let context = TodosContext.Zero.AddCategory category |> Result.get
            let todo = mkTodo (Guid.NewGuid()) "test" [category.Id] []
            let result = context.AddTodo todo |> Result.get
            Expect.equal (result.GetTodos()) [todo] "should be equal"

        testCase "add a todo referencing an existing category 2 - Ok" <| fun _ ->
            let category1 = mkCategory (Guid.NewGuid()) "test1"
            let category2 = mkCategory (Guid.NewGuid()) "test2"
            let context = TodosContext.Zero.AddCategory category1
            Expect.isOk context "should be ok"

            let contextWithCategories =
                (
                    ResultCE.result {
                        let! context = TodosContext.Zero.AddCategory category1 
                        let! result = context.AddCategory category2
                        return result
                    }
                ).OkValue

            let todo = mkTodo (Guid.NewGuid()) "test" [category1.Id] []
            let context'' = contextWithCategories.AddTodo todo
            Expect.isOk context'' "should be ok"
            let result = context''.OkValue
            Expect.equal (result.GetTodos() |> List.length) 1 "should be equal"

        testCase "add a todo referencing all the existing categories - Ok" <| fun _ ->
            let category1 = mkCategory (Guid.NewGuid()) "test1"
            let category2 = mkCategory (Guid.NewGuid()) "test2"
            let contextWithCategories =
                (
                    ResultCE.result {
                        let! context = TodosContext.Zero.AddCategory category1 
                        let! result = context.AddCategory category2
                        return result
                    }
                ).OkValue

            let todo = mkTodo (Guid.NewGuid()) "test" [category1.Id; category2.Id] []   
            let context = contextWithCategories.AddTodo todo
            Expect.isOk context "should be ok"

            let result = context.OkValue 
            Expect.equal (result.GetTodos() |> List.length) 1 "should be equal"

        testCase "add a todo referencing an existing and an unexisting category - Ko" <| fun _ ->
            let category1 = mkCategory (Guid.NewGuid()) "test1"
            let category2 = mkCategory (Guid.NewGuid()) "test2"
            let contextWithCategories =
                (
                    ResultCE.result {
                        let! context = TodosContext.Zero.AddCategory category1 
                        let! result = context.AddCategory category2
                        return result
                    }
                ).OkValue

            let newGuid = Guid.NewGuid()
            let todo = mkTodo (Guid.NewGuid()) "test" [category1.Id; newGuid] []
            let result = contextWithCategories.AddTodo todo
            Expect.isError result "should be error"

            let errMsg = result |> getError
            Expect.equal errMsg (sprintf "A category with id '%A' does not exist" newGuid) "should be equal"

        testCase "when remove a category, all references to it should be removed from todos - Ok" <| fun _ ->
            let categoryId = Guid.NewGuid()
            let category = mkCategory categoryId "test"
            let context = (TodosContext.Zero.AddCategory category) |> Result.get
            let todo = mkTodo (Guid.NewGuid()) "test" [categoryId] []
            let context' = context.AddTodo todo  |> Result.get

            Expect.equal (context'.GetTodos() |> List.length) 1 "should be equal"
            let result = context'.RemoveCategory categoryId |> Result.get

            Expect.equal (result.GetTodos() |> List.length) 1 "should be equal"
            Expect.equal (result.GetTodos() |> List.head).CategoryIds [] "should be equal"

        testCase "when remove a category, all references to it should be removed from todos 2 - Ok" <| fun _ ->
            let categoryId = Guid.NewGuid()
            let categoryId2 = Guid.NewGuid()
            let category = mkCategory categoryId "test"
            let category2 = mkCategory categoryId2 "test2"
            let context = (TodosContext.Zero.AddCategory category) |> Result.get
            let context2 = (context.AddCategory category2) |> Result.get
            let todo = mkTodo (Guid.NewGuid()) "test" [categoryId; categoryId2] []
            let context' = context2.AddTodo todo  |> Result.get

            Expect.equal (context'.GetTodos() |> List.length) 1 "should be equal"
            let result = context'.RemoveCategory categoryId |> Result.get

            Expect.equal (result.GetTodos() |> List.length) 1 "should be equal"
            Expect.equal (result.GetTodos() |> List.head).CategoryIds [categoryId2] "should be equal"

        testCase "when remove a category, all references to it should be removed from todos 3 - Ok" <| fun _ ->
            let categoryId1 = Guid.NewGuid()
            let categoryId2 = Guid.NewGuid()
            let category1 = mkCategory categoryId1 "test"

            let category2 = mkCategory categoryId2 "test2"

            let context1 = (TodosContext.Zero.AddCategory category1).OkValue 
            let context2 = (context1.AddCategory category2).OkValue 

            let todo1 = mkTodo (Guid.NewGuid()) "test" [categoryId1; categoryId2] []

            let todo2 = mkTodo (Guid.NewGuid()) "another test" [categoryId1; categoryId2] []

            let context' = (context2.AddTodo todo1).OkValue
            let context'' = (context'.AddTodo todo2).OkValue

            Expect.equal (context''.GetTodos() |> List.length) 2 "should be equal"
            let result = context''.RemoveCategory categoryId1 
            Expect.isOk result "should be ok"
            let result' = result.OkValue

            Expect.equal (result'.GetTodos() |> List.length) 2 "should be equal"
            Expect.equal (result'.GetTodos() |> List.item 0).CategoryIds [categoryId2] "should be equal"
            Expect.equal (result'.GetTodos() |> List.item 1).CategoryIds [categoryId2] "should be equal"
    ]
