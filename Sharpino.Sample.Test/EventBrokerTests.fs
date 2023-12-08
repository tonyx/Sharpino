
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

    // todo: thi will come next
    testList "build state by querying the event broker" [
        testCase "true" <| fun _ ->
            Expect.isTrue true "true"
    ]
    |> testSequenced