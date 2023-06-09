
module Tests.Sharpino.Sample.Tags.Models.CategoriesTests

open Expecto
open System
open FSharp.Core

open Sharpino.Sample.Models.CategoriesModel
open Sharpino.Utils

[<Tests>]
let categoryModelTests =
    testList "categories model tests" [
        testCase "add category - Ok" <| fun _ ->
            let category = { Id = Guid.NewGuid(); Name = "test"}
            let categories = Categories.Zero.AddCategory category
            Expect.isOk categories "should be ok"
            let categories' = categories.OkValue
            Expect.equal (categories'.categories |> List.length) 1 "should be equal"

        testCase "add and remove a category - Ok" <| fun _ ->
            let category = { Id = Guid.NewGuid(); Name = "test"}
            let categories = Categories.Zero.AddCategory category
            Expect.isOk categories "should be ok"
            let categories' = categories.OkValue
            let categories'' = categories'.RemoveCategory category.Id
            Expect.isOk categories'' "should be ok"
            let result = categories''.OkValue
            Expect.equal (result.categories |> List.length) 0 "should be equal"

        testCase "try removing an unexisting category - Ko" <| fun _ ->
            let category = { Id = Guid.NewGuid(); Name = "test"}
            let categories = (Categories.Zero.AddCategory category) |> Result.get
            let wrongId = Guid.NewGuid()
            let result = categories.RemoveCategory wrongId
            Expect.isError result "should be error"
            let actualMsg = result |> getError
            Expect.equal (sprintf "A category with id '%A' does not exist" wrongId) actualMsg "should be equal"
    ]