
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
        testList "KafkaStateKeeperTests" [
            testCase "after initialized the kafka state keeper has the same current state from the trusted source and is zero - oK" <| fun _ ->
                currentVersionPgWithKafkaApp._reset()
                let todoReceiver = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClinet")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoReceiver pgStorage
                let sourceOfTruthStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                
                Expect.equal (sourceOfTruthStateViewer().OkValue) kafkaViewer.State "should be equal"
                Expect.equal kafkaViewer.State (0, TodosContext.Zero, None) "should be equal"

            testCase "initialize kafka state keeper and then force state sync with the event store source of truth - Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber pgStorage
                let todo = mkTodo (Guid.NewGuid()) "test" [] []
                let added = app.addTodo todo
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, currentState, _) = storageStateViewer() |> Result.get
                let todos = currentState.todos.todos.GetAll()
                Expect.equal 1 todos.Length "should be equal"

                let forceSync = kafkaViewer.ForceSyncWithEventStore()
                Expect.isOk forceSync "should be ok"
                let (_ , kafkaViewerTodosState, _) = kafkaViewer.State
                Expect.equal kafkaViewerTodosState currentState "should be equal"
                
            testCase "kafka state keeper will be able to evolve for a new events after refresh - Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber pgStorage
                let todo = mkTodo (Guid.NewGuid()) "test" [] []
                let added = app.addTodo todo
                let added' = added.OkValue
                
                let (_, deliveryResults) = added'
               
                let (_, deliveryResults) = added'
                let deliveryResult = deliveryResults.Head.Value.Head
                let offset = deliveryResult.Offset
                let partition =  deliveryResult.Partition.Value
                todoSubscriber.Assign2 (offset, partition)
                
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, currentState, _) = storageStateViewer() |> Result.get
                let todos = currentState.todos.todos.GetAll()
                Expect.equal 1 todos.Length "should be equal"
                
                // let refreshed = kafkaViewer.Refresh()
                // Expect.isOk refreshed "should be ok"
                
                let message = Tests.Sharpino.Sample.MultiVersionsTests.listenForSingleEvent (ApplicationInstance.Instance.GetGuid(), todoSubscriber,  deliveryResults)
                printf "message \n %A\n" message
                let okMessage = message.OkValue
                let deserMessage = okMessage |> serializer.Deserialize<TodoEvent>
                let todoEvent = deserMessage |> Result.get
                Expect.equal todoEvent (TodoEvent.TodoAdded todo) "should be equal"
                
                // let (_ , kafkaViewerTodosState, _) = kafkaViewer.State
                // Expect.equal kafkaViewerTodosState currentState "should be equal"
                
                
            testCase "kafka state keeper will be able to evolve for a new events after refresh. use listenwithRetries -  Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber pgStorage
                let todo = mkTodo (Guid.NewGuid()) "test" [] []
                let added = app.addTodo todo
                let added' = added.OkValue
               
                let (_, deliveryResults) = added'
                let deliveryResult = deliveryResults.Head.Value.Head
                let offset = deliveryResult.Offset
                let partition =  deliveryResult.Partition.Value
                todoSubscriber.Assign2 (offset, partition)
                
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, currentState, _) = storageStateViewer() |> Result.get
                let todos = currentState.todos.todos.GetAll()
                Expect.equal 1 todos.Length "should be equal"
                
                // let refreshed = kafkaViewer.Refresh()
                // Expect.isOk refreshed "should be ok"
                
                // let message = Tests.Sharpino.Sample.MultiVersionsTests.listenForSingleEvent (ApplicationInstance.Instance.GetGuid(), todoSubscriber,  deliveryResults)
                let message =
                    // MultiVersionsTests.listenForSingleEventWithTimeout (ApplicationInstance.Instance.GetGuid(), todoSubscriber,  deliveryResults, 1000)
                    MultiVersionsTests.listenForSingleEventWithRetries (ApplicationInstance.Instance.GetGuid(), todoSubscriber,  deliveryResults, 10)
                
                printf "message \n %A\n" message
                let okMessage = message.OkValue
                let deserMessage = okMessage |> serializer.Deserialize<TodoEvent>
                let todoEvent = deserMessage |> Result.get
                Expect.equal todoEvent (TodoEvent.TodoAdded todo) "should be equal"
                
                // let (_ , kafkaViewerTodosState, _) = kafkaViewer.State
                // Expect.equal kafkaViewerTodosState currentState "should be equal"
                
            ftestCase "kafka state keeper will be able to evolve for a new events after refresh. use listenwithTimeout -  Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber pgStorage
                let todo = mkTodo (Guid.NewGuid()) "test" [] []
                let added = app.addTodo todo
                let added' = added.OkValue
               
                let (_, deliveryResults) = added'
                let deliveryResult = deliveryResults.Head.Value.Head
                let offset = deliveryResult.Offset
                let partition =  deliveryResult.Partition.Value
                
                todoSubscriber.Assign2 (offset, partition)
                
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, currentState, _) = storageStateViewer() |> Result.get
                let todos = currentState.todos.todos.GetAll()
                Expect.equal 1 todos.Length "should be equal"
                
                let message =
                    MultiVersionsTests.listenForSingleEventWithTimeout (ApplicationInstance.Instance.GetGuid(), todoSubscriber,  deliveryResults, 10)
                
                printf "message \n %A\n" message
                let okMessage = message.OkValue
                let deserMessage = okMessage |> serializer.Deserialize<TodoEvent>
                let todoEvent = deserMessage |> Result.get
                Expect.equal todoEvent (TodoEvent.TodoAdded todo) "should be equal"
                
            testCase "kafka state keeper will be able to refresh to the current state by processing events independently -  Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber pgStorage
                let todo = mkTodo (Guid.NewGuid()) "test" [] []
                let added = app.addTodo todo
                let added' = added.OkValue
               
                let (_, deliveryResults) = added'
                let deliveryResult = deliveryResults.Head.Value.Head
                let offset = deliveryResult.Offset
                let partition =  deliveryResult.Partition.Value
                
                todoSubscriber.Assign2 (offset, partition)
                
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, currentState, _) = storageStateViewer() |> Result.get
                let todos = currentState.todos.todos.GetAll()
                Expect.equal 1 todos.Length "should be equal"
               
                kafkaViewer.Refresh()
                let (_, kafkaState, _) = kafkaViewer.State
                
                Expect.equal kafkaState currentState "should be equal"
                
            testCase "kafka state keeper will be able to refresh to the current state by processing events independently, add two todos (two events) -  Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber pgStorage
                let todo = mkTodo (Guid.NewGuid()) "test" [] []
                let todo2 = mkTodo (Guid.NewGuid()) "test2" [] []
                let added = app.add2Todos (todo,todo2)
                let added' = added.OkValue
               
                let (_, deliveryResults) = added'
                let deliveryResult = deliveryResults.Head.Value.Head
                let offset = deliveryResult.Offset
                let partition =  deliveryResult.Partition.Value
                
                todoSubscriber.Assign2 (offset, partition)
                
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, currentState, _) = storageStateViewer() |> Result.get
                let todos = currentState.todos.todos.GetAll()
                Expect.equal 2 todos.Length "should be equal"
               
                kafkaViewer.Refresh()
                kafkaViewer.Refresh()
                let (_, kafkaState, _) = kafkaViewer.State
                
                Expect.equal kafkaState currentState "should be equal"
                Expect.isTrue true "true"
                
            ftestCase "kafka state keeper see what's happen when refresh more than needed -  Ok" <| fun _ ->
                // expect that there will be just a timeout and then who cares
                
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber pgStorage
                let todo = mkTodo (Guid.NewGuid()) "test" [] []
                let added = app.addTodo todo
                let added' = added.OkValue
               
                let (_, deliveryResults) = added'
                let deliveryResult = deliveryResults.Head.Value.Head
                let offset = deliveryResult.Offset
                let partition =  deliveryResult.Partition.Value
                
                todoSubscriber.Assign2 (offset, partition)
                
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, currentState, _) = storageStateViewer() |> Result.get
                let todos = currentState.todos.todos.GetAll()
                Expect.equal 1 todos.Length "should be equal"
               
                kafkaViewer.Refresh()
                let (_, kafkaState, _) = kafkaViewer.State
                
                Expect.equal kafkaState currentState "should be equal"
                
        ]
        |> testSequenced