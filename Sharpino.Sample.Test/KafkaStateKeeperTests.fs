
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
        ftestList "KafkaStateKeeperTests" [
            testCase "after initialized the kafka state keeper has the same current state from the trusted source and is zero - oK" <| fun _ ->
                currentVersionPgWithKafkaApp._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let todoReceiver = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoReceiver storageStateViewer (ApplicationInstance.Instance.GetGuid())
                let sourceOfTruthStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                
                Expect.equal (sourceOfTruthStateViewer().OkValue) kafkaViewer.State "should be equal"
                Expect.equal kafkaViewer.State (0, TodosContext.Zero, None, None) "should be equal"

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
                let (_, currentState, _, _) = storageStateViewer() |> Result.get
                let todos = currentState.todos.todos.GetAll()
                Expect.equal 1 todos.Length "should be equal"

                let forceSync = kafkaViewer.ForceSyncWithEventStore()
                Expect.isOk forceSync "should be ok"

                let (_ , kafkaViewerTodosState, _, _) = kafkaViewer.State
                Expect.equal kafkaViewerTodosState currentState "should be equal"

            testCase "any member of the app will return the delivery result that we can use to align the subscriber by using the offset and the partition number - Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let todo = mkTodo (Guid.NewGuid()) "test" [] []
                let added = app.addTodo todo
                Expect.isOk added "shold be ok"
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClinet")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

                kafkaViewer.Refresh()

                let (_, kafkaState, _, _) = kafkaViewer.State
                let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, sourceOfTruthStateView, _, _) = sourceOfTruthstateViewer() |> Result.get
                Expect.equal kafkaState sourceOfTruthStateView "should be equal"

            testCase "do the same as the previous test with the difference that the refresh is called as a loop - Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage

                let todo = mkTodo (Guid.NewGuid()) "test" [] []
                let added = app.addTodo todo
                Expect.isOk added "should be ok"

                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClinet")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

                kafkaViewer.RefreshLoop()

                let (_, kafkaState, _, _) = kafkaViewer.State
                let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, sourceOfTruthStateView, _, _) = sourceOfTruthstateViewer() |> Result.get
                Expect.equal kafkaState sourceOfTruthStateView "should be equal"

            testCase "respect to the previous expriments, now I am using thi ping to tget offset and partition to set the kafka viewer to the starting point - Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage

                let ping = app._pingTodo()                
                Expect.isOk ping "should be ok"

                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClinet") |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

                let todo = mkTodo (Guid.NewGuid()) "testmink" [] []
                let added = app.addTodo todo
                Expect.isOk added "should be ok"

                kafkaViewer.RefreshLoop()

                let (_, kafkaState, _, _) = kafkaViewer.State
                let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, sourceOfTruthStateView, _, _ ) = sourceOfTruthstateViewer() |> Result.get
                Expect.equal kafkaState sourceOfTruthStateView "should be equal"

            testCase "when you have a ping, then the current state should provide information to be able to assign the partition at construction - Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage

                let ping = app._pingTodo()                
                Expect.isOk ping "should be ok"

                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClinet")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

                let todo = mkTodo (Guid.NewGuid()) "testmink" [] []
                let added = app.addTodo todo
                Expect.isOk added "should be ok"

                kafkaViewer.RefreshLoop()

                let (_, kafkaState, _, _) = kafkaViewer.State
                let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, sourceOfTruthStateView, _, _) = sourceOfTruthstateViewer() |> Result.get
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
                Expect.isOk added "should be ok"

                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

                kafkaViewer.Refresh()
                kafkaViewer.Refresh()

                let (_, kafkaState, _, _) = kafkaViewer.State
                let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, sourceOfTruthStateView, _, _) = sourceOfTruthstateViewer() |> Result.get
                Expect.equal kafkaState sourceOfTruthStateView "should be equal"

            testCase "add two todos  and verify by doing refresh twice that the state of kafkaviewer is aligned with single source of truth. Third refresh will be error  - Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()

                let todo = mkTodo (Guid.NewGuid()) "test" [] []
                let todo2 = mkTodo (Guid.NewGuid()) "test2" [] []
                let added = app.add2Todos (todo, todo2)
                Expect.isOk added "should be ok"

                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

                let firstRefresh = kafkaViewer.RefreshLoop()

                let (_, kafkaState, _, _) = kafkaViewer.State
                let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, sourceOfTruthStateView, _, _) = sourceOfTruthstateViewer() |> Result.get
                Expect.equal kafkaState sourceOfTruthStateView "should be equal"

            testCase "should be able to do refresh unitl error and get the kafka state aligned  - Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage

                let todo = mkTodo (Guid.NewGuid()) "test" [] []
                let todo2 = mkTodo (Guid.NewGuid()) "test2" [] []
                let added = app.add2Todos (todo, todo2)
                Expect.isOk added "should be ok"
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

                let added' = added.OkValue
                let (_, deliveryResults) = added'

                let mutable refreshed = kafkaViewer.Refresh() |> resultToBool
                while refreshed do
                    refreshed <- kafkaViewer.Refresh() |> resultToBool

                let (_, kafkaState, _, _) = kafkaViewer.State
                let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, sourceOfTruthStateView, _, _) = sourceOfTruthstateViewer() |> Result.get
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
                Expect.isOk added "should be ok"
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

                let refreshed = kafkaViewer.RefreshLoop()

                let (_, kafkaState, _, _) = kafkaViewer.State
                let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, sourceOfTruthStateView, _, _) = sourceOfTruthstateViewer() |> Result.get
                Expect.equal kafkaState sourceOfTruthStateView "should be equal"

            testCase "should be able to do refresh until error and get the kafka state aligned. Use ping and then some method and then refresh loop on the class  - Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage

                let pinged = app._pingTodo()
                Expect.isOk pinged "should be ok"
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

                let todo = mkTodo (Guid.NewGuid()) "test" [] []
                let todo2 = mkTodo (Guid.NewGuid()) "test2" [] []

                let added = app.add2Todos (todo, todo2)
                let added' = added.OkValue

                let refreshed = kafkaViewer.RefreshLoop()

                let (_, kafkaState, _, _) = kafkaViewer.State
                let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, sourceOfTruthStateView, _, _) = sourceOfTruthstateViewer() |> Result.get
                Expect.equal kafkaState sourceOfTruthStateView "should be equal"

            testCase "should be able to do refresh until error and get the kafka state aligned. Use refresh loop on the class, avoid explicit assign  - Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage

                let todo = mkTodo (Guid.NewGuid()) "test" [] []
                let todo2 = mkTodo (Guid.NewGuid()) "test2" [] []
                let added = app.add2Todos (todo, todo2)

                Expect.isOk added "should be ok"
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

                let refreshed = kafkaViewer.RefreshLoop()

                let (_, kafkaState, _, _) = kafkaViewer.State
                let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, sourceOfTruthStateView, _, _) = sourceOfTruthstateViewer() |> Result.get
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
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

                kafkaViewer.RefreshLoop()

                let (_, kafkaState, _, _) = kafkaViewer.State
                let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, sourceOfTruthStateView, _, _) = sourceOfTruthstateViewer() |> Result.get
                Expect.equal kafkaState sourceOfTruthStateView "should be equal"
    
            testCase "kafka state keeper will be able to evolve for a new events after refresh - Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())
                let todo = mkTodo (Guid.NewGuid()) "test" [] []
                let added = app.addTodo todo
                Expect.isOk added "should be ok"
                
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, currentState, _, _) = storageStateViewer() |> Result.get
                let todos = currentState.todos.todos.GetAll()
                Expect.equal 1 todos.Length "should be equal"
                
            testCase "kafka state keeper will be able to evolve for a new events after refresh. use listenwithRetries -  Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage

                let todo = mkTodo (Guid.NewGuid()) "test" [] []
                let added = app.addTodo todo
                Expect.isOk added "should be ok"
               
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, currentState, _, _) = storageStateViewer() |> Result.get
                let todos = currentState.todos.todos.GetAll()
                Expect.equal 1 todos.Length "should be equal"
                
                kafkaViewer.RefreshLoop()
                let (_, kafkaState, _, _) = kafkaViewer.State
                Expect.equal kafkaState currentState "should be equal"
                
            testCase "kafka state keeper will be able to refresh to the current state by processing events independently -  Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let todo = mkTodo (Guid.NewGuid()) "test" [] []
                let added = app.addTodo todo
                let added' = added.OkValue
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())
                
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, currentState, _, _) = storageStateViewer() |> Result.get
                let todos = currentState.todos.todos.GetAll()
                Expect.equal 1 todos.Length "should be equal"
               
                kafkaViewer.RefreshLoop()

                let (_, kafkaState, _, _) = kafkaViewer.State
                Expect.equal kafkaState currentState "should be equal"
                
            testCase "kafka state keeper will be able to refresh to the current state by processing events independently, add two todos (two events) -  Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                // let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                // let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())
                let todo = mkTodo (Guid.NewGuid()) "test" [] []
                let todo2 = mkTodo (Guid.NewGuid()) "test2" [] []
                let added = app.add2Todos (todo,todo2)
                Expect.isOk added "should be ok"
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())
                
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, currentState, _, _) = storageStateViewer() |> Result.get
                let todos = currentState.todos.todos.GetAll()
                Expect.equal 2 todos.Length "should be equal"

                kafkaViewer.RefreshLoop()

                let (_, kafkaState, _, _) = kafkaViewer.State
                
                Expect.equal kafkaState currentState "should be equal"
                
            testCase "kafka state keeper see what's happen when refresh more than needed -  Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let todo = mkTodo (Guid.NewGuid()) "test" [] []
                let added = app.addTodo todo
                Expect.isOk added "should be ok"
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())
                
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let (_, currentState, _, _) = storageStateViewer() |> Result.get
                let todos = currentState.todos.todos.GetAll()
                Expect.equal 1 todos.Length "should be equal"
               
                kafkaViewer.RefreshLoop()
                let (_, kafkaState, _, _) = kafkaViewer.State
                Expect.equal kafkaState currentState "should be equal"

            testCase "add many todo and see the kafkaViewer state -  Ok" <| fun _ ->
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                let todo0 = mkTodo (Guid.NewGuid()) "test0" [] []
                let todo1 = mkTodo (Guid.NewGuid()) "test1" [] []
                let todo2 = mkTodo (Guid.NewGuid()) "test2" [] []
                let todo3 = mkTodo (Guid.NewGuid()) "test3" [] []
                let todo4 = mkTodo (Guid.NewGuid()) "test4" [] []
                let todo5 = mkTodo (Guid.NewGuid()) "test5" [] []
                let todo6 = mkTodo (Guid.NewGuid()) "test6" [] []
                let todo7 = mkTodo (Guid.NewGuid()) "test7" [] []
                let todo8 = mkTodo (Guid.NewGuid()) "test8" [] []
                let todo9 = mkTodo (Guid.NewGuid()) "test9" [] []

                let pinged = app._pingTodo()

                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
                let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

                let added0 = app.addTodo todo0
                let added1 = app.addTodo todo1
                let added2 = app.addTodo todo2
                let added3 = app.addTodo todo3
                let added4 = app.addTodo todo4
                let added5 = app.addTodo todo5
                let added6 = app.addTodo todo6
                let added7 = app.addTodo todo7
                let added8 = app.addTodo todo8
                let added9 = app.addTodo todo9

                kafkaViewer.RefreshLoop()
                let (_, currentState, _, _) = storageStateViewer() |> Result.get
                let (_, kafkaState, _, _) = kafkaViewer.State
                Expect.equal kafkaState currentState "should be equal"

        ]
        |> testSequenced