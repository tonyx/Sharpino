
module Tests.Tonyx.EventSourcing.Sample.TodosTests

open Expecto
open System
open FSharp.Core

open Tonyx.EventSourcing.Sample.TodosAggregate
open Tonyx.EventSourcing.Sample.Todos.Models.TodosModel
open Tonyx.EventSourcing.Sample.Todos.Models.CategoriesModel
open Tonyx.EventSourcing.Utils
open Tonyx.EventSourcing.Sample
open FsToolkit.ErrorHandling
open Microsoft.FSharp.Quotations

[<Tests>]
let todosAggregateTests =
    testList "todos aggregate tests" [

        testCase "add todo - Ok" <| fun _ ->
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = []}
            let aggregate = TodosAggregate.Zero.AddTodo todo
            Expect.isOk aggregate "should be ok"
            let result = aggregate.OkValue
            Expect.equal (result.GetTodos() |> List.length) 1 "should be equal"

        testCase "add category - Ok" <| fun _ ->
            let category = { Id = Guid.NewGuid(); Name = "test"}
            let aggregate = TodosAggregate.Zero.AddCategory category
            Expect.isOk aggregate "should be ok"
            let result = aggregate.OkValue
            Expect.equal (result.GetCategories() |> List.length) 1 "should be equal"

        testCase "add a todo rererencing an unexisting category - Ko" <| fun _ ->
            let guid = Guid.NewGuid()
            let category = { Id = guid; Name = "test"}
            let aggregate = TodosAggregate.Zero.AddCategory category |> Result.get
            let anotherGuid = Guid.NewGuid()
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = [anotherGuid]; TagIds = [] }
            let aggregate' = aggregate.AddTodo todo
            Expect.isError aggregate' "should be error"
            let errMsg = aggregate' |> getError
            Expect.equal errMsg (sprintf "A category with id '%A' does not exist" anotherGuid) "should be equal"

        testCase "add a todo referencing an existing category - Ok" <| fun _ ->
            let category = { Id = Guid.NewGuid(); Name = "test"}
            let aggregate = TodosAggregate.Zero.AddCategory category |> Result.get
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = [category.Id]; TagIds = []}
            let result = aggregate.AddTodo todo |> Result.get
            Expect.equal (result.GetTodos()) [todo] "should be equal"

        testCase "add a todo referencing an existing category 2 - Ok" <| fun _ ->
            let category1 = { Id = Guid.NewGuid(); Name = "test1"}
            let category2 = { Id = Guid.NewGuid(); Name = "test2"}
            let aggregate = TodosAggregate.Zero.AddCategory category1
            Expect.isOk aggregate "should be ok"

            let aggregateWithCategories =
                (
                    ResultCE.result {
                        let! aggregate = TodosAggregate.Zero.AddCategory category1 
                        let! result = aggregate.AddCategory category2
                        return result
                    }
                ).OkValue

            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = [category1.Id]; TagIds = []}
            let aggregate'' = aggregateWithCategories.AddTodo todo
            Expect.isOk aggregate'' "should be ok"
            let result = aggregate''.OkValue
            Expect.equal (result.GetTodos() |> List.length) 1 "should be equal"

        testCase "add a todo referencing all the existing categories - Ok" <| fun _ ->
            let category1 = { Id = Guid.NewGuid(); Name = "test1"}
            let category2 = { Id = Guid.NewGuid(); Name = "test2"}
            let aggregateWithCategories =
                (
                    ResultCE.result {
                        let! aggregate = TodosAggregate.Zero.AddCategory category1 
                        let! result = aggregate.AddCategory category2
                        return result
                    }
                ).OkValue

            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = [category1.Id; category2.Id]; TagIds = []}
            let aggregate = aggregateWithCategories.AddTodo todo
            Expect.isOk aggregate "should be ok"

            let result = aggregate.OkValue 
            Expect.equal (result.GetTodos() |> List.length) 1 "should be equal"

        testCase "add a todo referencing an existing and an unexisting category - Ko" <| fun _ ->
            let category1 = { Id = Guid.NewGuid(); Name = "test1"}
            let category2 = { Id = Guid.NewGuid(); Name = "test2"}
            let aggregateWithCategories =
                (
                    ResultCE.result {
                        let! aggregate = TodosAggregate.Zero.AddCategory category1 
                        let! result = aggregate.AddCategory category2
                        return result
                    }
                ).OkValue

            let newGuid = Guid.NewGuid()
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = [category1.Id; newGuid]; TagIds = []}
            let result = aggregateWithCategories.AddTodo todo
            Expect.isError result "should be error"

            let errMsg = result |> getError
            Expect.equal errMsg (sprintf "A category with id '%A' does not exist" newGuid) "should be equal"

        testCase "when remove a category, all references to it should be removed from todos - Ok" <| fun _ ->
            let categoryId = Guid.NewGuid()
            let category = { Id = categoryId; Name = "test"}
            let aggregate = (TodosAggregate.Zero.AddCategory category) |> Result.get
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = [categoryId]; TagIds = []}
            let aggregate' = aggregate.AddTodo todo  |> Result.get

            Expect.equal (aggregate'.GetTodos() |> List.length) 1 "should be equal"
            let result = aggregate'.RemoveCategory categoryId |> Result.get

            Expect.equal (result.GetTodos() |> List.length) 1 "should be equal"
            Expect.equal (result.GetTodos() |> List.head).CategoryIds [] "should be equal"

        testCase "when remove a category, all references to it should be removed from todos 2 - Ok" <| fun _ ->
            let categoryId = Guid.NewGuid()
            let categoryId2 = Guid.NewGuid()
            let category = { Id = categoryId; Name = "test"}
            let category2 = { Id = categoryId2; Name = "test2"}
            let aggregate = (TodosAggregate.Zero.AddCategory category) |> Result.get
            let aggregate2 = (aggregate.AddCategory category2) |> Result.get
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = [categoryId; categoryId2]; TagIds = []}
            let aggregate' = aggregate2.AddTodo todo  |> Result.get

            Expect.equal (aggregate'.GetTodos() |> List.length) 1 "should be equal"
            let result = aggregate'.RemoveCategory categoryId |> Result.get

            Expect.equal (result.GetTodos() |> List.length) 1 "should be equal"
            Expect.equal (result.GetTodos() |> List.head).CategoryIds [categoryId2] "should be equal"

        testCase "when remove a category, all references to it should be removed from todos 3 - Ok" <| fun _ ->
            let categoryId1 = Guid.NewGuid()
            let categoryId2 = Guid.NewGuid()
            let category1 = { Id = categoryId1; Name = "test"}
            let category2 = { Id = categoryId2; Name = "test2"}

            let aggregate1 = (TodosAggregate.Zero.AddCategory category1).OkValue 
            let aggregate2 = (aggregate1.AddCategory category2).OkValue 

            let todo1 = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = [categoryId1; categoryId2]; TagIds = []}
            let todo2 = { Id = Guid.NewGuid(); Description = "another test"; CategoryIds = [categoryId1; categoryId2]; TagIds = []}

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
