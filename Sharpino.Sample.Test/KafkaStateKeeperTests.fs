
module Tests.Sharpino.Sample.KafkaStateKeeperTests

open Expecto
open System
open FSharp.Core

open Sharpino
open Sharpino.ApplicationInstance
open Sharpino.Sample
open Sharpino.Sample.TodosContext
open Sharpino.Sample.TagsContext
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

open Sharpino.TestUtils
open Sharpino.KafkaReceiver
open Sharpino.KafkaBroker
open System.Threading
open FsToolkit.ErrorHandling
open Sharpino.KafkaBroker
open Sharpino.Storage
open Farmer
open log4net

[<Tests>]
    let kafkaTests =
        ftestList "KafkaStateKeeperTests" [
            testCase "after initialized the kafka state keeper has state zero" <| fun _ ->
                let todoReceiver = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClinet")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoReceiver pgStorage
                Expect.equal (0, TodosContext.Zero) kafkaViewer.State "should be equal"

            ftestCase "initialize kafka state keeper and then force state sync with the event store source of truth - Ok" <| fun _ ->
                let app = currentPostgresApp
                app._reset()
                let todoReceiver = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClinet")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoReceiver pgStorage
                let todo = mkTodo (Guid.NewGuid()) "test" [] []
                let added = app.addTodo todo
                let storageStateViewer = CommandHandler.getStorageStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, currentState) = storageStateViewer() |> Result.get
                let todos = currentState.todos.todos.GetAll()
                Expect.equal 1 todos.Length "should be equal"

                let forceSync = kafkaViewer.ForceSyncWithEventStore()
                Expect.isOk forceSync "should be ok"
                let (_ , kafkaViewerTodosState) = kafkaViewer.State
                Expect.equal kafkaViewerTodosState currentState "should be equal"


        ]
        |> testSequenced