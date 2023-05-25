
module Tests.Tonyx.EventSourcing.Sample02.Tags.Models.TagsTests

open Expecto
open System
open FSharp.Core

open Tonyx.EventSourcing.Sample_02.Tags.Models.TagsModel
open Tonyx.EventSourcing.Utils

[<Tests>]
let tagModelTests =
    testList "tag model 02 upgrade tests" [
        testCase "add a tag - Ok"  <| fun _ ->
            let tag = { Id = Guid.NewGuid(); Name = "test"; Color = Color.Blue }
            let tags = Tags.Zero.AddTag tag
            Expect.isOk tags "should be ok"
            let result = tags.OkValue
            Expect.equal (result.tags |> List.length) 1 "should be equal"

        testCase "add and remove a tag - Ok" <| fun _ ->
            let tag = { Id = Guid.NewGuid(); Name = "test"; Color = Color.Blue }
            let tags = Tags.Zero.AddTag tag |> Result.get
            let tags' = tags.RemoveTag tag.Id
            Expect.isOk tags' "should be ok"
            let result = tags'.OkValue
            Expect.equal (result.tags |> List.length) 0 "should be equal"

        testCase "try removing an unexisting tag - Ko" <| fun _ ->
            let tag = { Id = Guid.NewGuid(); Name = "test"; Color = Color.Blue }
            let tags = Tags.Zero.AddTag tag
            Expect.isOk tags "should be ok"
            let tags' = tags.OkValue
            let wrongId = Guid.NewGuid()
            let result = tags'.RemoveTag wrongId
            Expect.isError result "should be error"
            let actualMsg = result |> getError
            Expect.equal (sprintf "A tag with id '%A' does not exist" wrongId) actualMsg "should be equal"
    ]

