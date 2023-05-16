
module Tests.Tonyx.EventSourcing.Sample02.Tags.TagsTests

open Expecto
open System
open FSharp.Core

open Tonyx.EventSourcing.Sample_02.TagsAggregate
open Tonyx.EventSourcing.Sample_02.Tags.Models.TagsModel
open Tonyx.EventSourcing.Utils
open Tonyx.EventSourcing.Sample_02

[<Tests>]
let tagsAggregateTests =
    testList "tags aggregate update tests" [
        testCase "add a tag - Ok" <| fun _ ->
            let tag = { Id = Guid.NewGuid(); Name = "test"; Color = Color.Blue}
            let aggregate = TagsAggregate.Zero.AddTag tag
            Expect.isOk aggregate "should be ok"
            let result = aggregate |> Result.get
            Expect.equal (result.GetTags() |> List.length) 1 "should be equal"

        testCase "add and remove a tag - Ok" <| fun _ ->
            let tag = { Id = Guid.NewGuid(); Name = "test"; Color = Color.Blue}
            let aggregate = TagsAggregate.Zero.AddTag tag
            Expect.isOk aggregate "should be ok"
            let result = aggregate |> Result.get
            Expect.equal (result.GetTags() |> List.length) 1 "should be equal"

            let result' = result.RemoveTag tag.Id |> Result.get
            Expect.equal (result'.GetTags() |> List.length) 0 "should be equal"

        testCase "try removing an unexisting tag - Ko" <| fun _ ->
            let tag = { Id = Guid.NewGuid(); Name = "test"; Color = Color.Blue}
            let aggregate = TagsAggregate.Zero.AddTag tag
            Expect.isOk aggregate "should be ok"
            let result = aggregate |> Result.get
            Expect.equal (result.GetTags() |> List.length) 1 "should be equal"
            let newGuid = Guid.NewGuid()
            let result' = result.RemoveTag newGuid
            Expect.isError result' "should be error"
            let errMsg = result' |> getError
            Expect.equal errMsg (sprintf "A tag with id '%A' does not exist" newGuid) "should be equal"
    ]