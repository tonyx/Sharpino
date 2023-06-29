
module Tests.Sharpino.Sample.DbStorageTest

open Expecto
open FsCheck
open FsCheck.Prop
open Expecto.Tests
open System
open FSharp.Core

open Sharpino
open Sharpino.Lib.EvStore
open Sharpino.Sample.EventStoreApp
open Sharpino.Sample.Models.TagsModel

[<Tests>]
let utilsTests =
    let SetUp() =
        let eventStore = EventStoreBridge()
        async {
            let result = eventStore.Reset("_01", "_tags")
            let result = eventStore.Reset("_01", "_todo")
            let result = eventStore.Reset("_02", "_todo")
            let result = eventStore.Reset("_01", "_categories")
            return result
        }
        |> Async.RunSynchronously

    testList "dbstorage spike" [
        testCase "send events to event store - ok" <| fun _ ->
            let eventStore = EventStoreBridge()
            let events = ["{}"; "{}"; "{}"] |> Collections.Generic.List

            eventStore.AddEvents ("_01", events, "_todo")
            // let x = eventStore.SendEvent()
            // printf "x: %A\n" x
            Expect.isTrue true "true"

        testCase "add a tag - ok" <| fun _ ->
            let _ = SetUp()
            let eventStore = EventStoreBridge()
            let eventStoreApp = EventStoreApp(eventStore)
            let tag = {Id = System.Guid.NewGuid(); Name = "tag1"; Color = Color.Green}     
            let result = eventStoreApp.AddTag tag
            Expect.isTrue true "true"

            async {
                let! sl = Async.Sleep 10
                return sl
            }
            |> Async.RunSynchronously

            let listened  = 
                async {
                    let! result = eventStore.ConsumeEvents("_01", "_tags")  |> Async.AwaitTask
                    return result
                }
                |> Async.RunSynchronously
                |> Seq.toList

            listened 
            |> List.iter (fun x -> printf "listenedX: %A\n" (System.Text.Encoding.UTF8.GetString(x.Event.Data.ToArray())))

        ftestCase "add a tag and then verify it is present - ok" <| fun _ ->
            let _ = SetUp()
            let eventStore = EventStoreBridge()
            let eventStoreApp = EventStoreApp(eventStore)
            let tag = {Id = System.Guid.NewGuid(); Name = "tag1"; Color = Color.Green}     
            let result = eventStoreApp.AddTag tag
            Expect.isTrue true "true"

            async {
                let! sl = Async.Sleep 10
                return sl
            }
            |> Async.RunSynchronously

            let tags = eventStoreApp.GetAllTags() |> Result.get
            Expect.equal tags [tag] "should be equal"

        testCase "add a tag and retrieve it - ok" <| fun _ ->
            let deleted = SetUp()
            let eventStore = EventStoreBridge()
            let eventStoreApp = EventStoreApp(eventStore)
            let tag = {Id = System.Guid.NewGuid(); Name = "tag1"; Color = Color.Blue}     
            let result = eventStoreApp.AddTag tag
            Expect.isTrue true "true"
            let result = eventStoreApp.GetAllTags()    
            Expect.isOk result "should be ok"
            Expect.equal (result |> Result.get) [tag] "should be equal"
    ] 