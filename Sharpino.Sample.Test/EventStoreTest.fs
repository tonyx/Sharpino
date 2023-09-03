
module Tests.Sharpino.Sample.DbStorageTest

open Expecto
open System
open FSharp.Core

open Sharpino
open Sharpino.Storage
open Sharpino.Utils
open Sharpino.Sample.EventStoreApp
open Sharpino.Sample.Entities.Tags
open Sharpino.Sample.Entities.Todos
open Sharpino.Sample.Entities.Categories

open Sharpino.Sample.TagsAggregate
open Sharpino.Sample.TodosAggregate
open Sharpino.Sample.CategoriesAggregate

open Sharpino.Sample.Todos.TodoEvents
open Sharpino.Sample.Categories.CategoriesEvents
open Sharpino.Sample.Tags.TagsEvents

[<Tests>]
let utilsTests =
    let eventStoreConnection = "esdb://localhost:2113?tls=false"
    let eventStoreBridge = Sharpino.EventStore.EventStoreStorage(eventStoreConnection) :> ILightStorage
    let SetUp() =

        eventStoreBridge.ResetSnapshots "_01" "_tags"   
        eventStoreBridge.ResetEvents "_01" "_tags"
        eventStoreBridge.ResetSnapshots "_01" "_todo"
        eventStoreBridge.ResetEvents "_01" "_todo"
        eventStoreBridge.ResetSnapshots "_02" "_todo"
        eventStoreBridge.ResetEvents "_02" "_todo"      
        eventStoreBridge.ResetSnapshots "_01" "_categories"
        eventStoreBridge.ResetEvents "_01" "_categories"

        async {
            do! Async.Sleep 20
            return ()
        } |> Async.RunSynchronously
        
    let eventStoreApp = EventStoreApp(eventStoreBridge)

    ptestList "eventstore tests will delete them" [
        testCase "foo" <| fun _ ->
            Expect.isTrue true "true"

    ] 
    |> testSequenced