
module Tests.Sharpino.Sample.EventBrokerTests

open Expecto
open System
open FSharp.Core

open Sharpino
open Tests.Sharpino.Sample.MultiVersionsTests
open Sharpino.ApplicationInstance
open Sharpino.Sample
open Sharpino.Sample.TodosCluster
open Sharpino.Sample.TagsCluster
open Sharpino.Sample.Todos
open Sharpino.Sample.Entities.Categories
open Sharpino.Sample.Entities.Todos
open Sharpino.Sample.Entities.Tags
open Sharpino.Sample.Tags
open Sharpino.Sample.Tags.TagsEvents
open Sharpino.Sample.Shared.Entities
open Sharpino.Utils
open Sharpino.EventSourcing.Sample
open Sharpino.EventSourcing.Sample.AppVersions
open Sharpino.Sample.Todos.TodoEvents
open Tests.Sharpino.Shared
open Sharpino.KafkaReceiver

open Sharpino.TestUtils
open Sharpino.KafkaReceiver
open System.Threading
open FsToolkit.ErrorHandling
open Sharpino.KafkaBroker
open Sharpino.Storage
open log4net

[<Tests>]
let eventBrokerStateBuildingTests =
    let listenForEventwithTimeout (appId: Guid, receiver: KafkaSubscriber) =
        result {
            let! received = receiver.consumeWithTimeOut()
            let! deserialized = received.Message.Value |> serializer.Deserialize<BrokerMessage> 
            let mutable deserialized' = deserialized
            let mutable found = deserialized'.ApplicationId = appId

            while (not found) do
                let! received = receiver.consumeWithTimeOut()
                let! deserialized = received.Message.Value |> serializer.Deserialize<BrokerMessage> 
                deserialized' <- deserialized
                found <- deserialized'.ApplicationId = appId
            return deserialized'.Event
        }

    testList "build state by querying the event broker" [
        testCase "asdfa" <| fun _ ->   
            printf "hereXXX\n"
            Expect.isTrue true "true"
        
        testCase "there are no events, wait for the timeout" <| fun _ ->
            let todoReceiver = new KafkaSubscriber("localhost:9092", TodosCluster.Version, TodosCluster.StorageName, "sharpinoTestClient")
            let sut = currentVersionPgWithKafkaApp
            let events = listenForEventwithTimeout (ApplicationInstance.Instance.GetGuid(), todoReceiver)
            Expect.isTrue true "true"

        // ftestCase "given a context, create a state getter and having no events will return the initial state - Ok" <| fun _ ->
        //     let todoEventBrokerState = new EventBrokerState(TodosCluster.Version, TodosCluster.StorageName, "sharpinoTestClient")


            // let todoReceiver = new KafkaSubscriber("localhost:9092", TodosCluster.Version, TodosCluster.StorageName, "sharpinoTestClient")
            // let sut = currentVersionPgWithKafkaApp
            // let events = listenForEventwithTimeout (ApplicationInstance.Instance.GetGuid(), todoReceiver)
            Expect.isTrue true "true"

    ]
    |> testSequenced