

module Tests.Tonyx.EventSourcing.Sample.Tags.TagsTests

open Expecto
open System
open FSharp.Core

open Tonyx.EventSourcing.Sample.TagsAggregate
open Tonyx.EventSourcing.Sample.Tags.Models.TagsModel

open Tonyx.EventSourcing.Utils
open Tonyx.EventSourcing.Sample

open AppVersions
open VersionAnnotations
open System.Reflection

[<Tests>]
let reflectionTest =
    testList "reflection tests" [
        ptestCase "check metadata" <| fun _ ->
            let myapp = AppVersions.applicationMemoryStorage

            // let myType =  typeof<IApplication>.GetField("_storage", BindingFlags.NonPublic ||| BindingFlags.Instance)

            let fieldInfo =  
                typeof<Tonyx.EventSourcing.Sample.App.App>.GetMember("getAllTodos").[0]

            let pgStorage: Tonyx.EventSourcing.IStorage = Tonyx.EventSourcing.DbStorage.PgDb()
            let ap = Tonyx.EventSourcing.Sample.App.App(pgStorage)

            let fieldInfo' =  
                typeof<Tonyx.EventSourcing.Sample.App.App>.GetMember(nameof(ap.getAllTodos)).[0]

            // printf "name %s\n" 
            // let m = ap.getAllTodos.GetType().GetCustomAttributes(typeof<CurrentVersionService>, false).[0] :?> CurrentVersionService
            // let attr = fieldInfo // .GetValue.GetCustomAttributes(typeof<CurrentVersionApp>, false)
            // let attribute = fieldInfo.GetCustomAttributes(typeof<CurrentVersionApp>, false).[0] // :?> CurrentVersionApp

            let attribute = fieldInfo'.GetCustomAttributes(typeof<CurrentVersionService>, false).[0] :?> CurrentVersionService

            // attribute.ServiceName |> s
            // printf "ATTRIBUTE: %A\n" (attribute.ServiceName)
            printf "ATTRIBUTE: %A\n" (attribute.ServiceName)

            Expect.isTrue true "true"

    ]


[<Tests>]
let tagsAggregateTests =
    testList "tags aggregate tests" [
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