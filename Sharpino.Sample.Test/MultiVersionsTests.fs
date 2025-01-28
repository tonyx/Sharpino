module Tests.Sharpino.Sample.MultiVersionsTests

open Expecto
open System
open FSharp.Core

open Sharpino
open Sharpino.Cache
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
open System.Threading
open FsToolkit.ErrorHandling
open Sharpino.Storage
open log4net

let allVersions =
    [

        // see dbmate scripts for postgres setup. (create also user with name safe and password safe for dev only)
        // enable if you had setup postgres (see dbmate scripts):
        
        // disable only if you have your postgres configured

        (currentPostgresApp,        currentPostgresApp,     ((fun () -> () |> Result.Ok): unit -> Result<unit, string>), (pgStorage :> IEventStore<string>))

        // todo: finish porting to new version

        // (upgradedPostgresApp,       upgradedPostgresApp,    ((fun () -> () |> Result.Ok): unit -> Result<unit, string>), (pgStorage :> IEventStore<string>))

        // (currentPostgresApp,        upgradedPostgresApp,    currentPostgresApp._migrator.Value, pgStorage)
        
        
        (currentMemoryApp,          currentMemoryApp,       ((fun () -> () |> Result.Ok): unit -> Result<unit, string>) , (memoryStorage :> IEventStore<string>))
        // (upgradedMemoryApp,         upgradedMemoryApp,      ((fun () -> () |> Result.Ok): unit -> Result<unit, string>) , (memoryStorage :> IEventStore<string>))
        // (currentMemoryApp,          upgradedMemoryApp,      currentMemoryApp._migrator.Value, (memoryStorage :> IEventStore<string>))

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

// used those ones while I wanted to make the evolve able to skip errors
// after the pickler was introducedrp
// regression caused by pickler, I guess.

[<Tests>]
let testCoreEvolve =
    // quick and dirty way to log for debug:
    // log4net.Config.BasicConfigurator.Configure() |> ignore
    // let serializer = JsonSerializer(serSettings) :> ISerializer

    testList "evolve test" [
        multipleTestCase "generate the events directly without using the command handler - Ok " currentTestConfs <| fun (ap, _, _, eventStore) ->
            let _ = ap._reset()
            let id = Guid.NewGuid()
            let event = Todos.TodoEvents.TodoAdded { Id = id; Description = "test"; CategoryIds = []; TagIds = [] }

            let lastEventId = eventStore.TryGetLastEventId TodosContext.Version TodosContext.StorageName |> Option.defaultValue 0

            // I am adding the same event twice and the "evolve" will ignore it
            let _ = ap._addEvents (lastEventId, TodosContext.Version, [ event.Serialize ], TodosContext.StorageName, Guid.NewGuid()  )
            let _ = ap._addEvents (lastEventId, TodosContext.Version, [ event.Serialize ], TodosContext.StorageName, Guid.NewGuid() )
            
            let _ = ap._addEvents (lastEventId, TodosContextUpgraded.Version, [ event.Serialize ], TodosContextUpgraded.StorageName, Guid.NewGuid())

            let todos = ap.getAllTodos()

            Expect.isOk todos "should be ok"
            Expect.equal (todos.OkValue) [{ Id = id; Description = "test"; CategoryIds = []; TagIds = [] }] "should be equal"

        // this will be deprecated as events can't be inconsistent in the event store: the must succeed if they are there!
        multipleTestCase "in case events are unconsistent in the storage, then the evolve will be able to skip the unconsistent events - Ok" currentTestConfs <| fun (ap, _, _, eventStore) ->
            let _ = ap._reset()
            let id = Guid.NewGuid()
            let lastEventId = eventStore.TryGetLastEventId TodosContext.Version TodosContext.StorageName |> Option.defaultValue 0
            let event = TodoEvents.TodoAdded { Id = id; Description = "test"; CategoryIds = []; TagIds = [] }
            let _ = ap._addEvents (lastEventId, TodosContext.Version, [ event.Serialize ], TodosContext.StorageName, Guid.NewGuid())
            let _ = ap._addEvents (lastEventId,TodosContext.Version, [ event.Serialize ], TodosContext.StorageName, Guid.NewGuid())

            let lastEventId' = eventStore.TryGetLastEventId TodosContextUpgraded.Version TodosContextUpgraded.StorageName |> Option.defaultValue 0
            let _ = ap._addEvents (lastEventId', TodosContextUpgraded.Version, [ event.Serialize ], TodosContextUpgraded.StorageName, Guid.NewGuid())
            let _ = ap._addEvents (lastEventId', TodosContextUpgraded.Version, [ event.Serialize ], TodosContextUpgraded.StorageName, Guid.NewGuid())

            let todos = ap.getAllTodos()
            Expect.isOk todos "should be ok"
            Expect.equal (todos.OkValue) [{ Id = id; Description = "test"; CategoryIds = []; TagIds = [] }] "should be equal"

        ]
        |> testSequenced

[<Tests>]
let multiVersionsTests =

    testList "App with coordinator test - Ok" [

        multipleTestCase "add the same todo twice - Ko" currentTestConfs <| fun (ap, _, _, _) ->
            let _ = ap._reset() 
            let initialTodos = ap.getAllTodos() |> Result.get
            Expect.equal initialTodos [] "should be equal"

            let todo = mkTodo (Guid.NewGuid()) "test" [] []
            let added = ap.addTodo todo

            let okAdded = added |> Result.get

            Expect.isOk added "should be ok"

            let result = ap.addTodo todo

            Expect.isError result "should be error"

        multipleTestCase "add a todo - Ok" currentTestConfs <| fun (ap, _, _, _) ->
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

        multipleTestCase "add two todos - Ok" currentTestConfs <| fun (ap, _, _,_) -> 
            let _ = ap._reset()
            let todo1 = mkTodo (Guid.NewGuid()) "zakakakak" [] []
            let todo2 = mkTodo  (Guid.NewGuid()) "quququququX" [] []
            let result = ap.add2Todos (todo1, todo2)
            Expect.isOk result "should be ok"
            let okResult = result.OkValue
            let todos = ap.getAllTodos()
            Expect.isOk todos "should be ok"
            Expect.equal (todos.OkValue |> List.length) 2 "should be equal"

        multipleTestCase "add two todos, one has an unexisting category - Ko" currentTestConfs <| fun (ap, upgd, shdTstUpgrd, _) -> // this is for checking the case of a command returning two events
            let _ = ap._reset()
            let todo1 = mkTodo (Guid.NewGuid()) "test" [Guid.NewGuid()] []
            let todo2 = mkTodo (Guid.NewGuid()) "test2" [] []
            let added = ap.add2Todos (todo1, todo2)
            Expect.isError added "should be error"
            let todos = ap.getAllTodos().OkValue 
            Expect.equal todos [] "should be equal"

        multipleTestCase "add two todos, one has an unexisting tag - Ko" currentTestConfs <| fun (ap, upgd, shdTstUpgrd, _) -> // this is for checking the case of a command returning two events
            let _ = ap._reset()
            let todo1 = mkTodo (Guid.NewGuid()) "test" [] []
            let todo2 = mkTodo (Guid.NewGuid()) "test2" [] [Guid.NewGuid()]
            let added = ap.add2Todos (todo1, todo2)
            Expect.isError added "should be error"
            let result = ap.getAllTodos().OkValue 
            Expect.equal result [] "should be equal"

        multipleTestCase "add a todo with an unexisting tag - Ok" currentTestConfs  <| fun (ap, _, _, _)  ->
            let _ = ap._reset()
            let id1 = Guid.NewGuid()
            let id2 = Guid.NewGuid()
            let todo = mkTodo id1 "test" [] [id2]
            let result = ap.addTodo todo
            Expect.isError result "should be error"

        multipleTestCase "when remove a tag then all the reference to that tag are also removed from any todos - Ok" currentTestConfs  <| fun (ap, apUpgd, migrator, _) ->
            let _ = ap._reset()
            let id1 = Guid.NewGuid()
            let id2 = Guid.NewGuid()
            let tag = mkTag id2 "test" Color.Blue
            let result = ap.addTag tag
            Expect.isOk result "should be ok"
            let okResult = result.OkValue
            let todo = mkTodo id1 "test" [] [id2]
            let result = ap.addTodo todo
            let okResult = result.OkValue

            Expect.isOk result "should be ok"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let todos = apUpgd.getAllTodos().OkValue
            Expect.equal todos [todo] "should be equal"

            let removed = apUpgd.removeTag id2
            Expect.isOk removed "should be ok"
            let result = apUpgd.getAllTodos().OkValue
            Expect.isTrue (result.Head.TagIds |> List.isEmpty) "should be true"

        multipleTestCase "add and remove a todo 1 - Ok" currentTestConfs <| fun (app, upgdAp, migrator, _)  ->
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

        multipleTestCase "add and remove a todo 2 - Ok" currentTestConfs <| fun (ap, apUpgd, migrator, _)  ->
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

        multipleTestCase "remove an unexisting todo - Ko" currentTestConfs <| fun (ap, _, _, _) ->
            let _ = ap._reset()
            let newGuid = Guid.NewGuid()
            let removed = ap.removeTodo newGuid
            Expect.isError removed "should be error"
            let result = removed |> getError
            Expect.equal result (sprintf "A todo with id '%A' does not exist" newGuid) "should be equal"

        multipleTestCase "add category" currentTestConfs <| fun (ap, apUpgd, migrator, _) ->
            let _ = ap._reset()
            let category = mkCategory (Guid.NewGuid()) "testXX"
            let added = ap.addCategory category
            Expect.isOk added "should be ok"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let result = apUpgd.getAllCategories() |> Result.get
            Expect.equal result [category] "should be equal"

        multipleTestCase "add and remove a category 1" currentTestConfs <| fun (ap, apUpgd, migrator, _)  ->
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

        multipleTestCase "add and remove a category 2" currentTestConfs <| fun (ap, apUpgd, migrator, _)  ->
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

        multipleTestCase "add and remove a category 3" currentTestConfs <| fun (ap, apUpgd, migrator, _)   ->
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

        multipleTestCase "add a todo with an unexisting category - KO" currentTestConfs <| fun (ap, apUpgd, migrator, _) ->
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

        multipleTestCase "when remove a category all references to it should be removed from todos - Ok" currentTestConfs <| fun (ap, apUpgd, migrator, _) ->
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

        multipleTestCase "when remove a category all references to it should be removed from todos 2 - Ok" currentTestConfs <| fun (ap, apUpgd, migrator, _) ->
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

        multipleTestCase "when remove a category all references to it should be removed from todos 3 - Ok" currentTestConfs <| fun (ap, apUpgd, migrator, _) ->
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

        multipleTestCase "add tag" currentTestConfs <| fun (ap, apUpgd, migrator, _) ->
            let _ = ap._reset()
            let tag = mkTag (Guid.NewGuid()) "test" Color.Blue
            let added = ap.addTag tag
            let migrated = migrator()
            Expect.isOk migrated "should be ok"
            Expect.isOk added "should be ok"
            let result = apUpgd.getAllTags() |> Result.get
            Expect.equal result [tag] "should be equal"

        multipleTestCase "add and remove a tag" currentTestConfs <| fun (ap, apUpgd, migrator, _) ->
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

        multipleTestCase "when remove a tag all references to it should be removed from existing todos - Ok" currentTestConfs <| fun (ap, apUpgd, migrator, _) ->
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

        multipleTestCase "when remove a tag all references to it should be removed from existing todos 2 - Ok" currentTestConfs <| fun (ap, apUpgd, migrator, _) ->
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

        multipleTestCase "when remove a tag all references to it should be removed from existing todos 3 - Ok" currentTestConfs <| fun (ap, upgd, shdTstUpgrd, _) ->
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



        // focus
        // move somewhere else as the generalization of test parameterized won't work here
        // multipleTestCase "add two todos and then retrieve the report/projection - Ok" currentTestConfs <| fun (ap, upgd, migrator, _, todoAdded) ->
        //     let _ = ap._reset()
        //     let now = System.DateTime.UtcNow
        //     printf "now! %A" now
        //     let todo1 = mkTodo (Guid.NewGuid()) "test" [] []
        //     let todo2 = mkTodo (Guid.NewGuid()) "test2" [] []
        //     let added1 = ap.addTodo todo1
        //     let added2 = ap.addTodo todo2
        //     let result = ap.todoReport now System.DateTime.UtcNow
        //     let actualEvents = result.OkValue.TodoEvents |> Set.ofList
        //     let expcted = 
        //         [
        //             todoAdded todo1
        //             todoAdded todo2
        //             // TodoEvent.TodoAdded todo1
        //             // TodoEvent.TodoAdded todo2
        //         ]
        //         |> Set.ofList
        //     Expect.equal actualEvents expcted "should be equal"

        // multipleTestCase "add two todos and retrieve a patial report projection using a timeframe including only one event - Ok " currentTestConfs <| fun (ap, upgd, migrator, _, todoAdded ) ->
        //     let _ = ap._reset()
        //     let todo1 = mkTodo (Guid.NewGuid()) "test one" [] []
        //     let added1 = ap.addTodo todo1
        //     let timeBeforeAddingSecondTodo = System.DateTime.UtcNow // - System.TimeSpan.FromHours(1.0)
        //     let todo2 = mkTodo (Guid.NewGuid()) "test two" [] []
        //     let added2 = ap.addTodo todo2
        //     let result = ap.todoReport timeBeforeAddingSecondTodo System.DateTime.UtcNow // + System.TimeSpan.FromHours(0.2))
        //     let actualEvents = result.OkValue.TodoEvents |> Set.ofList
        //     let expcted = 
        //         [
        //             todoAdded todo2
        //             // TodoEvent.TodoAdded todo2
        //         ]
        //         |> Set.ofList
        //     Expect.equal actualEvents  expcted "should be equal"

        // pmultipleTestCase "add two todos and retrieve a patial report projection using a timeframe including only the first event - Ok " currentTestConfs <| fun (ap, _, _, _, todoAdded) ->
        //     let _ = ap._reset()
        //     let todo1 = mkTodo (Guid.NewGuid()) "test one" [] []
        //     let beforeAddingFirst = System.DateTime.Now
        //     let added1 = ap.addTodo todo1
        //     let beforeAddingSecond = System.DateTime.Now
        //     let todo2 = mkTodo (Guid.NewGuid()) "test2" [] []
        //     let added2 = ap.addTodo todo2
        //     let result = ap.todoReport beforeAddingFirst beforeAddingSecond 
        //     let actualEvents = result.OkValue.TodoEvents |> Set.ofList
        //     let expcted = 
        //         [
        //             todoAdded todo2
        //             // TodoEvent.TodoAdded todo1 //(mkTodo todo1.Id todo1.Description todo1.CategoryIds todo1.TagIds)
        //         ]
        //         |> Set.ofList
        //     Expect.equal actualEvents expcted "should be equal"
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