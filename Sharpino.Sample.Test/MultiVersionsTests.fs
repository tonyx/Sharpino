module Tests.Sharpino.Sample.MultiVersionsTests

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
open System.Threading
open FsToolkit.ErrorHandling
open Sharpino.KafkaBroker
open Sharpino.Storage
open log4net

let allVersions =
    [

        // see dbmate scripts for postgres setup. (create also user with name safe and password safe for dev only)
        // enable if you had setup postgres (see dbmate scripts):
        
        // disable only if you have your postgres configured
        (currentPostgresApp,        currentPostgresApp,     fun () -> () |> Result.Ok)
        (upgradedPostgresApp,       upgradedPostgresApp,    fun () -> () |> Result.Ok)
        (currentPostgresApp,        upgradedPostgresApp,    currentPostgresApp._migrator.Value)
        
        (currentMemoryApp,          currentMemoryApp,       fun () -> () |> Result.Ok)
        (upgradedMemoryApp,         upgradedMemoryApp,      fun () -> () |> Result.Ok)
        (currentMemoryApp,          upgradedMemoryApp,      currentMemoryApp._migrator.Value)

        // enable if you have eventstore locally (tested only with docker version of eventstore)
        // I am sorry but I think I am going to ditch eventstoreDb in the future
        // (AppVersions.evSApp,                    AppVersions.evSApp,                 fun () -> () |> Result.Ok)

        // enable if you have kafka installed locally with proper topics created (see Sharpino.Kafka project and CreateTopics.sh)
        // note that the by testing kafka you may experience some laggings.

        // (currentVersionPgWithKafkaApp,        currentVersionPgWithKafkaApp,     fun () -> () |> Result.Ok)

        // for the next eventBrokerStateBasedApp just use the tests in the file KafkaStateKeeperTest.fs
        // so don't enable the next line
        // (eventBrokerStateBasedApp,        eventBrokerStateBasedApp,     fun () -> () |> Result.Ok)

    ]

let currentTestConfs = allVersions

let listenForEvent (appId: Guid, receiver: KafkaSubscriber) =
    result {
        let received = receiver.Consume()
        let! deserialized = received.Message.Value |> serializer.Deserialize<BrokerMessage> 
        let mutable deserialized' = deserialized
        let mutable found = deserialized'.ApplicationId = appId

        while (not found) do
            let received = receiver.Consume()
            let! deserialized = received.Message.Value |> serializer.Deserialize<BrokerMessage> 
            deserialized' <- deserialized
            found <- deserialized'.ApplicationId = appId
        return deserialized'.Event
    }
    
let listenForEventWithRetries (appId: Guid, receiver: KafkaSubscriber, timeout: int) =
    result {
        let received = receiver.Consume()
        let! deserialized = received.Message.Value |> serializer.Deserialize<BrokerMessage> 
        let mutable deserialized' = deserialized
        let mutable found = deserialized'.ApplicationId = appId

        let mutable counter = 0
        while (not found) && (counter < timeout) do
            let received = receiver.Consume()
            let! deserialized = received.Message.Value |> serializer.Deserialize<BrokerMessage> 
            deserialized' <- deserialized
            found <- deserialized'.ApplicationId = appId
            counter <- counter + 1
        return deserialized'.Event
    }
    
let listenForEventWithTimeout (appId: Guid, receiver: KafkaSubscriber, timeout: int) =
    result {
        let! received = receiver.consume(timeout) 
        let! deserialized = received.Message.Value |> serializer.Deserialize<BrokerMessage> 
        let mutable deserialized' = deserialized
        let mutable found = deserialized'.ApplicationId = appId
        if (not found) then
            return! Error "not found"
        return deserialized'.Event
    }    

let listenForSingleEvent (appId: Guid, receiver: KafkaSubscriber, deliveryResults: List<Option<List<Confluent.Kafka.DeliveryResult<string, string>>>>) =
    let deliveryResult = deliveryResults |> List.head |> Option.get |> List.head 
    let position = deliveryResult.Offset
    let partition = deliveryResult.Partition
    receiver.Assign (position.Value, partition.Value)
    listenForEvent (appId, receiver)

let listenForTwoEvents (appId: Guid, receiver: KafkaSubscriber, deliveryResults: List<Option<List<Confluent.Kafka.DeliveryResult<string, string>>>>) =
    let deliveryResult = deliveryResults |> List.head |> Option.get |> List.head 
    let position = deliveryResult.Offset
    let partition = deliveryResult.Partition
    receiver.Assign (position.Value, partition.Value)
    let first = listenForEvent (appId, receiver)
    let second = listenForEvent (appId, receiver)
    (first, second)

let listenForSingleEventWithRetries (appId: Guid, receiver: KafkaSubscriber, deliveryResults: List<Option<List<Confluent.Kafka.DeliveryResult<string, string>>>>, timeout: int) =
    let deliveryResult = deliveryResults |> List.head |> Option.get |> List.head 
    let position = deliveryResult.Offset
    let partition = deliveryResult.Partition
    receiver.Assign (position.Value, partition.Value)
    listenForEventWithRetries (appId, receiver, timeout)
    
let rec listenForSingleEventWithTimeout (appId: Guid, receiver: KafkaSubscriber, deliveryResults: List<Option<List<Confluent.Kafka.DeliveryResult<string, string>>>>, timeout: int) =
    let deliveryResult = deliveryResults |> List.head |> Option.get |> List.head 
    let position = deliveryResult.Offset
    let partition = deliveryResult.Partition
    receiver.Assign (position.Value, partition.Value)
    listenForEventWithTimeout (appId, receiver, timeout)

[<Tests>]
let utilsTests =
    testList "catch errors test" [
        testCase "catch errors - ok" <| fun _ ->
            let result = List.traverseResultM (fun x -> x |> Ok) [1]
            Expect.isOk result "should be ok"
            Expect.equal result.OkValue [1] "should be equal"

        testCase "catch errors - Ko" <| fun _ ->
            let result = List.traverseResultM (fun x -> x |> Error) [1]
            Expect.isError result "should be error"

        testCase "catch errors 2 - Ko" <| fun _ ->
            let result = List.traverseResultM (fun x -> if x = 2 then Error 2; else x |> Ok  ) [ 1; 2; 3 ]
            Expect.isError result "should be error"

        testCase "catch errors 3 - Ko" <| fun _ ->
            let result = List.traverseResultM (fun x -> if x = 2 then Error 2; else x |> Ok  ) [ 1; 3; 4 ]
            Expect.isOk result "should be ok"
            Expect.equal result.OkValue [1; 3; 4] "should be equal"

        testCase "catch errors 4 - Ko" <| fun _ ->
            let result = List.traverseResultM (fun x -> if x = 2 then Error 2; else x |> Ok  ) [ 1; 5; 3; 4; 9; 9; 3; 99 ]
            Expect.isOk result "should be ok"
            Expect.equal result.OkValue [1; 5; 3; 4; 9; 9; 3; 99] "should be equal"
    ]

[<Tests>]
let testCoreEvolve =
    // quick and dirty way to log for debug:
    // log4net.Config.BasicConfigurator.Configure() |> ignore
    let serializer = JsonSerializer(serSettings) :> ISerializer

    testList "evolve test" [
        multipleTestCase "generate the events directly without using the command handler - Ok " currentTestConfs <| fun (ap, _, _) ->
            let _ = ap._reset()
            let id = Guid.NewGuid()
            let event = Todos.TodoEvents.TodoAdded { Id = id; Description = "test"; CategoryIds = []; TagIds = [] }

            // I am adding the same event twice and the "evolve" will ignore it
            let _ = ap._addEvents (TodosContext.Version, [ event.Serialize serializer], TodosContext.StorageName, Guid.NewGuid()  )
            let _ = ap._addEvents (TodosContext.Version, [ event.Serialize serializer], TodosContext.StorageName, Guid.NewGuid() )
            let _ = ap._addEvents (TodosContextUpgraded.Version, [ event.Serialize serializer ], TodosContextUpgraded.StorageName, Guid.NewGuid())

            let todos = ap.getAllTodos()

            Expect.isOk todos "should be ok"
            Expect.equal (todos.OkValue) [{ Id = id; Description = "test"; CategoryIds = []; TagIds = [] }] "should be equal"

        multipleTestCase "in case events are unconsistent in the storage, then the evolve will be able to skip the unconsistent events - Ok" currentTestConfs <| fun (ap, _, _) ->
            let _ = ap._reset()
            let id = Guid.NewGuid()
            let event = TodoEvents.TodoAdded { Id = id; Description = "test"; CategoryIds = []; TagIds = [] }
            let _ = ap._addEvents (TodosContext.Version, [ event.Serialize  serializer], TodosContext.StorageName, Guid.NewGuid())
            let _ = ap._addEvents (TodosContext.Version, [ event.Serialize  serializer], TodosContext.StorageName, Guid.NewGuid())

            let _ = ap._addEvents (TodosContextUpgraded.Version, [ event.Serialize serializer ], TodosContextUpgraded.StorageName, Guid.NewGuid())
            let _ = ap._addEvents (TodosContextUpgraded.Version, [ event.Serialize serializer ], TodosContextUpgraded.StorageName, Guid.NewGuid())

            let todos = ap.getAllTodos()
            Expect.isOk todos "should be ok"
            Expect.equal (todos.OkValue) [{ Id = id; Description = "test"; CategoryIds = []; TagIds = [] }] "should be equal"

        multipleTestCase "in case events are unconsistent in the storage, then the evolve will be able to skip the unconsistent events second try - Ok" currentTestConfs <| fun (ap, _, _) ->
            let _ = ap._reset() 
            let id = Guid.NewGuid()
            let id2 = Guid.NewGuid()
            let event = TodoEvents.TodoAdded (mkTodo id "test" [] [])
            let event2 = TodoEvents.TodoAdded (mkTodo id2 "test second part" [] [])

            let _ = ap._addEvents (TodosContext.Version, [ event.Serialize serializer ], TodosContext.StorageName, Guid.NewGuid()) 
            let _ = ap._addEvents (TodosContext.Version, [ event.Serialize serializer ], TodosContext.StorageName, Guid.NewGuid()) 

            let _ = ap._addEvents (TodosContextUpgraded.Version, [ event.Serialize serializer ], TodosContextUpgraded.StorageName, Guid.NewGuid())
            let _ = ap._addEvents (TodosContextUpgraded.Version, [ event.Serialize serializer ], TodosContextUpgraded.StorageName, Guid.NewGuid())

            let _ = ap._addEvents (TodosContext.Version,  [ event2.Serialize serializer ], TodosContext.StorageName, Guid.NewGuid())
            let _ = ap._addEvents (TodosContextUpgraded.Version, [ event2.Serialize serializer ], TodosContextUpgraded.StorageName, Guid.NewGuid())

            let todos = ap.getAllTodos()

            Expect.isOk todos "should be ok"
            Expect.equal (todos.OkValue |> Set.ofList) 
                (
                    [
                        { Id = id; Description = "test"; CategoryIds = []; TagIds = [] }
                        { Id = id2; Description = "test second part"; CategoryIds = []; TagIds = [] }
                    ] 
                    |> Set.ofList
                ) "should be equal"
        ]
        |> testSequenced


[<Tests>]
let multiVersionsTests =

    let serializer = JsonSerializer(serSettings) :> ISerializer
    let todoReceiver = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "sharpinoTestClient1") 
    let categoriesReceiver = KafkaSubscriber.Create ("localhost:9092", CategoriesContext.CategoriesContext.Version, CategoriesContext.CategoriesContext.StorageName, "sharpinoTestClient1")
    let tagsReceiver = KafkaSubscriber.Create ("localhost:9092", TagsContext.TagsContext.Version, TagsContext.TagsContext.StorageName, "sharpinoTestClient1")

    testList "App with coordinator test - Ok" [
        multipleTestCase "if notifier is enabled then receivers must be all ok" currentTestConfs <| fun (ap, _, _) ->
            let _ = ap._reset()
            if ap._notify.IsSome then
                Expect.isOk todoReceiver "should be ok"
                Expect.isOk categoriesReceiver "should be ok"
                Expect.isOk tagsReceiver "should be ok"
            else
                Expect.isTrue true "should be true"

        multipleTestCase "add the same todo twice - Ko" currentTestConfs <| fun (ap, _, _) ->
            let _ = ap._reset() 
            let initialTodos = ap.getAllTodos() |> Result.get
            Expect.equal initialTodos [] "should be equal"

            let todo = mkTodo (Guid.NewGuid()) "test" [] []
            let added = ap.addTodo todo
            let okAdded = added |> Result.get
            Expect.isOk added "should be ok"
            let result = ap.addTodo todo

            Expect.isError result "should be error"

            if ap._notify.IsSome && (todoReceiver |> Result.isOk) then
                let received = listenForSingleEvent (ApplicationInstance.Instance.GetGuid(), todoReceiver.OkValue, (okAdded |> snd))
                Expect.isOk received "should be ok"
                let received' = received.OkValue |> serializer.Deserialize<TodoEvents.TodoEvent> |> Result.get
                let expected = TodoEvents.TodoAdded todo
                Expect.equal expected received' "should be equal"

        multipleTestCase "add a todo - Ok" currentTestConfs <| fun (ap, _, _) ->
            let _ = ap._reset()
            let initialTodos = ap.getAllTodos() |> Result.get
            Expect.equal initialTodos [] "should be equal"
            let todo = mkTodo (Guid.NewGuid()) "test" [] []
            let result = ap.addTodo todo
            Expect.isOk result "should be ok"
            let okResult = result.OkValue
            let todos = ap.getAllTodos()
            Expect.isOk todos "should be ok"
            Expect.equal (todos.OkValue) [todo] "should be equal"

            let deliveryResults = okResult |> snd
            if ap._notify.IsSome && (todoReceiver |> Result.isOk) then
                let received = listenForSingleEvent (ApplicationInstance.Instance.GetGuid(), todoReceiver.OkValue, deliveryResults)
                Expect.isOk received "should be ok"
                let receivedOk = received.OkValue |> serializer.Deserialize<TodoEvents.TodoEvent> |> Result.get
                let expected = TodoEvents.TodoAdded todo
                Expect.equal expected receivedOk "should be equal"

        multipleTestCase "add two todos - Ok" currentTestConfs <| fun (ap, _, _) -> 
            let _ = ap._reset()
            let todo1 = mkTodo (Guid.NewGuid()) "zakakakak" [] []
            let todo2 = mkTodo  (Guid.NewGuid()) "quququququX" [] []
            let result = ap.add2Todos (todo1, todo2)
            Expect.isOk result "should be ok"
            let okResult = result.OkValue
            let todos = ap.getAllTodos()
            Expect.isOk todos "should be ok"
            Expect.equal (todos.OkValue |> List.length) 2 "should be equal"
            if ap._notify.IsSome && (todoReceiver |> Result.isOk) then
                let received = listenForTwoEvents (ApplicationInstance.Instance.GetGuid(), todoReceiver.OkValue, (okResult |> snd))
                let received1 = received |> fst |> Result.get |> serializer.Deserialize<TodoEvents.TodoEvent> |> Result.get
                let received2 = received |> snd |> Result.get |> serializer.Deserialize<TodoEvents.TodoEvent> |> Result.get
                Expect.equal received1 (TodoEvents.TodoAdded todo1) "should be equal"

        multipleTestCase "add two todos, one has an unexisting category - Ko" currentTestConfs <| fun (ap, upgd, shdTstUpgrd) -> // this is for checking the case of a command returning two events
            let _ = ap._reset()
            let todo1 = mkTodo (Guid.NewGuid()) "test" [Guid.NewGuid()] []
            let todo2 = mkTodo (Guid.NewGuid()) "test2" [] []
            let added = ap.add2Todos (todo1, todo2)
            Expect.isError added "should be error"
            let todos = ap.getAllTodos().OkValue 
            Expect.equal todos [] "should be equal"

        multipleTestCase "add two todos, one has an unexisting tag - Ko" currentTestConfs <| fun (ap, upgd, shdTstUpgrd) -> // this is for checking the case of a command returning two events
            let _ = ap._reset()
            let todo1 = mkTodo (Guid.NewGuid()) "test" [] []
            let todo2 = mkTodo (Guid.NewGuid()) "test2" [] [Guid.NewGuid()]
            let added = ap.add2Todos (todo1, todo2)
            Expect.isError added "should be error"
            let result = ap.getAllTodos().OkValue 
            Expect.equal result [] "should be equal"

        multipleTestCase "add a todo with an unexisting tag - Ok" currentTestConfs  <| fun (ap, _, _) ->
            let _ = ap._reset()
            let id1 = Guid.NewGuid()
            let id2 = Guid.NewGuid()
            let todo = mkTodo id1 "test" [] [id2]
            let result = ap.addTodo todo
            Expect.isError result "should be error"

        multipleTestCase "when remove a tag then all the reference to that tag are also removed from any todos - Ok" currentTestConfs  <| fun (ap, apUpgd, migrator) ->
            let _ = ap._reset()
            let id1 = Guid.NewGuid()
            let id2 = Guid.NewGuid()
            let tag = mkTag id2 "test" Color.Blue
            let result = ap.addTag tag
            Expect.isOk result "should be ok"
            let okResult = result.OkValue
            if ap._notify.IsSome && (tagsReceiver |> Result.isOk) then
                let received = listenForSingleEvent (ApplicationInstance.ApplicationInstance.Instance.GetGuid(), tagsReceiver.OkValue, (okResult |> snd))
                Expect.isOk received "should be ok"
                let received' = received.OkValue |> serializer.Deserialize<TagsEvents.TagEvent> |> Result.get
                let expected = TagEvent.TagAdded tag
                Expect.equal expected received' "should be equal"

            let todo = mkTodo id1 "test" [] [id2]
            let result = ap.addTodo todo
            let okResult = result.OkValue
            if ap._notify.IsSome && (todoReceiver |> Result.isOk) then
                let received = listenForSingleEvent (ApplicationInstance.ApplicationInstance.Instance.GetGuid(), todoReceiver.OkValue, (okResult |> snd))
                Expect.isOk received "should be ok"
                let received' = received.OkValue |> serializer.Deserialize<TodoEvents.TodoEvent> |> Result.get
                let expected = TodoEvents.TodoAdded todo
                Expect.equal expected received' "should be equal"

            Expect.isOk result "should be ok"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let todos = apUpgd.getAllTodos().OkValue
            Expect.equal todos [todo] "should be equal"

            let removed = apUpgd.removeTag id2
            Expect.isOk removed "should be ok"
            let result = apUpgd.getAllTodos().OkValue
            Expect.isTrue (result.Head.TagIds |> List.isEmpty) "should be true"

        multipleTestCase "add and remove a todo 1 - Ok" currentTestConfs <| fun (app, upgdAp, migrator)  ->
            let _ = app._reset()

            let todo = mkTodo (Guid.NewGuid()) "test" [] []
            let result = app.addTodo todo
            Expect.isOk result "should be ok"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let todos = upgdAp.getAllTodos() |> Result.get
            Expect.equal todos [todo] "should be equal"
            let result = upgdAp.removeTodo todo.Id
            Expect.isOk result "should be ok"
            let todos = upgdAp.getAllTodos() |> Result.get
            Expect.equal todos [] "should be equal"

        multipleTestCase "add and remove a todo 2 - Ok" currentTestConfs <| fun (ap, apUpgd, migrator)  ->
            let _ = ap._reset()

            let todo = mkTodo (Guid.NewGuid()) "test" [] []
            let result = ap.addTodo todo
            Expect.isOk result "should be ok"

            let todos = ap.getAllTodos() |> Result.get
            Expect.equal todos [todo] "should be equal"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let removed = apUpgd.removeTodo todo.Id
            Expect.isOk removed "should be ok"
            let result = apUpgd.getAllTodos() |> Result.get
            Expect.equal result [] "should be equal"

        multipleTestCase "remove an unexisting todo - Ko" currentTestConfs <| fun (ap, _, _) ->
            let _ = ap._reset()
            let newGuid = Guid.NewGuid()
            let removed = ap.removeTodo newGuid
            Expect.isError removed "should be error"
            let result = removed |> getError
            Expect.equal result (sprintf "A todo with id '%A' does not exist" newGuid) "should be equal"

        multipleTestCase "add category" currentTestConfs <| fun (ap, apUpgd, migrator) ->
            let _ = ap._reset()
            let category = mkCategory (Guid.NewGuid()) "testXX"
            let added = ap.addCategory category
            Expect.isOk added "should be ok"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let result = apUpgd.getAllCategories() |> Result.get
            Expect.equal result [category] "should be equal"

        multipleTestCase "add and remove a category 1" currentTestConfs <| fun (ap, apUpgd, migrator)  ->
            let _ = ap._reset()
            let category = mkCategory (Guid.NewGuid()) "test"
            let added = ap.addCategory category
            
            Expect.isOk added "should be ok"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let categories = apUpgd.getAllCategories() |> Result.get
            Expect.equal categories [category] "should be equal"
            let removed = apUpgd.removeCategory category.Id
            Expect.isOk removed "should be ok"
            let result = apUpgd.getAllCategories() |> Result.get
            Expect.equal result [] "should be equal"

        multipleTestCase "add and remove a category 2" currentTestConfs <| fun (ap, apUpgd, migrator)  ->
            let _ = ap._reset()
            let category = mkCategory (Guid.NewGuid()) "testuu"
            let added = ap.addCategory category
            
            Expect.isOk added "should be ok"
            let categories = ap.getAllCategories() |> Result.get
            Expect.equal categories [category] "should be equal"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let removed = apUpgd.removeCategory category.Id

            Expect.isOk removed "should be ok"
            let result = apUpgd.getAllCategories() |> Result.get
            Expect.equal result [] "should be equal"

        multipleTestCase "add and remove a category 3" currentTestConfs <| fun (ap, apUpgd, migrator)  ->
            let _ = ap._reset()
            let category = mkCategory (Guid.NewGuid()) "testuu"
            let added = ap.addCategory category
            Expect.isOk added "should be ok"
            let categories = ap.getAllCategories() |> Result.get
            Expect.equal categories [category] "should be equal"
            let removed = ap.removeCategory category.Id
            Expect.isOk removed "should be ok"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let result = apUpgd.getAllCategories() |> Result.get
            Expect.equal result [] "should be equal"

        multipleTestCase "add a todo with an unexisting category - KO" currentTestConfs <| fun (ap, apUpgd, migrator) ->
            let _ = ap._reset()
            let category = mkCategory (Guid.NewGuid()) "test"
            let added = ap.addCategory category
            Expect.isOk added "should be ok"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let category' = apUpgd.getAllCategories() |> Result.get
            Expect.equal category' [category] "should be equal"

            let todo = mkTodo (Guid.NewGuid()) "test" [Guid.NewGuid()] []
            let result = apUpgd.addTodo todo
            Expect.isError result "should be error"

        multipleTestCase "when remove a category all references to it should be removed from todos - Ok" currentTestConfs <| fun (ap, apUpgd, migrator) ->
            let _ = ap._reset()
            let categoryId = Guid.NewGuid()
            let category = mkCategory categoryId "testX"
            let todo = mkTodo (Guid.NewGuid()) "test" [categoryId] []

            let catAdded = ap.addCategory category
            Expect.isOk catAdded "should be ok"
            
            let added = ap.addTodo todo    
            
            Expect.isOk added "should be ok"

            let hasMigrated = migrator()
            Expect.isOk hasMigrated "should be ok"

            let todos = apUpgd.getAllTodos().OkValue 
            Expect.equal todos [todo] "should be equal"
            let result = apUpgd.removeCategory categoryId

            Expect.isOk result "should be ok"

            let result = apUpgd.getAllTodos().OkValue 
            Expect.equal (result |> List.head).CategoryIds [] "should be equal"

        multipleTestCase "when remove a category all references to it should be removed from todos 2 - Ok" currentTestConfs <| fun (ap, apUpgd, migrator) ->
            let _ = ap._reset()
            let categoryId1 = Guid.NewGuid()
            let categoryId2 = Guid.NewGuid()
            let category = mkCategory categoryId1 "testX"
            let category2 = { Id = categoryId2; Name = "test2X" }
            let todo = mkTodo (Guid.NewGuid()) "test" [categoryId1; categoryId2] []
            let added1 = ap.addCategory category

            Expect.isOk added1 "should be ok"
            Expect.isTrue true "true"

            let added2 = ap.addCategory category2
            Expect.isOk added2 "should be ok"

            let app' = ap.addTodo todo
            Expect.isOk app' "should be ok"

            let todos = ap.getAllTodos().OkValue 
            Expect.equal todos [todo] "should be equal"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let result = apUpgd.removeCategory categoryId1
            Expect.isOk result "should be ok"

            let result = apUpgd.getAllTodos().OkValue 
            Expect.equal (result |> List.head).CategoryIds [categoryId2] "should be equal"


        multipleTestCase "when remove a category all references to it should be removed from todos 3 - Ok" currentTestConfs <| fun (ap, apUpgd, migrator) ->
            let _ = ap._reset()
            let categoryId1 = Guid.NewGuid()
            let categoryId2 = Guid.NewGuid()
            let category = mkCategory categoryId1 "test"
            let category2 = mkCategory categoryId2 "test2"
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = [categoryId1; categoryId2]; TagIds = [] }

            let added = ap.addCategory category
            Expect.isOk added "should be ok"
            let added' = ap.addCategory category2
            Expect.isOk added' "should be ok"
            let added''' = ap.addTodo todo
            Expect.isOk added''' "should be ok"

            let todos = ap.getAllTodos().OkValue 
            Expect.equal todos [todo] "should be equal"

            let removed = ap.removeCategory categoryId1
            Expect.isOk removed "should be ok"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let result = apUpgd.getAllTodos().OkValue 

            Expect.equal (result |> List.head).CategoryIds [categoryId2] "should be equal"

        multipleTestCase "add tag" currentTestConfs <| fun (ap, apUpgd, migrator) ->
            let _ = ap._reset()
            let tag = mkTag (Guid.NewGuid()) "test" Color.Blue
            let added = ap.addTag tag
            let migrated = migrator()
            Expect.isOk migrated "should be ok"
            Expect.isOk added "should be ok"
            let result = apUpgd.getAllTags() |> Result.get
            Expect.equal result [tag] "should be equal"

        multipleTestCase "add and remove a tag" currentTestConfs <| fun (ap, apUpgd, migrator) ->
            let _ = ap._reset()
            let tag = mkTag (Guid.NewGuid()) "test" Color.Blue
            let added = ap.addTag tag
            Expect.isOk added "should be ok"
            let tags = ap.getAllTags() |> Result.get
            Expect.equal tags [tag] "should be equal"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let removed = apUpgd.removeTag tag.Id
            Expect.isOk removed "should be ok"
            let result = apUpgd.getAllTags() |> Result.get
            Expect.equal result [] "should be equal"

        multipleTestCase "when remove a tag all references to it should be removed from existing todos - Ok" currentTestConfs <| fun (ap, apUpgd, migrator) ->
            let _ = ap._reset()
            let tagId = Guid.NewGuid()
            let tag = mkTag tagId "test" Color.Blue
            let todo = mkTodo (Guid.NewGuid()) "test" [] [tagId]

            let added =
                ResultCE.result {
                    let! _ = ap.addTag tag
                    let! app' = ap.addTodo todo
                    return app'
                } 
            Expect.isOk added "should be ok"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let todos = apUpgd.getAllTodos().OkValue 
            Expect.equal todos [todo] "should be equal"
            let result = apUpgd.removeTag tagId
            Expect.isOk result "should be ok"

            let result = apUpgd.getAllTodos().OkValue 
            Expect.equal (result |> List.head).TagIds [] "should be equal"

        multipleTestCase "when remove a tag all references to it should be removed from existing todos 2 - Ok" currentTestConfs <| fun (ap, apUpgd, migrator) ->
            let _ = ap._reset()
            let tagId = Guid.NewGuid()
            let tag = mkTag tagId "test" Color.Blue
            let todo = mkTodo (Guid.NewGuid()) "test" [] [tagId]

            let added =
                ResultCE.result {
                    let! _ = ap.addTag tag
                    let! app' = ap.addTodo todo
                    return app'
                } 
            Expect.isOk added "should be ok"

            let todos = ap.getAllTodos().OkValue 
            Expect.equal todos [todo] "should be equal"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let removed = apUpgd.removeTag tagId
            Expect.isOk removed "should be ok"

            let result = apUpgd.getAllTodos().OkValue 
            Expect.equal (result |> List.head).TagIds [] "should be equal"

        multipleTestCase "when remove a tag all references to it should be removed from existing todos 3 - Ok" currentTestConfs <| fun (ap, upgd, shdTstUpgrd) ->
            let _ = ap._reset()
            let tagId = Guid.NewGuid()
            let tag1 = mkTag tagId "test" Color.Blue
            let tagId2 = Guid.NewGuid()
            let tag2 = mkTag tagId2 "test2" Color.Red
            let todo = mkTodo (Guid.NewGuid()) "test" [] [tagId; tagId2]

            let added = ap.addTag tag1
            Expect.isOk added "should be ok"
            let added' = ap.addTag tag2
            Expect.isOk added' "should be ok"
            let added'' = ap.addTodo todo
            Expect.isOk added'' "should be ok"

            let todos = ap.getAllTodos().OkValue 
            Expect.equal todos [todo] "should be equal"
            let removed = ap.removeTag tagId
            Expect.isOk removed "should be ok"

            let result = ap.getAllTodos().OkValue 
            Expect.equal (result |> List.head).TagIds [tagId2] "should be equal"

        multipleTestCase "add two todos and then retrieve the report/projection - Ok" currentTestConfs <| fun (ap, upgd, migrator) ->
            let _ = ap._reset()
            let now = System.DateTime.Now
            let todo1 = mkTodo (Guid.NewGuid()) "test" [] []
            let todo2 = mkTodo (Guid.NewGuid()) "test2" [] []
            let added1 = ap.addTodo todo1
            let added2 = ap.addTodo todo2
            let result = ap.todoReport now System.DateTime.Now
            let actualEvents = result.OkValue.TodoEvents |> Set.ofList
            let expcted = 
                [
                    TodoEvent.TodoAdded todo1
                    TodoEvent.TodoAdded todo2
                ]
                |> Set.ofList
            Expect.equal actualEvents expcted "should be equal"

        multipleTestCase "add two todos and retrieve a patial report projection using a timeframe including only one event - Ok " currentTestConfs <| fun (ap, upgd, migrator) ->
            let _ = ap._reset()
            let todo1 = mkTodo (Guid.NewGuid()) "test one" [] []
            let added1 = ap.addTodo todo1
            let timeBeforeAddingSecondTodo = System.DateTime.Now
            let todo2 = mkTodo (Guid.NewGuid()) "test two" [] []
            let added2 = ap.addTodo todo2
            let result = ap.todoReport timeBeforeAddingSecondTodo System.DateTime.Now
            let actualEvents = result.OkValue.TodoEvents |> Set.ofList
            let expcted = 
                [
                    TodoEvent.TodoAdded todo2
                ]
                |> Set.ofList
            Expect.equal actualEvents  expcted "should be equal"

        multipleTestCase "add two todos and retrieve a patial report projection using a timeframe including only the first event - Ok " currentTestConfs <| fun (ap, _, _) ->
            let _ = ap._reset()
            let todo1 = mkTodo (Guid.NewGuid()) "test one" [] []
            let beforeAddingFirst = System.DateTime.Now
            let added1 = ap.addTodo todo1
            let beforeAddingSecond = System.DateTime.Now
            let todo2 = mkTodo (Guid.NewGuid()) "test2" [] []
            let added2 = ap.addTodo todo2
            let result = ap.todoReport beforeAddingFirst beforeAddingSecond 
            let actualEvents = result.OkValue.TodoEvents |> Set.ofList
            let expcted = 
                [
                    TodoEvent.TodoAdded todo1 //(mkTodo todo1.Id todo1.Description todo1.CategoryIds todo1.TagIds)
                ]
                |> Set.ofList
            Expect.equal actualEvents expcted "should be equal"
    ] 
    |> testSequenced

[<Tests>]
let multiCallTests =
    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    // enable for quick debugging
    // log4net.Config.BasicConfigurator.Configure() |> ignore

    testList "massive sequence adding - Ok" [
        ptestCase "add many todos" <| fun _ ->

            let ap = AppVersions.currentPostgresApp
            let _ = ap._reset()

            for i = 0 to 999 do
                let todo = mkTodo (Guid.NewGuid()) ("todo" + (i.ToString())) [] []
                let added = ap.addTodo todo 
                ()

            let actualTodos = ap.getAllTodos().OkValue
            Expect.equal actualTodos.Length 1000 "should be equal"

    ] |> testSequenced