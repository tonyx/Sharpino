
module Tests.Sharpino.Sample.Tags.Models.TagsTests

open Tests.Sharpino.Shared
open Sharpino.Sample.Shared.Entities
open Sharpino.Sample.Entities.Tags

open Expecto
open System
open FSharp.Core

open Sharpino.Utils

[<Tests>]
let tagModelTests =
    testList "tag model tests" [
        testCase "add a tag - Ok"  <| fun _ ->
            let tag = mkTag (Guid.NewGuid()) "test" Color.Blue
            let tags = Tags.Zero.AddTag tag
            Expect.isOk tags "should be ok"
            let result = tags.OkValue
            Expect.equal (result.Tags.GetAll() |> List.length) 1 "should be equal"

        testCase "add and remove a tag - Ok" <| fun _ ->
            let tag = mkTag (Guid.NewGuid()) "test" Color.Blue
            let tags = Tags.Zero.AddTag tag |> Result.get
            let tags' = tags.RemoveTag tag.Id
            Expect.isOk tags' "should be ok"
            let result = tags'.OkValue
            Expect.equal (result.Tags.GetAll() |> List.length) 0 "should be equal"

        testCase "try removing an unexisting tag - Ko" <| fun _ ->
            let tag = mkTag (Guid.NewGuid()) "test" Color.Blue
            let tags = Tags.Zero.AddTag tag
            Expect.isOk tags "should be ok"
            let tags' = tags.OkValue
            let wrongId = Guid.NewGuid()
            let result = tags'.RemoveTag wrongId
            Expect.isError result "should be error"
            let actualMsg = result |> getError
            Expect.equal (sprintf "A tag with id '%A' does not exist" wrongId) actualMsg "should be equal"
    ]

