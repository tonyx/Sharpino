module testUtis

open System
open Expecto
open Sharpino.Sample._14.Definitions

[<Tests>]
let testUtils =
    testList "testUtils" [
        testCase "the id is always unique" <| fun _ ->
            let courseId1 = CourseId.New
            let courseId2 = CourseId.New
            Expect.notEqual courseId1.Id courseId2.Id "should be different"
            Expect.notEqual courseId1 courseId2 "should be different"
    ]
