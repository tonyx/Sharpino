module Tests.Sharpino.Sample.Tags.TagsTests
open Tests.Sharpino.Shared

open Expecto
open System
open FSharp.Core

open Sharpino.Sample.TagsCluster
open Sharpino.Sample.Entities.Tags
open Sharpino.Sample.Shared.Entities

open Sharpino.Utils

[<Tests>]
let tagsAggregateTests =
    testList "tags aggregate tests" [
        testCase "add a tag - Ok" <| fun _ ->
            let tag = mkTag (Guid.NewGuid()) "test" Color.Blue
            let aggregate = TagsCluster.Zero.AddTag tag
            Expect.isOk aggregate "should be ok"
            let result = aggregate |> Result.get
            Expect.equal (result.GetTags() |> List.length) 1 "should be equal"

        testCase "add and remove a tag - Ok" <| fun _ ->
            let tag = mkTag (Guid.NewGuid()) "test" Color.Blue
            let aggregate = TagsCluster.Zero.AddTag tag
            Expect.isOk aggregate "should be ok"
            let result = aggregate |> Result.get
            Expect.equal (result.GetTags() |> List.length) 1 "should be equal"

            let result' = result.RemoveTag tag.Id |> Result.get
            Expect.equal (result'.GetTags() |> List.length) 0 "should be equal"

        testCase "try removing an unexisting tag - Ko" <| fun _ ->
            let tag = mkTag (Guid.NewGuid()) "test" Color.Blue
            let aggregate = TagsCluster.Zero.AddTag tag
            Expect.isOk aggregate "should be ok"
            let result = aggregate |> Result.get
            Expect.equal (result.GetTags() |> List.length) 1 "should be equal"
            let newGuid = Guid.NewGuid()
            let result' = result.RemoveTag newGuid
            Expect.isError result' "should be error"
            let errMsg = result' |> getError
            Expect.equal errMsg (sprintf "A tag with id '%A' does not exist" newGuid) "should be equal"
    ]