
module Tests.Sharpino.Sample.KafkaStateKeeperTests

open Expecto
open System
open FSharp.Core

open Sharpino
open Sharpino.ApplicationInstance
open Sharpino.Sample.TodosContext
open Sharpino.Sample.Entities.Todos
open Sharpino.Sample.Shared.Entities
open Sharpino.EventSourcing.Sample.AppVersions
open Sharpino.Sample.Todos.TodoEvents
open Tests.Sharpino.Shared 

open Sharpino.KafkaReceiver
open Sharpino.KafkaBroker
open Sharpino.Sample.EventBrokerBasedApp

[<Tests>]
    let kafkaTests =
        let serializer = Utils.JsonSerializer(Utils.serSettings) :> Utils.ISerializer
        testList "Kafka consumer and subscribers test" [
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

                todoSubscriber.Assign(position, partition)

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

                // todoSubscriber.Assign(position, partition)

                // when
                let todosKafkaViewer =
                    mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber (CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage) (ApplicationInstance.Instance.GetGuid())
                let todosKafkaViewerState = todosKafkaViewer.State() |> Result.get |> fun (_, b, _, _) -> b

                // then
                Expect.equal (todosKafkaViewerState.Todos.Todos.GetAll()) [todo] "should be equal"

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
                let todosKafkaViewer =
                    mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber
                        (CommandHandler.getStorageFreshStateViewer<TodosContext, TodoEvent> pgStorage) (ApplicationInstance.Instance.GetGuid())
                let todosKafkaViewerState = todosKafkaViewer.State() |> Result.get |> fun (_, b, _, _) -> b

                // todoSubscriber.Assign(position, partition)

                // then
                Expect.equal (todosKafkaViewerState.Todos.Todos.GetAll()) [todo] "should be equal"

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

                // todoSubscriber.Assign(position, partition)

                let todo2 = mkTodo (Guid.NewGuid()) "testX2" [] []
                let todo2Added = app.addTodo todo2

                todosKafkaViewer.Refresh() |> ignore
                let todosKafkaViewerState = todosKafkaViewer.State() |> Result.get |> fun (_, b, _, _) -> b
                let actual = todosKafkaViewerState.Todos.Todos.GetAll() |> Set.ofList
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

                todoSubscriber.Assign(position, partition)

                let todo2 = mkTodo (Guid.NewGuid()) "testX2" [] []
                let todo2Added = app.addTodo todo2

                let todo3 = mkTodo (Guid.NewGuid()) "testX3" [] []
                let todo3Added = app.addTodo todo3

                todosKafkaViewer.Refresh() |> ignore
                let todosKafkaViewerState = todosKafkaViewer.State() |> Result.get |> fun (_, b, _, _) -> b
                let actual = todosKafkaViewerState.Todos.Todos.GetAll() |> Set.ofList
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

                todoSubscriber.Assign(position, partition)

                let todo2 = mkTodo (Guid.NewGuid()) "testX2" [] []
                let todo2Added = app.addTodo todo2

                let todo3 = mkTodo (Guid.NewGuid()) "testX3" [] []
                let todo3Added = app.addTodo todo3

                let todosKafkaViewerState = currentStateViewer() |> Result.get |> fun (_, b, _, _) -> b

                let actual = todosKafkaViewerState.Todos.Todos.GetAll() |> Set.ofList
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

                todoSubscriber.Assign(position, partition)

                let todo2 = mkTodo (Guid.NewGuid()) "testX2" [] []
                let todo2Added = app.addTodo todo2

                let todo3 = mkTodo (Guid.NewGuid()) "testX3" [] []
                let todo3Added = app.addTodo todo3

                let todosKafkaViewerState = currentStateViewer() |> Result.get |> fun (_, b, _, _) -> b

                let actual = todosKafkaViewerState.Todos.Todos.GetAll() |> Set.ofList
                let expected = [todo; todo2; todo3] |> Set.ofList
                Expect.equal actual expected "should be equal"

                let todo4 = mkTodo (Guid.NewGuid()) "testX4" [] []
                let todo4Added = app.addTodo todo4
                let todosKafkaViewerState = currentStateViewer() |> Result.get |> fun (_, b, _, _) -> b
                let actual = todosKafkaViewerState.Todos.Todos.GetAll() |> Set.ofList
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

                todoSubscriber.Assign(position, partition)

                let todo2 = mkTodo (Guid.NewGuid()) "testX2" [] []
                let todo2Added = app.addTodo todo2

                let todo3 = mkTodo (Guid.NewGuid()) "testX3" [] []
                let todo3Added = app.addTodo todo3

                let todosKafkaViewerState = currentStateViewer() |> Result.get |> fun (_, b, _, _) -> b

                let actual = todosKafkaViewerState.Todos.Todos.GetAll() |> Set.ofList
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

                let tags' = app.GetAllTags()
                let actualTags' = tags'.OkValue
                Expect.equal actualTags' [] "should be equal"

                let retrievedTodos = app.GetAllTodos()
                Expect.isOk retrievedTodos "should be ok"

                let actualTodos' = retrievedTodos.OkValue
                Expect.equal actualTodos'.Length 1 "should be equal"
                let firstTodo = actualTodos'.[0]
                Expect.equal firstTodo.TagIds [] "should be equal"
        ]
        |> testSequenced