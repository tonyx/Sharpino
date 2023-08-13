
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
open Sharpino.Sample.Models.CategoriesModel
open Sharpino.Sample.Models.TodosModel
open Sharpino.Sample.TagsAggregate
open Sharpino.Sample.Models.TagsModel
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
open Sharpino.TestUtils
open System.Threading
open FsToolkit.ErrorHandling
open Microsoft.FSharp.Quotations
open Sharpino.Lib.EvStore

let allVersions =
    [

        // (AppVersions.currentPostgresApp,        AppVersions.currentPostgresApp,     fun () -> () |> Result.Ok)
        // (AppVersions.upgradedPostgresApp,       AppVersions.upgradedPostgresApp,    fun () -> () |> Result.Ok)
        // (AppVersions.currentPostgresApp,        AppVersions.upgradedPostgresApp,    AppVersions.currentPostgresApp._migrator.Value)

        (AppVersions.currentMemoryApp,          AppVersions.currentMemoryApp,       fun () -> () |> Result.Ok)
        (AppVersions.upgradedMemoryApp,         AppVersions.upgradedMemoryApp,      fun () -> () |> Result.Ok)
        (AppVersions.currentMemoryApp,          AppVersions.upgradedMemoryApp,      AppVersions.currentMemoryApp._migrator.Value)

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
let multiVersionsTests =
    testList "App with coordinator test - Ok" [

        // let eventStoreBridge = EventStoreBridgeFS(Conf.eventStoreConnection)
        let eventStoreBridge: EventStore.EventStoreBridgeFS = Sharpino.EventStore.EventStoreBridgeFS(Conf.eventStoreConnection)
        multipleTestCase "generate the events directly without using the repository - Ok " currentTestConfs <| fun (ap, _, _) ->
            let _ = ap._reset()
            let id = Guid.NewGuid()
            let event = Todos.TodoEvents.TodoAdded { Id = id; Description = "test"; CategoryIds = []; TagIds = [] }

            // I am adding twice the same event and the "evolve" will ignore it
            let _ = ap._addEvents (TodosAggregate.Version, [ event |> serialize], TodosAggregate.StorageName )
            let _ = ap._addEvents (TodosAggregate.Version, [ event |> serialize], TodosAggregate.StorageName)
            let _ = ap._addEvents (TodosAggregate'.Version, [ event |> serialize], TodosAggregate'.StorageName)
            eventStoreBridge |> LightRepository.updateState<TodosAggregate, TodoEvent>
            let todos = ap.getAllTodos()
            Expect.isOk todos "should be ok"
            Expect.equal (todos.OkValue) [{ Id = id; Description = "test"; CategoryIds = []; TagIds = [] }] "should be equal"

        multipleTestCase "in case events are unconsistent in the storage, then the evolve will be able to skip the unconsistent events - Ok" currentTestConfs <| fun (ap, _, _) ->
            let _ = ap._reset()
            let id = Guid.NewGuid()
            let event = TodoEvents.TodoAdded { Id = id; Description = "test"; CategoryIds = []; TagIds = [] }
            let _ = ap._addEvents (TodosAggregate.Version, [ event |> serialize], TodosAggregate.StorageName)
            let _ = ap._addEvents (TodosAggregate.Version, [ event |> serialize], TodosAggregate.StorageName)

            let _ = ap._addEvents (TodosAggregate'.Version, [ event |> serialize], TodosAggregate'.StorageName)
            let _ = ap._addEvents (TodosAggregate'.Version, [ event |> serialize], TodosAggregate'.StorageName)

            eventStoreBridge |> LightRepository.updateState<TodosAggregate, TodoEvent>
            let todos = ap.getAllTodos()
            Expect.isOk todos "should be ok"
            Expect.equal (todos.OkValue) [{ Id = id; Description = "test"; CategoryIds = []; TagIds = [] }] "should be equal"


        multipleTestCase "in case events are unconsistent in the storage, then the evolve will be able to skip the unconsistent events second try - Ok" currentTestConfs <| fun (ap, _, _) ->
            let _ = ap._reset() 
            let id = Guid.NewGuid()
            let id2 = Guid.NewGuid()
            let event = TodoEvents.TodoAdded { Id = id; Description = "test"; CategoryIds = []; TagIds = [] }

            let event2 = TodoEvents.TodoAdded { Id = id2; Description = "test second part"; CategoryIds = []; TagIds = [] }

            let _ = ap._addEvents (TodosAggregate.Version, [ event |> serialize ],  TodosAggregate.StorageName) 
            let _ = ap._addEvents (TodosAggregate.Version, [ event |> serialize ],  TodosAggregate.StorageName) 

            let _ = ap._addEvents (TodosAggregate'.Version, [ event |> serialize ],  TodosAggregate'.StorageName)
            let _ = ap._addEvents (TodosAggregate'.Version, [ event |> serialize ],  TodosAggregate'.StorageName)

            let _ = ap._addEvents (TodosAggregate.Version,  [ event2 |> serialize ], TodosAggregate.StorageName)
            let _ = ap._addEvents (TodosAggregate'.Version, [ event2 |> serialize ], TodosAggregate'.StorageName)

            eventStoreBridge |> LightRepository.updateState<TodosAggregate, TodoEvent>
            eventStoreBridge |> LightRepository.updateState<TodosAggregate', TodoEvent'>
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

        multipleTestCase "add the same todo twice - Ko" currentTestConfs <| fun (ap, _, _) ->
            let _ = ap._reset() 
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [] }
            let added = ap.addTodo todo
            Expect.isOk added "should be ok"
            eventStoreBridge |> LightRepository.updateState<TodosAggregate, TodoEvent>
            eventStoreBridge |> LightRepository.updateState<TodosAggregate', TodoEvent'> 
            let result = ap.addTodo todo
            Expect.isError result "should be error"

        multipleTestCase "add a todo - ok" currentTestConfs <| fun (ap, _, _) ->
            let _ = ap._reset()
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [] }
            let result = ap.addTodo todo
            Expect.isOk result "should be ok"

        multipleTestCase "add a todo X - ok" currentTestConfs <| fun (ap, _, _) ->
            let _ = ap._reset()
            let todo = { Id = Guid.NewGuid(); Description = "testoh"; CategoryIds = []; TagIds = [] }
            let result = ap.addTodo todo
            Expect.isOk result "sould be ok"

        multipleTestCase "add two todos - Ok" currentTestConfs <| fun (ap, _, _) -> 
            let _ = ap._reset()

            let todo1 = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [] }
            let todo2 = { Id = Guid.NewGuid(); Description = "test2"; CategoryIds = []; TagIds = [] }
            let result = ap.add2Todos (todo1, todo2)
            Expect.isOk result "should be ok"
            let todos = ap.getAllTodos()
            Expect.isOk todos "should be ok"

        multipleTestCase "add two todos, one has an unexisting category - Ko" currentTestConfs <| fun (ap, upgd, shdTstUpgrd) -> // this is for checking the case of a command returning two events
            let _ = ap._reset()
            let todo1 = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = [Guid.NewGuid()]; TagIds = [] }
            let todo2 = { Id = Guid.NewGuid(); Description = "test2"; CategoryIds = []; TagIds = [] }
            let added = ap.add2Todos (todo1, todo2)
            Expect.isError added "should be error"
            let todos = ap.getAllTodos().OkValue 
            Expect.equal todos [] "should be equal"

        multipleTestCase "add two todos, one has an unexisting tag - Ko" currentTestConfs <| fun (ap, upgd, shdTstUpgrd) -> // this is for checking the case of a command returning two events
            let _ = ap._reset()
            let todo1 = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [] }
            let todo2 = { Id = Guid.NewGuid(); Description = "test2"; CategoryIds = []; TagIds = [Guid.NewGuid()] }
            let added = ap.add2Todos (todo1, todo2)
            Expect.isError added "should be error"
            let result = ap.getAllTodos().OkValue 
            Expect.equal result [] "should be equal"

        multipleTestCase "add a todo with an unexisting tag - Ok" currentTestConfs  <| fun (ap, _, _) ->
            let _ = ap._reset()
            let id1 = Guid.NewGuid()
            let id2 = Guid.NewGuid()
            let todo = { Id = id1; Description = "test"; CategoryIds = []; TagIds = [id2] }
            let result = ap.addTodo todo
            Expect.isError result "should be error"

        multipleTestCase "when remove a tag then all the reference to that tag are also removed from any todos - Ok" currentTestConfs  <| fun (ap, apUpgd, migrator) ->
            let _ = ap._reset()
            let id1 = Guid.NewGuid()
            let id2 = Guid.NewGuid()
            let tag = { Id = id2; Name = "test"; Color = Color.Blue }
            let result = ap.addTag tag
            eventStoreBridge |> LightRepository.updateState<TodosAggregate, TodoEvent>
            Expect.isOk result "should be ok"

            let todo = { Id = id1; Description = "test"; CategoryIds = []; TagIds = [id2] }
            let result = ap.addTodo todo
            eventStoreBridge |> LightRepository.updateState<TodosAggregate, TodoEvent>
            Expect.isOk result "should be ok"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let todos = apUpgd.getAllTodos().OkValue
            Expect.equal todos [todo] "should be equal"

            let removed = apUpgd.removeTag id2
            eventStoreBridge |> LightRepository.updateState<TodosAggregate, TodoEvent>
            Expect.isOk removed "should be ok"
            let result = apUpgd.getAllTodos().OkValue
            Expect.isTrue (result.Head.TagIds |> List.isEmpty) "should be true"

        multipleTestCase "add and remove a todo 1 - Ok" currentTestConfs <| fun (ap, apUpgd, migrator)  ->
            let _ = ap._reset()

            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [] }
            let result = ap.addTodo todo
            eventStoreBridge |> LightRepository.updateState<TodosAggregate, TodoEvent>
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

            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [] }
            let result = ap.addTodo todo
            Expect.isOk result "should be ok"

            eventStoreBridge |> LightRepository.updateState<TodosAggregate, TodoEvent>

            let todos = ap.getAllTodos() |> Result.get
            Expect.equal todos [todo] "should be equal"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let removed = apUpgd.removeTodo todo.Id
            eventStoreBridge |> LightRepository.updateState<TodosAggregate, TodoEvent>
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
            let category = { Id = Guid.NewGuid(); Name = "test"}
            let added = ap.addCategory category
            eventStoreBridge |> LightRepository.updateState<CategoriesAggregate, CategoryEvent> 
            Expect.isOk added "should be ok"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let result = apUpgd.getAllCategories() |> Result.get
            Expect.equal result [category] "should be equal"

        multipleTestCase "add and remove a category 1" currentTestConfs <| fun (ap, apUpgd, migrator)  ->
            let _ = ap._reset()
            let category = { Id = Guid.NewGuid(); Name = "test"}
            let added = ap.addCategory category
            async {
                do! Async.Sleep 10
                return ()
            } |> Async.RunSynchronously
            eventStoreBridge |> LightRepository.updateState<CategoriesAggregate, CategoryEvent>
            
            Expect.isOk added "should be ok"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let categories = apUpgd.getAllCategories() |> Result.get
            Expect.equal categories [category] "should be equal"
            let removed = apUpgd.removeCategory category.Id
            eventStoreBridge |> LightRepository.updateState<CategoriesAggregate, CategoryEvent> 
            Expect.isOk removed "should be ok"
            let result = apUpgd.getAllCategories() |> Result.get
            Expect.equal result [] "should be equal"

        multipleTestCase "add and remove a category 2" currentTestConfs <| fun (ap, apUpgd, migrator)  ->
            let _ = ap._reset()
            let category = { Id = Guid.NewGuid(); Name = "testuu"}
            let added = ap.addCategory category
            eventStoreBridge |> LightRepository.updateState<CategoriesAggregate, CategoryEvent> 
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
            eventStoreBridge |> LightRepository.updateState<TagsAggregate, TagsEvents.TagEvent> 
            eventStoreBridge |> LightRepository.updateState<CategoriesAggregate, CategoryEvent> 

            Expect.isOk removed "should be ok"
            let result = apUpgd.getAllCategories() |> Result.get
            Expect.equal result [] "should be equal"

        multipleTestCase "add and remove a category 3" currentTestConfs <| fun (ap, apUpgd, migrator)  ->
            let _ = ap._reset()
            let category = { Id = Guid.NewGuid(); Name = "testuu"}
            let added = ap.addCategory category
            eventStoreBridge |> LightRepository.updateState<CategoriesAggregate, CategoryEvent> 
            eventStoreBridge |> LightRepository.updateState<TodosAggregate, TodoEvent> 
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
            let category = { Id = Guid.NewGuid(); Name = "test"}
            let added = ap.addCategory category
            Expect.isOk added "should be ok"
            eventStoreBridge |> LightRepository.updateState<CategoriesAggregate, CategoryEvent> 
            Expect.isOk added "should be ok"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            async {
                do! Async.Sleep 10
                return ()
            } |> Async.RunSynchronously
            let category' = apUpgd.getAllCategories() |> Result.get
            Expect.equal category' [category] "should be equal"

            eventStoreBridge |> LightRepository.updateState<CategoriesAggregate, CategoryEvent> 

            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = [Guid.NewGuid()]; TagIds = [] }
            let result = apUpgd.addTodo todo
            Expect.isError result "should be error"

        multipleTestCase "when remove a category all references to it should be removed from todos - Ok" currentTestConfs <| fun (ap, apUpgd, migrator) ->
            let _ = ap._reset()
            let categoryId = Guid.NewGuid()
            let category = { Id = categoryId; Name = "test" }
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = [categoryId]; TagIds = [] }

            let _ = ap.addCategory category

            eventStoreBridge |> LightRepository.updateState<CategoriesAggregate, CategoryEvent> 

            async {
                do! Async.Sleep 10
                return ()
            } |> Async.RunSynchronously
            
            let added = ap.addTodo todo    
            
            eventStoreBridge |> LightRepository.updateState<TodosAggregate, TodoEvent> 

            Expect.isOk added "should be ok"

            let hasMigrated = migrator()
            Expect.isOk hasMigrated "should be ok"

            let todos = apUpgd.getAllTodos().OkValue 
            Expect.equal todos [todo] "should be equal"
            let result = apUpgd.removeCategory categoryId

            eventStoreBridge |> LightRepository.updateState<CategoriesAggregate, CategoryEvent> 

            Expect.isOk result "should be ok"

            let result = apUpgd.getAllTodos().OkValue 
            Expect.equal (result |> List.head).CategoryIds [] "should be equal"

        multipleTestCase "when remove a category all references to it should be removed from todos 2 - Ok" currentTestConfs <| fun (ap, apUpgd, migrator) ->
            let _ = ap._reset()
            let categoryId1 = Guid.NewGuid()
            let categoryId2 = Guid.NewGuid()
            let category = { Id = categoryId1; Name = "test" }
            let category2 = { Id = categoryId2; Name = "test2" }
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = [categoryId1; categoryId2]; TagIds = [] }

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
            let category = { Id = categoryId1; Name = "test" }
            let category2 = { Id = categoryId2; Name = "test2" }
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
            let tag = { Id = Guid.NewGuid(); Name = "test"; Color = Color.Blue }
            let added = ap.addTag tag
            let migrated = migrator()
            Expect.isOk migrated "should be ok"
            Expect.isOk added "should be ok"
            eventStoreBridge |> LightRepository.updateState<TagsAggregate, TagEvent> 
            let result = apUpgd.getAllTags() |> Result.get
            Expect.equal result [tag] "should be equal"

        multipleTestCase "add and remove a tag" currentTestConfs <| fun (ap, apUpgd, migrator) ->
            let _ = ap._reset()
            let tag = { Id = Guid.NewGuid(); Name = "test"; Color = Color.Blue }
            let added = ap.addTag tag
            eventStoreBridge |> LightRepository.updateState<TagsAggregate, TagEvent> 
            Expect.isOk added "should be ok"
            let tags = ap.getAllTags() |> Result.get
            Expect.equal tags [tag] "should be equal"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let removed = apUpgd.removeTag tag.Id
            eventStoreBridge |> LightRepository.updateState<TagsAggregate, TagEvent> 
            Expect.isOk removed "should be ok"
            let result = apUpgd.getAllTags() |> Result.get
            Expect.equal result [] "should be equal"

        multipleTestCase "when remove a tag all references to it should be removed from existing todos - Ok" currentTestConfs <| fun (ap, apUpgd, migrator) ->
            let _ = ap._reset()
            let tagId = Guid.NewGuid()
            let tag = { Id = tagId; Name = "test"; Color = Color.Blue }
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [tagId] }

            let added =
                ResultCE.result {
                    let! _ = ap.addTag tag
                    async {
                        do! Async.Sleep 1
                        return ()
                    } |> Async.RunSynchronously
                    eventStoreBridge |> LightRepository.updateState<TagsAggregate, TagsEvents.TagEvent> 
                    let! app' = ap.addTodo todo
                    return app'
                } 
            eventStoreBridge |> LightRepository.updateState<TodosAggregate, TodoEvents.TodoEvent> 
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
            let tag = { Id = tagId; Name = "test"; Color = Color.Blue }
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [tagId] }

            let added =
                ResultCE.result {
                    let! _ = ap.addTag tag
                    eventStoreBridge |> LightRepository.updateState<TagsAggregate, TagEvent> 
                    async {
                        do! Async.Sleep 1
                        return ()
                    } |> Async.RunSynchronously
                    let! app' = ap.addTodo todo
                    return app'
                } 
            eventStoreBridge |> LightRepository.updateState<TodosAggregate, TodoEvent> 
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
            let tag1 = { Id = tagId; Name = "test"; Color = Color.Blue }
            let tagId2 = Guid.NewGuid()
            let tag2 = { Id = tagId2; Name = "test2"; Color = Color.Red }
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [tagId; tagId2] }

            let added = ap.addTag tag1
            Expect.isOk added "should be ok"
            let added' = ap.addTag tag2
            Expect.isOk added' "should be ok"
            let added'' = ap.addTodo todo
            Expect.isOk added'' "should be ok"

            eventStoreBridge |> LightRepository.updateState<TodosAggregate, TodoEvent> 
            let todos = ap.getAllTodos().OkValue 
            Expect.equal todos [todo] "should be equal"
            let removed = ap.removeTag tagId
            Expect.isOk removed "should be ok"

            let result = ap.getAllTodos().OkValue 
            Expect.equal (result |> List.head).TagIds [tagId2] "should be equal"

    ] 
    |> testSequenced

[<Tests>]
let multiCallTests =
    let doAddNewTodo() =
        let ap = AppVersions.currentPostgresApp
        for i = 0 to 9 do
            let todo = { Id = Guid.NewGuid(); Description = ((Guid.NewGuid().ToString()) + "todo"+(i.ToString())); CategoryIds = []; TagIds = [] }
            ap.addTodo todo |> ignore
            ()

    ptestList "massive sequence adding - Ok" [

        testCase "add many todos" <| fun _ ->
            Expect.isTrue true "should be true"
            let ap = AppVersions.currentPostgresApp
            let _ = ap._reset()

            for i = 0 to 999 do
                let todo = { Id = Guid.NewGuid(); Description = "todo"+(i.ToString()); CategoryIds = []; TagIds = [] }
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