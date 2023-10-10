
module Tests.Sharpino.Sample.MultiVersionsTests

open Expecto
open FsCheck
open FsCheck.Prop
open Expecto.Tests
open System
open FSharp.Core

open Sharpino
open Sharpino.Sample
open Sharpino.Sample.TodosAggregate
open Sharpino.Sample.Todos
open Sharpino.Sample.Entities.Categories
open Sharpino.Sample.Entities.Todos
open Sharpino.Sample.TagsAggregate
open Sharpino.Sample.Entities.Tags
open Sharpino.Sample.Categories
open Sharpino.Sample.Tags
open Sharpino.Utils
open Sharpino.EventSourcing.Sample
open Sharpino.EventSourcing
open Sharpino.Sample.CategoriesAggregate
open Sharpino.Sample.Categories.CategoriesEvents
open Sharpino.Sample.Todos.TodoEvents
open Sharpino.Sample.Tags.TagsEvents
open System.Runtime.CompilerServices
open Sharpino.Conf
open Tests.Sharpino.Shared

open Sharpino.TestUtils
open System.Threading
open FsToolkit.ErrorHandling
open Microsoft.FSharp.Quotations
open Sharpino.EventSourcing.Sample.AppVersions

let allVersions =
    [

        // see dbmate scripts for postgres setup. (create also user with name safe and password safe for dev only)
        // enable if you had setup postgres (see dbmate scripts):
        

        // (currentPostgresApp,        currentPostgresApp,     fun () -> () |> Result.Ok)
        // (upgradedPostgresApp,       upgradedPostgresApp,    fun () -> () |> Result.Ok)
        // (currentPostgresApp,        upgradedPostgresApp,    currentPostgresApp._migrator.Value)

        (currentMemoryApp,          currentMemoryApp,       fun () -> () |> Result.Ok)
        (upgradedMemoryApp,         upgradedMemoryApp,      fun () -> () |> Result.Ok)
        (currentMemoryApp,          upgradedMemoryApp,      currentMemoryApp._migrator.Value)

        // enable if you have eventstore locally (tested only with docker version of eventstore)
        // (AppVersions.evSApp,                    AppVersions.evSApp,                 fun () -> () |> Result.Ok)
    ]

let currentTestConfs = allVersions

[<Tests>]
let utilsTests =
    testList "catch errors test" [
        testCase "catch errors - ok" <| fun _ ->
            let result = catchErrors (fun x -> x |> Ok) [1]
            Expect.isOk result "should be ok"
            Expect.equal result.OkValue [1] "should be equal"

        testCase "catch errors - Ko" <| fun _ ->
            let result = catchErrors (fun x -> x |> Error) [1]
            Expect.isError result "should be error"

        testCase "catch errors 2 - Ko" <| fun _ ->
            let result = catchErrors (fun x -> if x = 2 then Error 2; else x |> Ok  ) [ 1; 2; 3 ]
            Expect.isError result "should be error"

        testCase "catch errors 3 - Ko" <| fun _ ->
            let result = catchErrors (fun x -> if x = 2 then Error 2; else x |> Ok  ) [ 1; 3; 4 ]
            Expect.isOk result "should be error"
            Expect.equal result.OkValue [1; 3; 4] "should be equal"

        testCase "catch errors 4 - Ko" <| fun _ ->
            let result = catchErrors (fun x -> if x = 2 then Error 2; else x |> Ok  ) [ 1; 5; 3; 4; 9; 9; 3; 99 ]
            Expect.isOk result "should be error"
            Expect.equal result.OkValue [1; 5; 3; 4; 9; 9; 3; 99] "should be equal"
    ]

[<Tests>]
let testCoreEvolve =
    // quick and dirty way to log for debug:
    // log4net.Config.BasicConfigurator.Configure() |> ignore
    let serializer = JsonSerializer(serSettings)
    let updateStateIfNecessary (ap: Sharpino.EventSourcing.Sample.AppVersions.IApplication) =
        match ap._forceStateUpdate with
        | Some f -> f()
        | None -> ()
    testList "evolve test" [
        multipleTestCase "generate the events directly without using the repository - Ok " currentTestConfs <| fun (ap, _, _) ->
            let _ = ap._reset()
            let id = Guid.NewGuid()
            let event = Todos.TodoEvents.TodoAdded { Id = id; Description = "test"; CategoryIds = []; TagIds = [] }

            // I am adding twice the same event and the "evolve" will ignore it
            let _ = ap._addEvents (TodosAggregate.Version, [ event |> serializer.Serialize], TodosAggregate.StorageName )
            let _ = ap._addEvents (TodosAggregate.Version, [ event |> serializer.Serialize], TodosAggregate.StorageName)
            let _ = ap._addEvents (TodosAggregate'.Version, [ event |> serializer.Serialize], TodosAggregate'.StorageName)
            ap |> updateStateIfNecessary

            let todos = ap.getAllTodos()

            Expect.isOk todos "should be ok"
            Expect.equal (todos.OkValue) [{ Id = id; Description = "test"; CategoryIds = []; TagIds = [] }] "should be equal"

        multipleTestCase "in case events are unconsistent in the storage, then the evolve will be able to skip the unconsistent events - Ok" currentTestConfs <| fun (ap, _, _) ->
            let _ = ap._reset()
            let id = Guid.NewGuid()
            let event = TodoEvents.TodoAdded { Id = id; Description = "test"; CategoryIds = []; TagIds = [] }
            let _ = ap._addEvents (TodosAggregate.Version, [ event |> serializer.Serialize], TodosAggregate.StorageName)
            let _ = ap._addEvents (TodosAggregate.Version, [ event |> serializer.Serialize], TodosAggregate.StorageName)

            let _ = ap._addEvents (TodosAggregate'.Version, [ event |> serializer.Serialize ], TodosAggregate'.StorageName)
            let _ = ap._addEvents (TodosAggregate'.Version, [ event |> serializer.Serialize], TodosAggregate'.StorageName)

            ap |> updateStateIfNecessary
            let todos = ap.getAllTodos()
            Expect.isOk todos "should be ok"
            Expect.equal (todos.OkValue) [{ Id = id; Description = "test"; CategoryIds = []; TagIds = [] }] "should be equal"

        multipleTestCase "in case events are unconsistent in the storage, then the evolve will be able to skip the unconsistent events second try - Ok" currentTestConfs <| fun (ap, _, _) ->
            let _ = ap._reset() 
            let id = Guid.NewGuid()
            let id2 = Guid.NewGuid()
            let event = TodoEvents.TodoAdded (mkTodo id "test" [] [])
            let event2 = TodoEvents.TodoAdded (mkTodo id2 "test second part" [] [])

            let _ = ap._addEvents (TodosAggregate.Version, [ event |> serializer.Serialize ],  TodosAggregate.StorageName) 
            let _ = ap._addEvents (TodosAggregate.Version, [ event |> serializer.Serialize ],  TodosAggregate.StorageName) 

            let _ = ap._addEvents (TodosAggregate'.Version, [ event |> serializer.Serialize ],  TodosAggregate'.StorageName)
            let _ = ap._addEvents (TodosAggregate'.Version, [ event |> serializer.Serialize ],  TodosAggregate'.StorageName)

            let _ = ap._addEvents (TodosAggregate.Version,  [ event2 |> serializer.Serialize ], TodosAggregate.StorageName)
            let _ = ap._addEvents (TodosAggregate'.Version, [ event2 |> serializer.Serialize ], TodosAggregate'.StorageName)

            ap |> updateStateIfNecessary
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
    testList "App with coordinator test - Ok" [
        // not needed anymore but I keep it for reference and future storages that may require this approach
        let updateStateIfNecessary (ap: Sharpino.EventSourcing.Sample.AppVersions.IApplication) =
            match ap._forceStateUpdate with
            | Some f -> f()
            | None -> ()

        multipleTestCase "add the same todo twice - Ko" currentTestConfs <| fun (ap, _, _) ->
            let _ = ap._reset() 
            let todo = mkTodo (Guid.NewGuid()) "test" [] []
            let added = ap.addTodo todo
            Expect.isOk added "should be ok"
            ap |> updateStateIfNecessary
            let result = ap.addTodo todo
            Expect.isError result "should be error"

        multipleTestCase "add a todo - ok" currentTestConfs <| fun (ap, _, _) ->
            let _ = ap._reset()
            let todo = mkTodo (Guid.NewGuid()) "test" [] []
            let result = ap.addTodo todo
            Expect.isOk result "should be ok"

        multipleTestCase "add a todo X - ok" currentTestConfs <| fun (ap, _, _) ->
            let _ = ap._reset()
            let todo = mkTodo (Guid.NewGuid()) "test" [] []
            let result = ap.addTodo todo
            Expect.isOk result "sould be ok"

        multipleTestCase "add two todos - Ok" currentTestConfs <| fun (ap, _, _) -> 
            let _ = ap._reset()

            let todo1 = mkTodo (Guid.NewGuid()) "test" [] []
            let todo2 = mkTodo  (Guid.NewGuid()) "test2" [] []
            let result = ap.add2Todos (todo1, todo2)
            Expect.isOk result "should be ok"
            let todos = ap.getAllTodos()
            Expect.isOk todos "should be ok"

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
            ap |> updateStateIfNecessary
            Expect.isOk result "should be ok"

            let todo = mkTodo id1 "test" [] [id2]
            let result = ap.addTodo todo
            ap |> updateStateIfNecessary
            Expect.isOk result "should be ok"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let todos = apUpgd.getAllTodos().OkValue
            Expect.equal todos [todo] "should be equal"

            let removed = apUpgd.removeTag id2
            apUpgd |> updateStateIfNecessary
            Expect.isOk removed "should be ok"
            let result = apUpgd.getAllTodos().OkValue
            Expect.isTrue (result.Head.TagIds |> List.isEmpty) "should be true"

        multipleTestCase "add and remove a todo 1 - Ok" currentTestConfs <| fun (ap, apUpgd, migrator)  ->
            let _ = ap._reset()

            let todo = mkTodo (Guid.NewGuid()) "test" [] []
            let result = ap.addTodo todo
            ap |> updateStateIfNecessary
            Expect.isOk result "should be ok"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let todos = apUpgd.getAllTodos() |> Result.get
            Expect.equal todos [todo] "should be equal"
            let result = apUpgd.removeTodo todo.Id
            Expect.isOk result "should be ok"
            let todos = apUpgd.getAllTodos() |> Result.get
            Expect.equal todos [] "should be equal"

        multipleTestCase "add and remove a todo 2 - Ok" currentTestConfs <| fun (ap, apUpgd, migrator)  ->
            let _ = ap._reset()

            let todo = mkTodo (Guid.NewGuid()) "test" [] []
            let result = ap.addTodo todo
            Expect.isOk result "should be ok"

            ap |> updateStateIfNecessary

            let todos = ap.getAllTodos() |> Result.get
            Expect.equal todos [todo] "should be equal"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let removed = apUpgd.removeTodo todo.Id
            apUpgd |> updateStateIfNecessary
            Expect.isOk removed "should be ok"
            let result = apUpgd.getAllTodos() |> Result.get
            Expect.equal result [] "should be equal"

        multipleTestCase "remove an unexisting todo - Ko" currentTestConfs <| fun (ap, _, _) ->
            let _ = ap._reset()
            let newGuid = Guid.NewGuid()
            let removed = ap.removeTodo newGuid
            Expect.isError removed "should be error"
            let result = removed |> getError
            Expect.equal result (sprintf "A Todo with id '%A' does not exist" newGuid) "should be equal"

        multipleTestCase "add category" currentTestConfs <| fun (ap, apUpgd, migrator) ->
            let _ = ap._reset()
            let category = mkCategory (Guid.NewGuid()) "test"
            let added = ap.addCategory category
            ap |> updateStateIfNecessary
            Expect.isOk added "should be ok"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let result = apUpgd.getAllCategories() |> Result.get
            Expect.equal result [category] "should be equal"

        multipleTestCase "add and remove a category 1" currentTestConfs <| fun (ap, apUpgd, migrator)  ->
            let _ = ap._reset()
            let category = mkCategory (Guid.NewGuid()) "test"
            let added = ap.addCategory category
            async {
                do! Async.Sleep 10
                return ()
            } |> Async.RunSynchronously
            ap |> updateStateIfNecessary
            
            Expect.isOk added "should be ok"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let categories = apUpgd.getAllCategories() |> Result.get
            Expect.equal categories [category] "should be equal"
            let removed = apUpgd.removeCategory category.Id
            apUpgd |> updateStateIfNecessary
            Expect.isOk removed "should be ok"
            let result = apUpgd.getAllCategories() |> Result.get
            Expect.equal result [] "should be equal"

        multipleTestCase "add and remove a category 2" currentTestConfs <| fun (ap, apUpgd, migrator)  ->
            let _ = ap._reset()
            let category = mkCategory (Guid.NewGuid()) "testuu"
            let added = ap.addCategory category
            ap |> updateStateIfNecessary
            async {
                do! Async.Sleep 10
                return ()
            } |> Async.RunSynchronously
            
            Expect.isOk added "should be ok"
            let categories = ap.getAllCategories() |> Result.get
            Expect.equal categories [category] "should be equal"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let removed = apUpgd.removeCategory category.Id
            apUpgd |> updateStateIfNecessary

            Expect.isOk removed "should be ok"
            let result = apUpgd.getAllCategories() |> Result.get
            Expect.equal result [] "should be equal"

        multipleTestCase "add and remove a category 3" currentTestConfs <| fun (ap, apUpgd, migrator)  ->
            let _ = ap._reset()
            let category = mkCategory (Guid.NewGuid()) "testuu"
            let added = ap.addCategory category
            ap |> updateStateIfNecessary
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
            ap |> updateStateIfNecessary
            Expect.isOk added "should be ok"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            async {
                do! Async.Sleep 10
                return ()
            } |> Async.RunSynchronously
            let category' = apUpgd.getAllCategories() |> Result.get
            Expect.equal category' [category] "should be equal"

            apUpgd |> updateStateIfNecessary

            let todo = mkTodo (Guid.NewGuid()) "test" [Guid.NewGuid()] []
            let result = apUpgd.addTodo todo
            Expect.isError result "should be error"

        multipleTestCase "when remove a category all references to it should be removed from todos - Ok" currentTestConfs <| fun (ap, apUpgd, migrator) ->
            let _ = ap._reset()
            let categoryId = Guid.NewGuid()
            let category = mkCategory categoryId "test"
            let todo = mkTodo (Guid.NewGuid()) "test" [categoryId] []

            let _ = ap.addCategory category

            ap |> updateStateIfNecessary

            async {
                do! Async.Sleep 10
                return ()
            } |> Async.RunSynchronously
            
            let added = ap.addTodo todo    
            
            ap |> updateStateIfNecessary

            Expect.isOk added "should be ok"

            let hasMigrated = migrator()
            Expect.isOk hasMigrated "should be ok"

            let todos = apUpgd.getAllTodos().OkValue 
            Expect.equal todos [todo] "should be equal"
            let result = apUpgd.removeCategory categoryId

            apUpgd |> updateStateIfNecessary

            Expect.isOk result "should be ok"

            let result = apUpgd.getAllTodos().OkValue 
            Expect.equal (result |> List.head).CategoryIds [] "should be equal"

        multipleTestCase "when remove a category all references to it should be removed from todos 2 - Ok" currentTestConfs <| fun (ap, apUpgd, migrator) ->
            let _ = ap._reset()
            let categoryId1 = Guid.NewGuid()
            let categoryId2 = Guid.NewGuid()
            let category = mkCategory categoryId1 "test"
            let category2 = { Id = categoryId2; Name = "test2" }
            let todo = mkTodo (Guid.NewGuid()) "test" [categoryId1; categoryId2] []

            let _ = ap.addCategory category
            let _ = ap.addCategory category2
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
            ap |> updateStateIfNecessary
            let result = apUpgd.getAllTags() |> Result.get
            Expect.equal result [tag] "should be equal"

        multipleTestCase "add and remove a tag" currentTestConfs <| fun (ap, apUpgd, migrator) ->
            let _ = ap._reset()
            let tag = mkTag (Guid.NewGuid()) "test" Color.Blue
            let added = ap.addTag tag
            ap |> updateStateIfNecessary
            Expect.isOk added "should be ok"
            let tags = ap.getAllTags() |> Result.get
            Expect.equal tags [tag] "should be equal"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let removed = apUpgd.removeTag tag.Id
            apUpgd |> updateStateIfNecessary
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
                    async {
                        do! Async.Sleep 1
                        return ()
                    } |> Async.RunSynchronously
                    ap |> updateStateIfNecessary
                    let! app' = ap.addTodo todo
                    return app'
                } 
            ap |> updateStateIfNecessary
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
                    ap |> updateStateIfNecessary
                    async {
                        do! Async.Sleep 1
                        return ()
                    } |> Async.RunSynchronously
                    let! app' = ap.addTodo todo
                    return app'
                } 
            ap |> updateStateIfNecessary
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

            ap |> updateStateIfNecessary
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
            let actualEvents = result.TodoEvents |> Set.ofList
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
            async {
                do! Async.Sleep 10
                return ()
            } |> Async.RunSynchronously
            let timeBeforeAddingSecondTodo = System.DateTime.Now
            async {
                do! Async.Sleep 10
                return ()
            } |> Async.RunSynchronously
            let todo2 = mkTodo (Guid.NewGuid()) "test two" [] []
            let added2 = ap.addTodo todo2
            async {
                do! Async.Sleep 10
                return ()
            } |> Async.RunSynchronously
            let result = ap.todoReport timeBeforeAddingSecondTodo System.DateTime.Now
            let actualEvents = result.TodoEvents |> Set.ofList
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
            async {
                do! Async.Sleep 10
                return ()
            } |> Async.RunSynchronously
            let added1 = ap.addTodo todo1
            let beforeAddingSecond = System.DateTime.Now
            async {
                do! Async.Sleep 1
                return ()
            } |> Async.RunSynchronously
            let todo2 = mkTodo (Guid.NewGuid()) "test2" [] []
            let added2 = ap.addTodo todo2
            let result = ap.todoReport beforeAddingFirst beforeAddingSecond 
            let actualEvents = result.TodoEvents |> Set.ofList
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
    let doAddNewTodo() =
        let ap = AppVersions.currentPostgresApp
        for i = 0 to 9 do
            let todo = mkTodo (Guid.NewGuid()) ("todo" + (i.ToString())) [] []
            ap.addTodo todo |> ignore
            ()

    ptestList "massive sequence adding - Ok" [
        testCase "add many todos" <| fun _ ->
            Expect.isTrue true "should be true"
            let ap = AppVersions.currentPostgresApp
            let _ = ap._reset()

            for i = 0 to 999 do
                let todo = mkTodo (Guid.NewGuid()) ("todo" + (i.ToString())) [] []
                let added = ap.addTodo todo 
                ()

            let actualTodos = ap.getAllTodos().OkValue

            Expect.equal actualTodos.Length 1000 "should be equal"

        testCase "add many todos in parallel" <| fun _ ->
            let ap = AppVersions.currentPostgresApp
            let _ = ap._reset()

            for i = 0 to 99 do   
                let thread = new Thread(doAddNewTodo)
                thread.Start()
                thread.Join()

            let actualTodos = ap.getAllTodos().OkValue

            Expect.equal actualTodos.Length 1000 "should be equal"
    ] |> testSequenced