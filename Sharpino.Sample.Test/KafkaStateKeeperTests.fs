
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
open Sharpino.Sample.EventBrokerBasedApp

let resultToBool x =
    match x with
    | Ok _ -> true
    | Error _ -> false

[<Tests>]
    let kafkaTests =
        let serializer = Utils.JsonSerializer(Utils.serSettings) :> Utils.ISerializer
        ftestList "Kafka consumer and subscribers test" [
            testCase "retrieve row event by todoSubscriber and consume - Ok " <| fun _ ->
                // given
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient") |> Result.get
                let todo = mkTodo (Guid.NewGuid()) "testX" [] []
                let todoAdded = app.addTodo todo
                Expect.isOk todoAdded "should be ok"

                let deliveryResult = todoAdded.OkValue |> snd |> List.head |> Option.get |> List.head
                let position = deliveryResult.Offset
                let partition = deliveryResult.Partition
                todoSubscriber.Assign2(position, partition)

                // when
                let consumeResult = todoSubscriber.Consume() 
                let message = consumeResult.Message.Value 
                let brokerMessage = message |> serializer.Deserialize<BrokerMessage> |> Result.get

                // then
                let event = brokerMessage.Event |> serializer.Deserialize<TodoEvent> |> Result.get
                let expected = TodoEvent.TodoAdded todo
                Expect.equal expected event "should be equal"

            testCase "retrieve event and build current state by KafkaStateViewer - OK" <| fun _ ->
                // given
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient") |> Result.get
                let todo = mkTodo (Guid.NewGuid()) "testX" [] []
                let todoAdded = app.addTodo todo
                Expect.isOk todoAdded "should be ok"
                let deliveryResult = todoAdded.OkValue |> snd |> List.head |> Option.get |> List.head
                let position = deliveryResult.Offset
                let partition = deliveryResult.Partition
                todoSubscriber.Assign2(position, partition)

                // when
                let todosKafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber (CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage) (ApplicationInstance.Instance.GetGuid())
                let todosKafkaViewerState = todosKafkaViewer.State() |> fun (_, b, _, _) -> b

                // then
                Expect.equal (todosKafkaViewerState.todos.todos.GetAll()) [todo] "should be equal"

            testCase "retrieve event and build current state by KafkaStateViewer (different order of assignment) - OK" <| fun _ ->
                // given
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient") |> Result.get
                let todo = mkTodo (Guid.NewGuid()) "testX" [] []
                let todoAdded = app.addTodo todo
                Expect.isOk todoAdded "should be ok"
                let deliveryResult = todoAdded.OkValue |> snd |> List.head |> Option.get |> List.head
                let position = deliveryResult.Offset
                let partition = deliveryResult.Partition

                // when
                let todosKafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber (CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage) (ApplicationInstance.Instance.GetGuid())
                let todosKafkaViewerState = todosKafkaViewer.State() |> fun (_, b, _, _) -> b

                todoSubscriber.Assign2(position, partition)

                // then
                Expect.equal (todosKafkaViewerState.todos.todos.GetAll()) [todo] "should be equal"

            testCase "add many events and build current state by KafkaStateViewer - OK" <| fun _ ->
                // given
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient") |> Result.get
                let todo = mkTodo (Guid.NewGuid()) "testX" [] []
                let todoAdded = app.addTodo todo
                Expect.isOk todoAdded "should be ok"
                let deliveryResult = todoAdded.OkValue |> snd |> List.head |> Option.get |> List.head
                let position = deliveryResult.Offset
                let partition = deliveryResult.Partition
                // when
                let todosKafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber (CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage) (ApplicationInstance.Instance.GetGuid())

                todoSubscriber.Assign2(position, partition)

                let todo2 = mkTodo (Guid.NewGuid()) "testX2" [] []
                let todo2Added = app.addTodo todo2

                todosKafkaViewer.Refresh() |> ignore
                let todosKafkaViewerState = todosKafkaViewer.State() |> fun (_, b, _, _) -> b
                let actual = todosKafkaViewerState.todos.todos.GetAll() |> Set.ofList
                let expected = [todo; todo2] |> Set.ofList
                Expect.equal actual expected "should be equal"

            testCase "add many events and build current state by KafkaStateViewer 2 - OK" <| fun _ ->
                // given
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient") |> Result.get
                let todo = mkTodo (Guid.NewGuid()) "testX" [] []
                let todoAdded = app.addTodo todo
                Expect.isOk todoAdded "should be ok"
                let deliveryResult = todoAdded.OkValue |> snd |> List.head |> Option.get |> List.head
                let position = deliveryResult.Offset
                let partition = deliveryResult.Partition
                // when
                let todosKafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber (CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage) (ApplicationInstance.Instance.GetGuid())

                todoSubscriber.Assign2(position, partition)

                let todo2 = mkTodo (Guid.NewGuid()) "testX2" [] []
                let todo2Added = app.addTodo todo2

                let todo3 = mkTodo (Guid.NewGuid()) "testX3" [] []
                let todo3Added = app.addTodo todo3

                todosKafkaViewer.Refresh() |> ignore
                let todosKafkaViewerState = todosKafkaViewer.State() |> fun (_, b, _, _) -> b
                let actual = todosKafkaViewerState.todos.todos.GetAll() |> Set.ofList
                let expected = [todo; todo2; todo3] |> Set.ofList
                Expect.equal actual expected "should be equal"

            testCase "add many events and get the state by labmda viewer  - OK" <| fun _ ->
                // given
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient") |> Result.get
                let todo = mkTodo (Guid.NewGuid()) "testX" [] []
                let todoAdded = app.addTodo todo
                Expect.isOk todoAdded "should be ok"
                let deliveryResult = todoAdded.OkValue |> snd |> List.head |> Option.get |> List.head
                let position = deliveryResult.Offset
                let partition = deliveryResult.Partition
                // when
                let todosKafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber (CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage) (ApplicationInstance.Instance.GetGuid())

                let currentStateViewer = fun () -> 
                    todosKafkaViewer.Refresh() |> ignore
                    todosKafkaViewer.State() 

                todoSubscriber.Assign2(position, partition)

                let todo2 = mkTodo (Guid.NewGuid()) "testX2" [] []
                let todo2Added = app.addTodo todo2

                let todo3 = mkTodo (Guid.NewGuid()) "testX3" [] []
                let todo3Added = app.addTodo todo3

                // todosKafkaViewer.Refresh() |> ignore

                // let todosKafkaViewerState = todosKafkaViewer.State() |> fun (_, b, _, _) -> b
                let todosKafkaViewerState = currentStateViewer() |> fun (_, b, _, _) -> b

                let actual = todosKafkaViewerState.todos.todos.GetAll() |> Set.ofList
                let expected = [todo; todo2; todo3] |> Set.ofList
                Expect.equal actual expected "should be equal"

            testCase "add many events and get the state by lambda viewer, then add other events and tet the state again  - OK" <| fun _ ->
                // given
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient") |> Result.get
                let todo = mkTodo (Guid.NewGuid()) "testX" [] []
                let todoAdded = app.addTodo todo
                Expect.isOk todoAdded "should be ok"
                let deliveryResult = todoAdded.OkValue |> snd |> List.head |> Option.get |> List.head
                let position = deliveryResult.Offset
                let partition = deliveryResult.Partition
                // when
                let todosKafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber (CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage) (ApplicationInstance.Instance.GetGuid())

                let currentStateViewer = fun () -> 
                    todosKafkaViewer.Refresh() |> ignore
                    todosKafkaViewer.State() 

                todoSubscriber.Assign2(position, partition)

                let todo2 = mkTodo (Guid.NewGuid()) "testX2" [] []
                let todo2Added = app.addTodo todo2

                let todo3 = mkTodo (Guid.NewGuid()) "testX3" [] []
                let todo3Added = app.addTodo todo3

                let todosKafkaViewerState = currentStateViewer() |> fun (_, b, _, _) -> b

                let actual = todosKafkaViewerState.todos.todos.GetAll() |> Set.ofList
                let expected = [todo; todo2; todo3] |> Set.ofList
                Expect.equal actual expected "should be equal"

                let todo4 = mkTodo (Guid.NewGuid()) "testX4" [] []
                let todo4Added = app.addTodo todo4
                let todosKafkaViewerState = currentStateViewer() |> fun (_, b, _, _) -> b
                let actual = todosKafkaViewerState.todos.todos.GetAll() |> Set.ofList
                let expected = [todo; todo2; todo3; todo4] |> Set.ofList
                Expect.equal actual expected "should be equal"

            testCase "use the version of app that is based on the kafka state viewer  - OK" <| fun _ ->
                // given
                let app = currentVersionPgWithKafkaApp
                app._reset()
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient") |> Result.get
                let todo = mkTodo (Guid.NewGuid()) "testX" [] []
                let todoAdded = app.addTodo todo
                Expect.isOk todoAdded "should be ok"
                let deliveryResult = todoAdded.OkValue |> snd |> List.head |> Option.get |> List.head
                let position = deliveryResult.Offset
                let partition = deliveryResult.Partition
                // when
                let todosKafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber (CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage) (ApplicationInstance.Instance.GetGuid())

                let currentStateViewer = fun () -> 
                    todosKafkaViewer.Refresh() |> ignore
                    todosKafkaViewer.State() 

                todoSubscriber.Assign2(position, partition)

                let todo2 = mkTodo (Guid.NewGuid()) "testX2" [] []
                let todo2Added = app.addTodo todo2

                let todo3 = mkTodo (Guid.NewGuid()) "testX3" [] []
                let todo3Added = app.addTodo todo3

                let todosKafkaViewerState = currentStateViewer() |> fun (_, b, _, _) -> b

                let actual = todosKafkaViewerState.todos.todos.GetAll() |> Set.ofList
                let expected = [todo; todo2; todo3] |> Set.ofList
                Expect.equal actual expected "should be equal"

            testCase "eventBroker based app add and retrieve todo - Ok" <| fun _ ->
                resetDb pgStorage
                resetAppId()
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient") |> Result.get
                let app = EventBrokerBasedApp(pgStorage, localHostbroker)
                let todo = mkTodo (Guid.NewGuid()) "testX" [] []
                let todoAdded = app.AddTodo todo
                Expect.isOk todoAdded "should be ok"
                let todos = app.GetAllTodos()
                Expect.isOk todos "should be ok"
                let actual = todos.OkValue 
                Expect.equal actual [todo] "should be equal"

            testCase "eventBroker based app add and retrieve a category - Ok" <| fun _ ->
                resetDb pgStorage
                resetAppId()
                let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient") |> Result.get
                let app = EventBrokerBasedApp(pgStorage, localHostbroker)
                let category = mkCategory (Guid.NewGuid()) "testX" 
                let categoryAdded = app.AddCategory category
                Expect.isOk categoryAdded "should be ok"
                let categories = app.GetAllCategories()
                Expect.isOk categories "should be ok"
                let actual = categories.OkValue
                Expect.equal actual [category] "should be equal"

            testCase "eventBroker based app adds the same todo twice - Error" <| fun _ ->
                resetDb pgStorage
                resetAppId()
                let app = EventBrokerBasedApp(pgStorage, localHostbroker)
                let todo = mkTodo (Guid.NewGuid()) "testX" [] []
                let todoAdded = app.AddTodo todo
                Expect.isOk todoAdded "should be ok"
                let todoAddedAgain = app.AddTodo todo
                Expect.isError todoAddedAgain "should be error"

            testCase "add a todo with a valid category - Ok" <| fun _ ->
                resetDb pgStorage
                resetAppId()
                let app = EventBrokerBasedApp(pgStorage, localHostbroker)
                let categoryId = Guid.NewGuid()       
                let category = mkCategory categoryId "testCategory"
                let categoryAdded = app.AddCategory category
                Expect.isOk categoryAdded "should be ok"
                let todo = mkTodo (Guid.NewGuid()) "testX" [categoryId] []
                let todoAdded = app.AddTodo todo
                Expect.isOk todoAdded "should be ok"

            testCase "try add a todo with an invalid category - Ko" <| fun _ ->
                resetDb pgStorage
                resetAppId()
                let app = EventBrokerBasedApp(pgStorage, localHostbroker)
                let todo = mkTodo (Guid.NewGuid()) "testX" [Guid.NewGuid()] []
                let tryAdd = app.AddTodo todo
                Expect.isError tryAdd "should be error"

            testCase "add a tag - Ok" <| fun _ ->
                resetDb pgStorage
                resetAppId()
                let app = EventBrokerBasedApp(pgStorage, localHostbroker)
                let tag = mkTag (Guid.NewGuid()) "testTag" Color.Red
                let tagAdded = app.AddTag tag 
                Expect.isOk tagAdded "should be ok"

            testCase "add two todos, one has an unexisting category - Error" <| fun _ ->
                resetDb pgStorage
                resetAppId()
                let app = EventBrokerBasedApp(pgStorage, localHostbroker)
                let firstTodo = mkTodo (Guid.NewGuid()) "testX" [] []
                let secondTodo = mkTodo (Guid.NewGuid()) "testX" [Guid.NewGuid()] []
                let tryAdd = app.Add2Todos (firstTodo, secondTodo)
                Expect.isError tryAdd "should be error"

            testCase "add a todo with an unexisting tag - Ok" <| fun _ ->
                resetDb pgStorage
                resetAppId()
                let app = EventBrokerBasedApp(pgStorage, localHostbroker)
                let tagId = Guid.NewGuid()
                let todo = mkTodo (Guid.NewGuid()) "testX" [] [tagId]
                let todoAdded = app.AddTodo todo
                Expect.isError todoAdded "should be error"

            testCase "add and remove a todo - Ok" <| fun _ ->
                resetDb pgStorage
                resetAppId()
                let app = EventBrokerBasedApp(pgStorage, localHostbroker)
                let todo = mkTodo (Guid.NewGuid()) "testX" [] []
                let todoId = todo.Id
                let added = app.AddTodo todo
                Expect.isOk added "should be ok"
                let removed = app.RemoveTodo todoId
                let todos = app.GetAllTodos()
                let actual = todos.OkValue
                Expect.equal actual [] "should be equal"

            testCase "when remove a category then all the references to that category should be removed from todos - Ok " <| fun _ ->
                resetDb pgStorage
                resetAppId()
                let app = EventBrokerBasedApp(pgStorage, localHostbroker)
                let categorId = Guid.NewGuid()
                let category = mkCategory categorId "testCategory"
                let todo = mkTodo (Guid.NewGuid()) "testX" [categorId] []
                let categoryAdded = app.AddCategory category
                Expect.isOk categoryAdded "should be ok"
                let todoAdded = app.AddTodo todo
                Expect.isOk todoAdded "should be ok"
                let categories = app.GetAllCategories()
                Expect.isOk categories "should be ok"
                let actualCategories = categories.OkValue
                Expect.equal actualCategories [category] "should be equal"
                let removed = app.RemoveCategory categorId
                Expect.isOk removed "should be ok"
                let todos = app.GetAllTodos()
                Expect.isOk todos "should be ok"
                let actualTodos = todos.OkValue
                let categoryRemoved = app.RemoveCategory categorId
                let categories' = app.GetAllCategories()
                let actualCategories' = categories'.OkValue
                Expect.equal actualCategories' [] "should be equal"

                let retrievedTodos = app.GetAllTodos()
                Expect.isOk retrievedTodos "should be ok"
                let actualTodos' = retrievedTodos.OkValue
                Expect.equal actualTodos'.Length 1 "should be equal"
                let firstTodo = actualTodos'.[0]
                Expect.equal firstTodo.CategoryIds [] "should be equal"

            testCase "when remove a tag then all references to that tag should be removed from tags - Ok" <| fun _ ->
                resetDb pgStorage
                resetAppId()
                let app = EventBrokerBasedApp(pgStorage, localHostbroker)
                let tagId = Guid.NewGuid()
                let tag = mkTag tagId "testTag" Color.Red
                let todo = mkTodo (Guid.NewGuid()) "testX" [] [tagId]
                let tagAdded = app.AddTag tag
                Expect.isOk tagAdded "should be ok"
                let todoAdded = app.AddTodo todo
                Expect.isOk todoAdded "should be ok"
                let tags = app.GetAllTags()
                Expect.isOk tags "should be ok"
                let actualTags = tags.OkValue
                Expect.equal actualTags [tag] "should be equal"
                let removed = app.RemoveTag tagId
                Expect.isOk removed "should be ok"
                let todos = app.GetAllTodos()
                Expect.isOk todos "should be ok"

                let actualTodos = todos.OkValue
                let tagRemoved = app.RemoveTag tagId
                // Expect.isOk tagRemoved "should be ok"

                let tags' = app.GetAllTags()
                let actualTags' = tags'.OkValue
                Expect.equal actualTags' [] "should be equal"

                let retrievedTodos = app.GetAllTodos()
                Expect.isOk retrievedTodos "should be ok"

                let actualTodos' = retrievedTodos.OkValue
                Expect.equal actualTodos'.Length 1 "should be equal"
                let firstTodo = actualTodos'.[0]
                Expect.equal firstTodo.TagIds [] "should be equal"







            

            // testCase "after initialized the kafka state keeper has the same current state from the trusted source and is zero - oK" <| fun _ ->
            //     currentVersionPgWithKafkaApp._reset()
            //     let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
            //     let todoReceiver = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
            //     let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoReceiver storageStateViewer (ApplicationInstance.Instance.GetGuid())
            //     let sourceOfTruthStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
                
            //     Expect.equal (sourceOfTruthStateViewer().OkValue) (kafkaViewer.State ()) "should be equal"
            //     Expect.equal (kafkaViewer.State ()) (0, TodosContext.Zero, None, None) "should be equal"

            // testCase "any viewer will have a state aligned with the source of truth state after using forceSync  - Ok" <| fun _ ->
            //     // given
            //     let app = currentVersionPgWithKafkaApp
            //     app._reset()
            //     let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage

            //     let todo = mkTodo (Guid.NewGuid()) "test" [] []
            //     let added = app.addTodo todo
            //     let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
            //     let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

            //     Expect.isOk added "should be ok"
            //     let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
            //     let (_, currentState, _, _) = storageStateViewer() |> Result.get
            //     let todos = currentState.todos.todos.GetAll()
            //     Expect.equal 1 todos.Length "should be equal"

            //     let forceSync = kafkaViewer.ForceSyncWithSourceOfTruth()
            //     Expect.isOk forceSync "should be ok"

            //     let (_ , kafkaViewerTodosState, _, _) = (kafkaViewer.State ())
            //     Expect.equal kafkaViewerTodosState currentState "should be equal"

            // testCase "any member of the app will return the delivery result that we can use to align the subscriber by using the offset and the partition number - Ok" <| fun _ ->
            //     let app = currentVersionPgWithKafkaApp
            //     app._reset()
            //     let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
            //     let todo = mkTodo (Guid.NewGuid()) "test" [] []
            //     let added = app.addTodo todo
            //     Expect.isOk added "shold be ok"
            //     let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
            //     let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

            //     kafkaViewer.Refresh()

            //     let (_, kafkaState, _, _) = (kafkaViewer.State ())
            //     let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
            //     let (_, sourceOfTruthStateView, _, _) = sourceOfTruthstateViewer() |> Result.get
            //     Expect.equal kafkaState sourceOfTruthStateView "should be equal"

            // testCase "do the same as the previous test with the difference that the refresh is called as a loop - Ok" <| fun _ ->
            //     let app = currentVersionPgWithKafkaApp
            //     app._reset()
            //     let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage

            //     let todo = mkTodo (Guid.NewGuid()) "test" [] []
            //     let added = app.addTodo todo
            //     Expect.isOk added "should be ok"

            //     let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClinet")  |> Result.get
            //     let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

            //     kafkaViewer.RefreshLoop()

            //     let (_, kafkaState, _, _) = (kafkaViewer.State ())
            //     let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
            //     let (_, sourceOfTruthStateView, _, _) = sourceOfTruthstateViewer() |> Result.get
            //     Expect.equal kafkaState sourceOfTruthStateView "should be equal"

            // testCase "respect to the previous expriments, now I am using thi ping to tget offset and partition to set the kafka viewer to the starting point - Ok" <| fun _ ->
            //     let app = currentVersionPgWithKafkaApp
            //     app._reset()
            //     let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage

            //     let ping = app._pingTodo()                
            //     Expect.isOk ping "should be ok"

            //     let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClinet") |> Result.get
            //     let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

            //     let todo = mkTodo (Guid.NewGuid()) "testmink" [] []
            //     let added = app.addTodo todo
            //     Expect.isOk added "should be ok"

            //     kafkaViewer.RefreshLoop()

            //     let (_, kafkaState, _, _) = (kafkaViewer.State ())
            //     let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
            //     let (_, sourceOfTruthStateView, _, _ ) = sourceOfTruthstateViewer() |> Result.get
            //     Expect.equal kafkaState sourceOfTruthStateView "should be equal"

            // testCase "when you have a ping, then the current state should provide information to be able to assign the partition at construction - Ok" <| fun _ ->
            //     let app = currentVersionPgWithKafkaApp
            //     app._reset()
            //     let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage

            //     let ping = app._pingTodo()                
            //     Expect.isOk ping "should be ok"

            //     let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClinet")  |> Result.get
            //     let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

            //     let todo = mkTodo (Guid.NewGuid()) "testmink" [] []
            //     let added = app.addTodo todo
            //     Expect.isOk added "should be ok"

            //     kafkaViewer.RefreshLoop()

            //     let (_, kafkaState, _, _) = (kafkaViewer.State ())
            //     let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
            //     let (_, sourceOfTruthStateView, _, _) = sourceOfTruthstateViewer() |> Result.get
            //     Expect.equal kafkaState sourceOfTruthStateView "should be equal"

            // testCase "add two todos  and verify by doing refresh twice that the state of kafkaviewer is aligned with single source of truth - Ok" <| fun _ ->
            //     let app = currentVersionPgWithKafkaApp
            //     app._reset()
            //     let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
            //     let todo = mkTodo (Guid.NewGuid()) "test" [] []
            //     let todo2 = mkTodo (Guid.NewGuid()) "test2" [] []
            //     let added = app.add2Todos (todo, todo2)
            //     Expect.isOk added "should be ok"
            //     let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
            //     let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

            //     let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
            //     let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

            //     kafkaViewer.Refresh()
            //     kafkaViewer.Refresh()

            //     let (_, kafkaState, _, _) = (kafkaViewer.State ())
            //     let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
            //     let (_, sourceOfTruthStateView, _, _) = sourceOfTruthstateViewer() |> Result.get
            //     Expect.equal kafkaState sourceOfTruthStateView "should be equal"

            // testCase "add two todos  and verify by doing refresh twice that the state of kafkaviewer is aligned with single source of truth. Third refresh will be error  - Ok" <| fun _ ->
            //     let app = currentVersionPgWithKafkaApp
            //     app._reset()

            //     let todo = mkTodo (Guid.NewGuid()) "test" [] []
            //     let todo2 = mkTodo (Guid.NewGuid()) "test2" [] []
            //     let added = app.add2Todos (todo, todo2)
            //     Expect.isOk added "should be ok"

            //     let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
            //     let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
            //     let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

            //     let firstRefresh = kafkaViewer.RefreshLoop()

            //     let (_, kafkaState, _, _) = (kafkaViewer.State ())
            //     let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
            //     let (_, sourceOfTruthStateView, _, _) = sourceOfTruthstateViewer() |> Result.get
            //     Expect.equal kafkaState sourceOfTruthStateView "should be equal"

            // testCase "should be able to do refresh unitl error and get the kafka state aligned  - Ok" <| fun _ ->
            //     let app = currentVersionPgWithKafkaApp
            //     app._reset()
            //     let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage

            //     let todo = mkTodo (Guid.NewGuid()) "test" [] []
            //     let todo2 = mkTodo (Guid.NewGuid()) "test2" [] []
            //     let added = app.add2Todos (todo, todo2)
            //     Expect.isOk added "should be ok"
            //     let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
            //     let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

            //     let added' = added.OkValue
            //     let (_, deliveryResults) = added'

            //     let mutable refreshed = kafkaViewer.Refresh() |> resultToBool
            //     while refreshed do
            //         refreshed <- kafkaViewer.Refresh() |> resultToBool

            //     let (_, kafkaState, _, _) = kafkaViewer.State ()
            //     let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
            //     let (_, sourceOfTruthStateView, _, _) = sourceOfTruthstateViewer() |> Result.get
            //     Expect.equal kafkaState sourceOfTruthStateView "should be equal"

            // testCase "should be able to do refresh until error and get the kafka state aligned. Use refresh loop on the class  - Ok" <| fun _ ->
            //     let app = currentVersionPgWithKafkaApp
            //     app._reset()
            //     let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
            //     let todo = mkTodo (Guid.NewGuid()) "test" [] []
            //     let todo2 = mkTodo (Guid.NewGuid()) "test2" [] []
            //     let added = app.add2Todos (todo, todo2)
            //     Expect.isOk added "should be ok"
            //     let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
            //     let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())
            //     let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
            //     let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
            //     let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

            //     let refreshed = kafkaViewer.RefreshLoop()

            //     let (_, kafkaState, _, _) = kafkaViewer.State ()
            //     let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
            //     let (_, sourceOfTruthStateView, _, _) = sourceOfTruthstateViewer() |> Result.get
            //     Expect.equal kafkaState sourceOfTruthStateView "should be equal"

            // testCase "should be able to do refresh until error and get the kafka state aligned. Use ping and then some method and then refresh loop on the class  - Ok" <| fun _ ->
            //     let app = currentVersionPgWithKafkaApp
            //     app._reset()
            //     let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage

            //     let pinged = app._pingTodo()
            //     Expect.isOk pinged "should be ok"
            //     let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
            //     let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

            //     let todo = mkTodo (Guid.NewGuid()) "test" [] []
            //     let todo2 = mkTodo (Guid.NewGuid()) "test2" [] []

            //     let added = app.add2Todos (todo, todo2)
            //     let added' = added.OkValue

            //     let refreshed = kafkaViewer.RefreshLoop()
            //     let (_, kafkaState, _, _) = kafkaViewer.State ()
            //     let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
            //     let (_, sourceOfTruthStateView, _, _) = sourceOfTruthstateViewer() |> Result.get
            //     Expect.equal kafkaState sourceOfTruthStateView "should be equal"

            // testCase "should be able to do refresh until error and get the kafka state aligned. Use refresh loop on the class, avoid explicit assign  - Ok" <| fun _ ->
            //     let app = currentVersionPgWithKafkaApp
            //     app._reset()
            //     let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage

            //     let todo = mkTodo (Guid.NewGuid()) "test" [] []
            //     let todo2 = mkTodo (Guid.NewGuid()) "test2" [] []
            //     let added = app.add2Todos (todo, todo2)

            //     Expect.isOk added "should be ok"
            //     let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
            //     let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

            //     let refreshed = kafkaViewer.RefreshLoop()

            //     let (_, kafkaState, _, _) = kafkaViewer.State ()
            //     let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
            //     let (_, sourceOfTruthStateView, _, _) = sourceOfTruthstateViewer() |> Result.get
            //     Expect.equal kafkaState sourceOfTruthStateView "should be equal"

            // testCase "add two todos  and verify by doing refresh tree times. Third time the refresh is error as there are not more events/messages - Ok" <| fun _ ->
            //     let app = currentVersionPgWithKafkaApp
            //     app._reset()
            //     let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage

            //     let pinged = app._pingTodo()
            //     Expect.isOk pinged "should be ok"

            //     let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
            //     let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

            //     let todo = mkTodo (Guid.NewGuid()) "test" [] []
            //     let todo2 = mkTodo (Guid.NewGuid()) "test2" [] []

            //     let added = app.add2Todos (todo, todo2)
            //     let added' = added.OkValue

            //     kafkaViewer.RefreshLoop()

            //     let (_, kafkaState, _, _) = kafkaViewer.State ()
            //     let sourceOfTruthstateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
            //     let (_, sourceOfTruthStateView, _, _) = sourceOfTruthstateViewer() |> Result.get
            //     Expect.equal kafkaState sourceOfTruthStateView "should be equal"
    
            // testCase "kafka state keeper will be able to evolve for a new events after refresh - Ok" <| fun _ ->
            //     let app = currentVersionPgWithKafkaApp
            //     app._reset()
            //     let pinged = app._pingTodo()
            //     Expect.isOk pinged "should be ok"
            //     let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
            //     let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
            //     let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())
            //     let todo = mkTodo (Guid.NewGuid()) "test" [] []
            //     let added = app.addTodo todo
            //     Expect.isOk added "should be ok"
                
            //     let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
            //     let (_, currentState, _, _) = storageStateViewer() |> Result.get
            //     let todos = currentState.todos.todos.GetAll()
            //     Expect.equal 1 todos.Length "should be equal"
                
            // testCase "kafka state keeper will be able to evolve for a new events after refresh. use listenwithRetries -  Ok" <| fun _ ->
            //     let app = currentVersionPgWithKafkaApp
            //     app._reset()
            //     let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage

            //     let pinged = app._pingTodo()
            //     Expect.isOk pinged "should be ok"
            //     let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
            //     let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

            //     let todo = mkTodo (Guid.NewGuid()) "test" [] []
            //     let added = app.addTodo todo
            //     Expect.isOk added "should be ok"

            //     let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
            //     let (_, currentState, _, _) = storageStateViewer() |> Result.get
            //     let todos = currentState.todos.todos.GetAll()
            //     Expect.equal 1 todos.Length "should be equal"
                
            //     kafkaViewer.RefreshLoop()

            //     let (_, kafkaState, _, _) = kafkaViewer.State ()
            //     Expect.equal kafkaState currentState "should be equal"
                
            // testCase "kafka state keeper will be able to refresh to the current state by processing events independently -  Ok" <| fun _ ->
            //     let app = currentVersionPgWithKafkaApp
            //     app._reset()
            //     let pinged = app._pingTodo()
            //     Expect.isOk pinged "should be ok"
            //     let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
            //     let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
            //     let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())
            //     let todo = mkTodo (Guid.NewGuid()) "test" [] []
            //     let added = app.addTodo todo
            //     let added' = added.OkValue
                
            //     let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
            //     let (_, currentState, _, _) = storageStateViewer() |> Result.get
            //     let todos = currentState.todos.todos.GetAll()
            //     Expect.equal 1 todos.Length "should be equal"
               
            //     kafkaViewer.RefreshLoop()

            //     let (_, kafkaState, _, _) = kafkaViewer.State ()
            //     Expect.equal kafkaState currentState "should be equal"
                
            // // FOCUS!!!
            // testCase "kafka state keeper will be able to refresh to the current state by processing events independently, add two todos (two events) -  Ok" <| fun _ ->
            //     let app = currentVersionPgWithKafkaApp
            //     app._reset()
            //     let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage

            //     let pinged = app._pingTodo()
            //     Expect.isOk pinged "should be ok"

            //     let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
            //     let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

            //     let todo = mkTodo (Guid.NewGuid()) "test" [] []
            //     let todo2 = mkTodo (Guid.NewGuid()) "test2" [] []
            //     let added = app.add2Todos (todo,todo2)
            //     Expect.isOk added "should be ok"
                
            //     let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
            //     let (_, currentState, _, _) = storageStateViewer() |> Result.get
            //     let todos = currentState.todos.todos.GetAll()
            //     Expect.equal 2 todos.Length "should be equal"

            //     kafkaViewer.RefreshLoop()

            //     let (_, kafkaState, _, _) = kafkaViewer.State ()
                
            //     Expect.equal kafkaState currentState "should be equal"
                
            // testCase "kafka state keeper see what's happen when refresh more than needed -  Ok" <| fun _ ->
            //     let app = currentVersionPgWithKafkaApp
            //     app._reset()
            //     let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
            //     let todo = mkTodo (Guid.NewGuid()) "test" [] []
            //     let pinged = app._pingTodo()
            //     Expect.isOk pinged "should be ok"
            //     let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
            //     let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

            //     let added = app.addTodo todo
            //     Expect.isOk added "should be ok"
                
            //     let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
            //     let (_, currentState, _, _) = storageStateViewer() |> Result.get
            //     let todos = currentState.todos.todos.GetAll()
            //     Expect.equal 1 todos.Length "should be equal"
               
            //     kafkaViewer.RefreshLoop()
            //     let (_, kafkaState, _, _) = kafkaViewer.State ()
            //     Expect.equal kafkaState currentState "should be equal"

            // testCase "add many todo and see the kafkaViewer state -  Ok" <| fun _ ->
            //     let app = currentVersionPgWithKafkaApp
            //     app._reset()
            //     let pinged = app._pingTodo()
            //     Expect.isOk pinged "should be ok"

            //     let storageStateViewer = CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage
            //     let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient")  |> Result.get
            //     let kafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageStateViewer (ApplicationInstance.Instance.GetGuid())

            //     let todo0 = mkTodo (Guid.NewGuid()) "test0" [] []
            //     let todo1 = mkTodo (Guid.NewGuid()) "test1" [] []
            //     let todo2 = mkTodo (Guid.NewGuid()) "test2" [] []
            //     let todo3 = mkTodo (Guid.NewGuid()) "test3" [] []
            //     let todo4 = mkTodo (Guid.NewGuid()) "test4" [] []
            //     let todo5 = mkTodo (Guid.NewGuid()) "test5" [] []
            //     let todo6 = mkTodo (Guid.NewGuid()) "test6" [] []
            //     let todo7 = mkTodo (Guid.NewGuid()) "test7" [] []
            //     let todo8 = mkTodo (Guid.NewGuid()) "test8" [] []
            //     let todo9 = mkTodo (Guid.NewGuid()) "test9" [] []

            //     let added0 = app.addTodo todo0
            //     let added1 = app.addTodo todo1
            //     let added2 = app.addTodo todo2
            //     let added3 = app.addTodo todo3
            //     let added4 = app.addTodo todo4
            //     let added5 = app.addTodo todo5
            //     let added6 = app.addTodo todo6
            //     let added7 = app.addTodo todo7
            //     let added8 = app.addTodo todo8
            //     let added9 = app.addTodo todo9

            //     kafkaViewer.RefreshLoop()

            //     let (_, currentState, _, _) = storageStateViewer() |> Result.get
            //     let (_, kafkaState, _, _) = kafkaViewer.State ()
            //     Expect.equal kafkaState currentState "should be equal"
            //      // eventBrokerStateBasedApp
                 
            // testCase "add a todo on eventBrokerStateBasedApp and check its state -  Ok" <| fun _ ->
            //     let app = eventBrokerStateBasedApp 
            //     app._reset()
            //     let pinged = app._pingTodo()
            //     Expect.isOk pinged "should be ok"

            //     let todo = mkTodo (Guid.NewGuid()) "testXXX" [] []
            //     let added = app.addTodo todo

            //     let todos = app.getAllTodos()
            //     Expect.isOk todos "should be ok"
            //     let todos' = todos.OkValue
            //     Expect.equal 1 todos'.Length "should be equal"
            //     Expect.equal todo todos'.[0] "should be equal"

            // testCase "add a todo and check that the eventbrokerbased state viewer is aligned -  Ok" <| fun _ ->

            //     // let app = currentVersionPgWithKafkaApp
            //     let app = eventBrokerStateBasedApp
            //     app._reset()
            //     let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClientX") |> Result.get
            //     let pinged = app._pingTodo()

            //     // let okPinged = pinged |> Result.get
            //     // let (_, dr)  = okPinged
            //     // let deliveryResult = dr |> List.head |> Option.get |> List.head
            //     // let position = deliveryResult.Offset
            //     // let partition = deliveryResult.Partition
            //     // todoSubscriber.Assign2(position, partition)

            //     let kafkaTodoViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber (CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage) (ApplicationInstance.Instance.GetGuid())

            //     let kafkaTodoStateViewer = fun () -> kafkaTodoViewer.State () 

            //     Expect.isOk pinged "should be ok"

            //     let todo = mkTodo (Guid.NewGuid()) "testXXX" [] []
            //     let added = app.addTodo todo
            //     kafkaTodoViewer.RefreshLoop()
                

            //     let todos = app.getAllTodos()
            //     Expect.isOk todos "should be ok"
            //     let todos' = todos.OkValue
            //     Expect.equal 1 todos'.Length "should be equal"
            //     Expect.equal todo todos'.[0] "should be equal"
            //     printf "kafkastate: %A \n" (kafkaTodoViewer.State)

            //     let (_, kafkaState, _, _) = kafkaTodoStateViewer()
            //     Expect.equal (kafkaState.todos.todos.GetAll()) [todo] "should be equal"

            // testCase "add two todos - Ok" <| fun _ ->
            //     let app = eventBrokerStateBasedApp
            //     app._reset()
            //     let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClientX") |> Result.get
            //     let pinged = app._pingTodo()

            //     let kafkaTodoViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber (CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage) (ApplicationInstance.Instance.GetGuid())

            //     let kafkaTodoStateViewer = fun () -> kafkaTodoViewer.State () 

            //     Expect.isOk pinged "should be ok"

            //     let todo = mkTodo (Guid.NewGuid()) "testXXX" [] []
            //     let todo2 = mkTodo (Guid.NewGuid()) "testQ" [] []
            //     let added = app.add2Todos (todo, todo2)
            //     kafkaTodoViewer.RefreshLoop()

            //     let todos = app.getAllTodos() |> Result.get |> Set.ofList
            //     Expect.equal todos (Set.ofList [todo; todo2]) "should be equal"

            //     let kafkaState = kafkaTodoStateViewer() |> fun (a, b, c, d) -> b
            //     Expect.equal (kafkaState.todos.todos.GetAll()) [todo; todo2] "should be equal"

            // testCase "add a todo to eventBrokerBasedApp - Ok" <| fun _ ->
            //     resetDb pgStorage
            //     resetAppId()
            //     let eventBrokerBasedApp = EventBrokerBasedApp.EventBrokerBasedApp(pgStorage, localHostbroker)
            //     let todoAdded = eventBrokerBasedApp.AddTodo (mkTodo (Guid.NewGuid()) "testXXXXX" [] [])
            //     Expect.isOk todoAdded "should be ok"
            //     eventBrokerBasedApp.refresh()
            //     let todosRetrieved = eventBrokerBasedApp.GetAllTodos()
            //     Expect.isOk todosRetrieved "should be ok"
            //     let todos = todosRetrieved.OkValue
            //     Expect.equal todos.Length 1 "should be equal"

            // testCase "add the same todo twice - Error" <| fun _ ->
            //     resetDb pgStorage
            //     resetAppId()
            //     let eventBrokerBasedApp = EventBrokerBasedApp.EventBrokerBasedApp(pgStorage, localHostbroker)
            //     let todo = mkTodo (Guid.NewGuid()) "testXXXXX" [] []
            //     let todoAdded = eventBrokerBasedApp.AddTodo todo
            //     Expect.isOk todoAdded "should be ok"
            //     eventBrokerBasedApp.refresh()
            //     let addedAgain = eventBrokerBasedApp.AddTodo todo
            //     Expect.isError addedAgain "should be error"

            // testCase "add a category to eventBrokerBasedApp - Ok" <| fun _ ->
            //     resetDb pgStorage
            //     resetAppId()
            //     let eventBrokerBasedApp = EventBrokerBasedApp.EventBrokerBasedApp(pgStorage, localHostbroker)
            //     let category = mkCategory (Guid.NewGuid()) "testXXXXX"
            //     let categoryAdded = eventBrokerBasedApp.AddCategory category
            //     Expect.isOk categoryAdded "should be ok"
            //     eventBrokerBasedApp.refresh()
            //     let categoriesRetrieved = eventBrokerBasedApp.GetAllCategories()
            //     Expect.isOk categoriesRetrieved "should be ok"
            //     let categories = categoriesRetrieved.OkValue
            //     Expect.equal categories.Length 1 "should be equal"
            //     Expect.equal categories.[0] category "should be equal"

            // testCase "add two todos, one has an unexisting category - Error" <| fun _ ->
            //     resetDb pgStorage
            //     resetAppId()
            //     let eventBrokerBasedApp = EventBrokerBasedApp.EventBrokerBasedApp(pgStorage, localHostbroker)
            //     let category = mkCategory (Guid.NewGuid()) "testXXXXX"
            //     let categoryAdded = eventBrokerBasedApp.AddCategory category
            //     Expect.isOk categoryAdded "should be ok"
            //     eventBrokerBasedApp.refresh()
            //     let todo = mkTodo (Guid.NewGuid()) "testXXXXX" [category.Id] []
            //     let todo2 = mkTodo (Guid.NewGuid()) "testXXXXX" [Guid.NewGuid()] []
            //     let todosAdded = eventBrokerBasedApp.Add2Todos (todo, todo2)
            //     Expect.isError todosAdded "should be error"

            // testCase "add a todo with an unexisting tag - Error" <| fun _ ->
            //     resetDb pgStorage
            //     resetAppId()
            //     let eventBrokerBasedApp = EventBrokerBasedApp.EventBrokerBasedApp(pgStorage, localHostbroker)
            //     let id1 = Guid.NewGuid()
            //     let todo = mkTodo (Guid.NewGuid()) "testXXXXX" [] [id1]
            //     let result = eventBrokerBasedApp.AddTodo todo
            //     Expect.isError result "should be error"

            // testCase "when remove a tag then all the reference to that tag are also removed from any todos - Ok"  <| fun _ ->
            //     resetDb pgStorage
            //     resetAppId()
            //     let eventBrokerBasedApp = EventBrokerBasedApp.EventBrokerBasedApp(pgStorage, localHostbroker)
            //     let tgId = Guid.NewGuid()
            //     let tag = mkTag tgId "testXXXXX" Color.Red
            //     let tagAdded = eventBrokerBasedApp.AddTag tag
            //     Expect.isOk tagAdded "should be ok"
            //     eventBrokerBasedApp.refresh()
            //     let todo = mkTodo (Guid.NewGuid()) "testXXXXX" [] [tgId]
            //     let todoAdded = eventBrokerBasedApp.AddTodo todo
            //     Expect.isOk todoAdded "should be ok"

            //     // eventBrokerBasedApp.refresh()
            //     let tags = eventBrokerBasedApp.GetAllTags() |> Result.get
            //     Expect.equal tags.Length 1 "should be equal"



            //     eventBrokerBasedApp.refresh()
            //     let todos = eventBrokerBasedApp.GetAllTodos() |> Result.get
            //     Expect.equal todos.Length 1 "should be equal"

            //     let removedTag = eventBrokerBasedApp.RemoveTag tgId
            //     Expect.isOk removedTag "should be ok"
            //     // eventBrokerBasedApp.refresh()
            //     // let todos = eventBrokerBasedApp.GetAllTodos() |> Result.get
            //     // let todo = todos.[0]

            //     // Expect.isTrue true "true"
            //     // Expect.equal todo.TagIds [] "should be equal"


        ]
        |> testSequenced