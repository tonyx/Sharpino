
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
open Microsoft.Extensions.Hosting

let resultToBool x =
    match x with
    | Ok _ -> true
    | Error _ -> false

[<Tests>]
    let kafkaTests =
        testList "KafkaStateKeeperTests" [
            testCase "after initialized the kafka state keeper has the same current state from the trusted source and is zero - oK" <| fun _ ->
                currentVersionPgWithKafkaApp._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let todoReceiver = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoReceiver storageStateViewer (ApplicationInstance.Instance.GetGuid())
                let sourceOfTruthStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                
                Expect.equal (sourceOfTruthStateViewer().OkValue) kafkaViewer.State "should be equal"
                Expect.equal kafkaViewer.State (0, TodosContext.Zero, None) "should be equal"

            testCase "any viewer will have a state aligned with the source of truth state after using forceSync  - Ok" <| fun _ ->
                // given
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

                let todo = mkTodo (Guid.NewGuid()) "test" [] []
                let added = app.addTodo todo
                Expect.isOk added "should be ok"
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, currentState, _) = storageStateViewer() |> Result.get
                let todos = currentState.todos.todos.GetAll()
                Expect.equal 1 todos.Length "should be equal"

                let forceSync = kafkaViewer.ForceSyncWithEventStore()
                Expect.isOk forceSync "should be ok"

                let (_ , kafkaViewerTodosState, _) = kafkaViewer.State
                Expect.equal kafkaViewerTodosState currentState "should be equal"

            testCase "any member of the app will return the delivery result that we can use to align the subscriber by using the offset and the partition number - Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClinet")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())
                let todo = mkTodo (Guid.NewGuid()) "test" [] []
                let added = app.addTodo todo
                let added' = added.OkValue
                let (_, deliveryResults) = added'
                let deliveryResult = deliveryResults.Head.Value.Head
                let offset = deliveryResult.Offset
                let partition =  deliveryResult.Partition.Value

                todoSubscriber.Assign2 (offset, partition)

                kafkaViewer.Refresh()

                let (_, kafkaState, _) = kafkaViewer.State
                let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, sourceOfTruthStateView, _) = sourceOfTruthstateViewer() |> Result.get
                Expect.equal kafkaState sourceOfTruthStateView "should be equal"

            testCase "do the same as the previous test with the difference that the refresh is called as a loop - Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClinet")  |> Result.get

                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

                let todo = mkTodo (Guid.NewGuid()) "test" [] []
                let added = app.addTodo todo
                let added' = added.OkValue
                let (_, deliveryResults) = added'
                let deliveryResult = deliveryResults.Head.Value.Head
                let offset = deliveryResult.Offset
                let partition =  deliveryResult.Partition.Value

                todoSubscriber.Assign2 (offset, partition)

                kafkaViewer.RefreshLoop()

                let (_, kafkaState, _) = kafkaViewer.State
                let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, sourceOfTruthStateView, _) = sourceOfTruthstateViewer() |> Result.get
                Expect.equal kafkaState sourceOfTruthStateView "should be equal"

            testCase "respect to the previous expriments, now I am using thi ping to tget offset and partition to set the kafka viewer to the starting point - Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClinet")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

                let ping = app._pingTodo()                
                let ping' = ping.OkValue
                let (_, deliveryResults) = ping'
                let deliverResult = deliveryResults.Head.Value.Head
                let offset = deliverResult.Offset
                let partition =  deliverResult.Partition.Value
                todoSubscriber.Assign2 (offset, partition)

                let todo = mkTodo (Guid.NewGuid()) "testmink" [] []
                let added = app.addTodo todo
                Expect.isOk added "should be ok"

                kafkaViewer.RefreshLoop()

                let (_, kafkaState, _) = kafkaViewer.State
                let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, sourceOfTruthStateView, _) = sourceOfTruthstateViewer() |> Result.get
                Expect.equal kafkaState sourceOfTruthStateView "should be equal"

            ftestCase "when you have a ping, then the current state should provide information to be able to assign the partition at construction - Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClinet")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

                let ping = app._pingTodo()                
                let ping' = ping.OkValue
                let (_, deliveryResults) = ping'
                let deliverResult = deliveryResults.Head.Value.Head
                let offset = deliverResult.Offset
                let partition =  deliverResult.Partition.Value


                todoSubscriber.Assign2 (offset, partition)

                let todo = mkTodo (Guid.NewGuid()) "testmink" [] []
                let added = app.addTodo todo
                Expect.isOk added "should be ok"

                kafkaViewer.RefreshLoop()

                let (_, kafkaState, _) = kafkaViewer.State
                let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, sourceOfTruthStateView, _) = sourceOfTruthstateViewer() |> Result.get
                Expect.equal kafkaState sourceOfTruthStateView "should be equal"




            testCase "add two todos  and verify by doing refresh twice that the state of kafkaviewer is aligned with single source of truth - Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())
                let todo = mkTodo (Guid.NewGuid()) "test" [] []
                let todo2 = mkTodo (Guid.NewGuid()) "test2" [] []
                let added = app.add2Todos (todo, todo2)

                let added' = added.OkValue
                let (_, deliveryResults) = added'
                let deliveryResult = deliveryResults.Head.Value.Head
                let offset = deliveryResult.Offset
                let partition =  deliveryResult.Partition.Value
                todoSubscriber.Assign2 (offset, partition)

                kafkaViewer.Refresh()
                kafkaViewer.Refresh()


                let (_, kafkaState, _) = kafkaViewer.State
                let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, sourceOfTruthStateView, _) = sourceOfTruthstateViewer() |> Result.get
                Expect.equal kafkaState sourceOfTruthStateView "should be equal"

            testCase "add two todos  and verify by doing refresh twice that the state of kafkaviewer is aligned with single source of truth. Third refresh will be error  - Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get


                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())
                let todo = mkTodo (Guid.NewGuid()) "test" [] []
                let todo2 = mkTodo (Guid.NewGuid()) "test2" [] []
                let added = app.add2Todos (todo, todo2)
                let added' = added.OkValue
                let (_, deliveryResults) = added'
                let deliveryResult = deliveryResults.Head.Value.Head
                let offset = deliveryResult.Offset
                let partition =  deliveryResult.Partition.Value
                todoSubscriber.Assign2 (offset, partition)

                let firstRefresh = kafkaViewer.Refresh()
                let secondRefresh = kafkaViewer.Refresh()

                Expect.isOk firstRefresh "should be ok"
                Expect.isOk secondRefresh "should be ok"

                let thirtRefresh = kafkaViewer.Refresh()
                Expect.isError thirtRefresh "should be error"

                let (_, kafkaState, _) = kafkaViewer.State
                let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, sourceOfTruthStateView, _) = sourceOfTruthstateViewer() |> Result.get
                Expect.equal kafkaState sourceOfTruthStateView "should be equal"

            testCase "should be able to do refresh unitl error and get the kafka state aligned  - Ok" <| fun _ ->

                let app = currentVersionPgWithKafkaApp
                app._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())
                let todo = mkTodo (Guid.NewGuid()) "test" [] []
                let todo2 = mkTodo (Guid.NewGuid()) "test2" [] []
                let added = app.add2Todos (todo, todo2)
                let added' = added.OkValue
                let (_, deliveryResults) = added'
                let deliveryResult = deliveryResults.Head.Value.Head
                let offset = deliveryResult.Offset
                let partition =  deliveryResult.Partition.Value
                todoSubscriber.Assign2 (offset, partition)

                let mutable refreshed = kafkaViewer.Refresh() |> resultToBool
                while refreshed do
                    refreshed <- kafkaViewer.Refresh() |> resultToBool

                let (_, kafkaState, _) = kafkaViewer.State
                let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, sourceOfTruthStateView, _) = sourceOfTruthstateViewer() |> Result.get
                Expect.equal kafkaState sourceOfTruthStateView "should be equal"

            testCase "should be able to do refresh until error and get the kafka state aligned. Use refresh loop on the class  - Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get

                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())
                let todo = mkTodo (Guid.NewGuid()) "test" [] []
                let todo2 = mkTodo (Guid.NewGuid()) "test2" [] []
                let added = app.add2Todos (todo, todo2)
                let added' = added.OkValue
                let (_, deliveryResults) = added'
                let deliveryResult = deliveryResults.Head.Value.Head
                let offset = deliveryResult.Offset
                let partition =  deliveryResult.Partition.Value
                todoSubscriber.Assign2 (offset, partition)

                let refreshed = kafkaViewer.RefreshLoop()

                let (_, kafkaState, _) = kafkaViewer.State
                let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, sourceOfTruthStateView, _) = sourceOfTruthstateViewer() |> Result.get
                Expect.equal kafkaState sourceOfTruthStateView "should be equal"

            testCase "should be able to do refresh until error and get the kafka state aligned. Use ping and then some method and then refresh loop on the class  - Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get

                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

                let pinged = app._pingTodo()
                let (_, deliveryResults) = pinged.OkValue
                let deliveryResult = deliveryResults.Head.Value.Head
                let deliveryResult = deliveryResults.Head.Value.Head
                let offset = deliveryResult.Offset
                let partition =  deliveryResult.Partition.Value
                todoSubscriber.Assign2 (offset, partition)

                let todo = mkTodo (Guid.NewGuid()) "test" [] []
                let todo2 = mkTodo (Guid.NewGuid()) "test2" [] []

                let added = app.add2Todos (todo, todo2)
                let added' = added.OkValue

                let refreshed = kafkaViewer.RefreshLoop()
                let refreshed = kafkaViewer.RefreshLoop()

                let (_, kafkaState, _) = kafkaViewer.State
                let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, sourceOfTruthStateView, _) = sourceOfTruthstateViewer() |> Result.get
                Expect.equal kafkaState sourceOfTruthStateView "should be equal"

            testCase "should be able to do refresh until error and get the kafka state aligned. Use refresh loop on the class, avoid explicit assign  - Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

                let forcedState = kafkaViewer.ForceSyncWithEventStore()

                let todo = mkTodo (Guid.NewGuid()) "test" [] []
                let todo2 = mkTodo (Guid.NewGuid()) "test2" [] []
                let added = app.add2Todos (todo, todo2)
                let added' = added.OkValue
                let (_, deliveryResults) = added'
                let deliveryResult = deliveryResults.Head.Value.Head
                let offset = deliveryResult.Offset
                let partition =  deliveryResult.Partition.Value

                todoSubscriber.Assign2 (offset, partition)

                let refreshed = kafkaViewer.RefreshLoop()

                let (_, kafkaState, _) = kafkaViewer.State
                let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, sourceOfTruthStateView, _) = sourceOfTruthstateViewer() |> Result.get
                Expect.equal kafkaState sourceOfTruthStateView "should be equal"

            testCase "add two todos  and verify by doing refresh tree times. Third time the refresh is error as there are not more events/messages - Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())
                let todo = mkTodo (Guid.NewGuid()) "test" [] []
                let todo2 = mkTodo (Guid.NewGuid()) "test2" [] []
                let added = app.add2Todos (todo, todo2)
                let added' = added.OkValue
                let (_, deliveryResults) = added'
                let deliveryResult = deliveryResults.Head.Value.Head
                let offset = deliveryResult.Offset
                let partition =  deliveryResult.Partition.Value
                todoSubscriber.Assign2 (offset, partition)

                kafkaViewer.Refresh()
                kafkaViewer.Refresh()

                let (_, kafkaState, _) = kafkaViewer.State
                let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, sourceOfTruthStateView, _) = sourceOfTruthstateViewer() |> Result.get
                Expect.equal kafkaState sourceOfTruthStateView "should be equal"
                
                kafkaViewer.Refresh()
                kafkaViewer.Refresh()

                let (_, kafkaState, _) = kafkaViewer.State
                let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, sourceOfTruthStateView, _) = sourceOfTruthstateViewer() |> Result.get
                Expect.equal kafkaState sourceOfTruthStateView "should be equal"
    
            testCase "kafka state keeper will be able to evolve for a new events after refresh - Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())
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
                
                let message = Tests.Sharpino.Sample.MultiVersionsTests.listenForSingleEvent (ApplicationInstance.Instance.GetGuid(), todoSubscriber,  deliveryResults)
                printf "message \n %A\n" message
                let okMessage = message.OkValue
                let deserMessage = okMessage |> serializer.Deserialize<TodoEvent>
                let todoEvent = deserMessage |> Result.get
                Expect.equal todoEvent (TodoEvent.TodoAdded todo) "should be equal"
                
            testCase "kafka state keeper will be able to evolve for a new events after refresh. use listenwithRetries -  Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())
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
                    MultiVersionsTests.listenForSingleEventWithRetries (ApplicationInstance.Instance.GetGuid(), todoSubscriber,  deliveryResults, 10)
                
                printf "message \n %A\n" message
                let okMessage = message.OkValue
                let deserMessage = okMessage |> serializer.Deserialize<TodoEvent>
                let todoEvent = deserMessage |> Result.get
                Expect.equal todoEvent (TodoEvent.TodoAdded todo) "should be equal"
                
            testCase "kafka state keeper will be able to refresh to the current state by processing events independently -  Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())
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
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())
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
                
            testCase "kafka state keeper see what's happen when refresh more than needed -  Ok" <| fun _ ->
                
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())
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