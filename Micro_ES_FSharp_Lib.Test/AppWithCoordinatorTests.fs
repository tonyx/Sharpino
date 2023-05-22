
module Tests.Tonyx.EventSourcing.Sample.AppWithCoordinator

open Expecto
open FsCheck
open FsCheck.Prop
open Expecto.Tests
open System
open FSharp.Core

open Tonyx.EventSourcing.Sample.TodosAggregate
open Tonyx.EventSourcing.Sample.Todos.Models.CategoriesModel
open Tonyx.EventSourcing.Sample.Todos.Models.TodosModel
open Tonyx.EventSourcing.Sample.TagsAggregate
open Tonyx.EventSourcing.Sample.Tags.Models.TagsModel
open Tonyx.EventSourcing.Utils
open Tonyx.EventSourcing.Sample
open Tonyx.EventSourcing
open Tonyx.EventSourcing.Sample.CategoriesAggregate
open System.Runtime.CompilerServices

let db = Repository.storage


let setUp() =
    db.Reset "_01" TodosAggregate.StorageName 
    Cache.EventCache<TodosAggregate>.Instance.Clear()
    Cache.SnapCache<TodosAggregate>.Instance.Clear()

    db.Reset "_02" TodosAggregate.TodosAggregate'.StorageName 
    Cache.EventCache<TodosAggregate.TodosAggregate'>.Instance.Clear()
    Cache.SnapCache<TodosAggregate.TodosAggregate'>.Instance.Clear()

    db.Reset "_01" TagsAggregate.StorageName
    Cache.EventCache<TagsAggregate>.Instance.Clear()
    Cache.SnapCache<TagsAggregate>.Instance.Clear()

    db.Reset "_02" CategoriesAggregate.StorageName
    Cache.EventCache<CategoriesAggregate>.Instance.Clear()
    Cache.SnapCache<CategoriesAggregate>.Instance.Clear()

let app =
    AppCoordinator.applicationVersion2

let apps =
    [
        AppCoordinator.applicationVersion1
        AppCoordinator.applicationVersion2
    ]

let allConfs =
    [
        (AppCoordinator.applicationVersion1, (fun () -> Conf.storageType <- Conf.StorageType.Memory), AppCoordinator.applicationVersion1, fun () -> () |> Result.Ok) 
        (AppCoordinator.applicationVersion2, (fun () -> Conf.storageType <- Conf.StorageType.Memory), AppCoordinator.applicationVersion2, fun () -> () |> Result.Ok)
        (AppCoordinator.applicationVersion1, (fun () -> Conf.storageType <- Conf.StorageType.Postgres), AppCoordinator.applicationVersion1, fun () -> () |> Result.Ok)
        (AppCoordinator.applicationVersion2, (fun () -> Conf.storageType <- Conf.StorageType.Postgres), AppCoordinator.applicationVersion2, fun () -> () |> Result.Ok)
        (AppCoordinator.applicationVersion1, (fun () -> Conf.storageType <- Conf.StorageType.Memory), AppCoordinator.applicationVersion2, App.migrate) 
        (AppCoordinator.applicationVersion2, (fun () -> Conf.storageType <- Conf.StorageType.Memory), AppCoordinator.applicationVersion2, fun () -> () |> Result.Ok)
        (AppCoordinator.applicationVersion1, (fun () -> Conf.storageType <- Conf.StorageType.Postgres), AppCoordinator.applicationVersion2, App.migrate)
        (AppCoordinator.applicationVersion2, (fun () -> Conf.storageType <- Conf.StorageType.Postgres), AppCoordinator.applicationVersion2, fun () -> () |> Result.Ok)
    ]

let focusConfs =
    [
        (AppCoordinator.applicationVersion1, fun () -> Conf.storageType <- Conf.StorageType.Memory)
    ]

let currentTestConfs = allConfs

[<Tests>]
let parametricTests2 =
    testList "parametric 2" (

        [AppCoordinator.applicationVersion1; AppCoordinator.applicationVersion2]
        |> List.map (fun x ->
            testParam x [
                "first 2 " + (x.ToString()), 
                    fun ap () ->
                        let _ = setUp()
                        let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [] }
                        let result = ap.addTodo todo
                        Expect.isOk result "should be ok"
            ] 
        )
        |> Seq.concat
        |> List.ofSeq
    )

[<Tests>]
let appWithCoordinatorTests =

    let multipleTestCase name par myTest =
        testList name (
            par
            |> List.map 
                (fun (app, cnf, upgd, shdTstUpgrd) ->
                testParam (app, cnf, upgd, shdTstUpgrd) [
                        (app.ToString())+ cnf.ToString(), 
                            fun (app, cnf, upgd, shdTstUpgrd) () ->
                                lock Conf.storageType (fun () -> myTest(app, cnf, upgd, shdTstUpgrd))
                ]
                |> List.ofSeq
            )
            |> List.concat
        ) 

    let fmultipleTestCase name cnf myTest =
        ftestList name (
            cnf
            |> List.map 
                (fun (ap, cnf, upgd, upgrader) ->
                testParam (ap, cnf, upgd, upgrader ) [
                        (ap.ToString())+ cnf.ToString(), 
                            fun (ap, cnf, upgd, upgrader) () ->
                                // locking this because cnf will change it using mutability 
                                lock Conf.storageType (fun () -> myTest(ap, cnf, upgd, upgrader))
                ]
                |> List.ofSeq
            )
            |> List.concat
        )

    let pmultipleTestCase name cnf test =
        ptestList name (
            cnf
            |> List.map 
                (fun (ap, cnf, upgd, upgrader) ->
                testParam (ap, cnf, upgd, upgrader) [
                        (ap.ToString())+ cnf.ToString(), 
                            fun (ap, cnf, upgd, upgrader) () ->
                                test(ap, cnf, upgd, upgrader)
                ]
                |> List.ofSeq
            )
            |> List.concat
        )

    testList "App with coordinator test - Ok" [

        multipleTestCase "add two todos -- oook" currentTestConfs <| fun (ap, cnf, upgd, upgrader) ->
            let _ = setUp()
            let _ = cnf()
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [] }
            let result = ap.addTodo todo
            Expect.isOk result "should be ok"

        multipleTestCase "add two todos - Ok" currentTestConfs <| fun (ap, cnf, upgd, shdTstUpgrd) -> 
            let _ = setUp()
            let _ = cnf()
            let todo1 = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [] }
            let todo2 = { Id = Guid.NewGuid(); Description = "test2"; CategoryIds = []; TagIds = [] }
            let result = ap.add2Todos (todo1, todo2)
            Expect.isOk result "should be ok"
            let todos = ap.getAllTodos()
            Expect.isOk todos "should be ok"
            Expect.equal (todos.OkValue |> Set.ofList) ([todo1; todo2] |> Set.ofList)  "should be equal"

        multipleTestCase "add two todos, one has an unexisting category - Ko" currentTestConfs <| fun (ap, cnf, upgd, shdTstUpgrd) -> // this is for checking the case of a command returning two events
            let _ = setUp()
            let _ = cnf()
            let todo1 = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = [Guid.NewGuid()]; TagIds = [] }
            let todo2 = { Id = Guid.NewGuid(); Description = "test2"; CategoryIds = []; TagIds = [] }
            let result = ap.add2Todos (todo1, todo2)
            Expect.isError result "should be error"
            let todos = ap.getAllTodos().OkValue 
            Expect.equal todos [] "should be equal"

        multipleTestCase "add two todos, one has an unexisting tag - Ko" currentTestConfs <| fun (ap, cnf, upgd, shdTstUpgrd) -> // this is for checking the case of a command returning two events
            let _ = setUp()
            let _ = cnf()
            let todo1 = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [] }
            let todo2 = { Id = Guid.NewGuid(); Description = "test2"; CategoryIds = []; TagIds = [Guid.NewGuid()] }
            let result = ap.add2Todos (todo1, todo2)
            Expect.isError result "should be error"
            let todos = ap.getAllTodos().OkValue 
            Expect.equal todos [] "should be equal"

        multipleTestCase "add a todo with an unexisting tag - Ok" currentTestConfs  <| fun (ap, cnf, upgd, shdTstUpgrd) ->
            let _ = setUp()
            let _ = cnf()
            let id1 = Guid.NewGuid()
            let id2 = Guid.NewGuid()
            let todo = { Id = id1; Description = "test"; CategoryIds = []; TagIds = [id2] }
            let result = ap.addTodo todo
            Expect.isError result "should be error"

        multipleTestCase "when remove a tag then all the reference to that tag are also removed from any todos - Ok" currentTestConfs  <| fun (ap, cnf, apUpgd, migrator) ->
            let _ = setUp()
            let _ = cnf()
            let id1 = Guid.NewGuid()
            let id2 = Guid.NewGuid()
            let tag = { Id = id2; Name = "test"; Color = Color.Blue }
            let result = ap.addTag tag
            Expect.isOk result "should be ok"

            let todo = { Id = id1; Description = "test"; CategoryIds = []; TagIds = [id2] }
            let result = ap.addTodo todo

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            Expect.isOk result "should be ok"
            let todos = apUpgd.getAllTodos().OkValue
            Expect.equal todos [todo] "should be equal"
            let result = apUpgd.removeTag id2
            Expect.isOk result "should be ok"
            let todos = apUpgd.getAllTodos().OkValue
            Expect.isTrue (todos.Head.TagIds |> List.isEmpty) "should be true"

        multipleTestCase "add and remove a todo 1 - Ok" currentTestConfs <| fun (ap, cnf, apUpgd, migrator)  ->
            let _ = setUp()
            let _ = cnf()

            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [] }
            let result = ap.addTodo todo
            Expect.isOk result "should be ok"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let todos = apUpgd.getAllTodos() |> Result.get
            Expect.equal todos [todo] "should be equal"
            let result = apUpgd.removeTodo todo.Id
            Expect.isOk result "should be ok"
            let todos = apUpgd.getAllTodos() |> Result.get
            Expect.equal todos [] "should be equal"

        multipleTestCase "add and remove a todo 2 - Ok" currentTestConfs <| fun (ap, cnf, apUpgd, migrator)  ->
            let _ = setUp()
            let _ = cnf()

            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [] }
            let result = ap.addTodo todo
            Expect.isOk result "should be ok"

            let todos = ap.getAllTodos() |> Result.get
            Expect.equal todos [todo] "should be equal"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let result = apUpgd.removeTodo todo.Id
            Expect.isOk result "should be ok"
            let todos = apUpgd.getAllTodos() |> Result.get
            Expect.equal todos [] "should be equal"

        multipleTestCase "remove an unexisting todo - Ko" currentTestConfs <| fun (ap, cnf, apUpgd, migrator) ->
            let _ = setUp()
            let _ = cnf()
            let newGuid = Guid.NewGuid()
            let result = ap.removeTodo newGuid
            Expect.isError result "should be error"
            let errMsg = result |> getError
            Expect.equal errMsg (sprintf "A Todo with id '%A' does not exist" newGuid) "should be equal"

        multipleTestCase "add category" currentTestConfs <| fun (ap, cnf, apUpgd, migrator) ->
            let _ = setUp()
            let _ = cnf()
            let category = { Id = Guid.NewGuid(); Name = "test"}
            let result = ap.addCategory category
            Expect.isOk result "should be ok"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let categories = apUpgd.getAllCategories() |> Result.get
            Expect.equal categories [category] "should be equal"

        multipleTestCase "add and remove a category 1" currentTestConfs <| fun (ap, cnf, apUpgd, migrator)  ->
            let _ = setUp()
            let _ = cnf()
            let category = { Id = Guid.NewGuid(); Name = "test"}
            let result = ap.addCategory category
            Expect.isOk result "should be ok"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let categories = apUpgd.getAllCategories() |> Result.get
            Expect.equal categories [category] "should be equal"
            let result = apUpgd.removeCategory category.Id
            Expect.isOk result "should be ok"
            let categories = apUpgd.getAllCategories() |> Result.get
            Expect.equal categories [] "should be equal"

        multipleTestCase "add and remove a category 2" currentTestConfs <| fun (ap, cnf, apUpgd, migrator)  ->
            let _ = setUp()
            let _ = cnf()
            let category = { Id = Guid.NewGuid(); Name = "testuu"}
            let result = ap.addCategory category
            Expect.isOk result "should be ok"
            let categories = ap.getAllCategories() |> Result.get
            Expect.equal categories [category] "should be equal"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let result = apUpgd.removeCategory category.Id
            Expect.isOk result "should be ok"
            let categories = apUpgd.getAllCategories() |> Result.get
            Expect.equal categories [] "should be equal"
        |> testSequenced

        multipleTestCase "add and remove a category 3" currentTestConfs <| fun (ap, cnf, apUpgd, migrator)  ->
            let _ = setUp()
            let _ = cnf()
            let category = { Id = Guid.NewGuid(); Name = "testuu"}
            let result = ap.addCategory category
            Expect.isOk result "should be ok"
            let categories = ap.getAllCategories() |> Result.get
            Expect.equal categories [category] "should be equal"
            let result = ap.removeCategory category.Id
            Expect.isOk result "should be ok"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let categories = apUpgd.getAllCategories() |> Result.get
            Expect.equal categories [] "should be equal"
        |> testSequenced


        multipleTestCase "add a todo with an unexisting category - KO" currentTestConfs <| fun (ap, cnf, apUpgd, migrator) ->
            let _ = setUp()
            let _ = cnf()
            let category = { Id = Guid.NewGuid(); Name = "test"}
            let result = ap.addCategory category
            Expect.isOk result "should be ok"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let category' = apUpgd.getAllCategories() |> Result.get
            Expect.equal category' [category] "should be equal"
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = [Guid.NewGuid()]; TagIds = [] }
            let result = apUpgd.addTodo todo
            Expect.isError result "should be error"

        multipleTestCase "when remove a category all references to it should be removed from todos - Ok" currentTestConfs <| fun (ap, cnf, apUpgd, migrator) ->
            let _ = setUp()
            let _ = cnf()
            let categoryId = Guid.NewGuid()
            let category = { Id = categoryId; Name = "test" }
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = [categoryId]; TagIds = [] }

            let added =
                ceResult {
                    let! _ = ap.addCategory category
                    let! ap' = ap.addTodo todo
                    return ap'
                } 
            Expect.isOk added "should be ok"

            let hasMigrated = migrator()
            Expect.isOk hasMigrated "should be ok"

            let todos = apUpgd.getAllTodos().OkValue 
            Expect.equal todos [todo] "should be equal"
            let result = apUpgd.removeCategory categoryId
            Expect.isOk result "should be ok"

            let todos = apUpgd.getAllTodos().OkValue 
            Expect.equal (todos |> List.head).CategoryIds [] "should be equal"

        multipleTestCase "when remove a category all references to it should be removed from todos 2 - Ok" currentTestConfs <| fun (ap, cnf, apUpgd, migrator) ->
            let _ = setUp()
            let _ = cnf()
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

            let todos = apUpgd.getAllTodos().OkValue 
            Expect.equal (todos |> List.head).CategoryIds [categoryId2] "should be equal"

        multipleTestCase "when remove a category all references to it should be removed from todos 3 - Ok" currentTestConfs <| fun (ap, cnf, apUpgd, migrator) ->
            let _ = setUp()
            let _ = cnf()
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

            let result = ap.removeCategory categoryId1
            Expect.isOk result "should be ok"

            let migrated = migrator()
            Expect.isOk migrated "should be ok"

            let todos = apUpgd.getAllTodos().OkValue 
            Expect.equal (todos |> List.head).CategoryIds [categoryId2] "should be equal"

        multipleTestCase "add tag" currentTestConfs <| fun (ap, cnf, upgd, shdTstUpgrd) ->
            let _ = setUp()
            let _ = cnf()
            let tag = { Id = Guid.NewGuid(); Name = "test"; Color = Color.Blue }
            let result = ap.addTag tag
            Expect.isOk result "should be ok"
            let tags = ap.getAllTags() |> Result.get
            Expect.equal tags [tag] "should be equal"

        multipleTestCase "add and remove a tag" currentTestConfs <| fun (ap, cnf, upgd, shdTstUpgrd) ->
            let _ = setUp()
            let _ = cnf()
            let tag = { Id = Guid.NewGuid(); Name = "test"; Color = Color.Blue }
            let result = ap.addTag tag
            Expect.isOk result "should be ok"
            let tags = ap.getAllTags() |> Result.get
            Expect.equal tags [tag] "should be equal"
            let result = ap.removeTag tag.Id
            Expect.isOk result "should be ok"
            let tags = ap.getAllTags() |> Result.get
            Expect.equal tags [] "should be equal"

        multipleTestCase "when remove a tag all references to it should be removed from existing todos - Ok" currentTestConfs <| fun (ap, cnf, upgd, shdTstUpgrd) ->
            let _ = setUp()
            let _ = cnf()
            let tagId = Guid.NewGuid()
            let tag = { Id = tagId; Name = "test"; Color = Color.Blue }
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [tagId] }

            let added =
                ceResult {
                    let! _ = ap.addTag tag
                    let! app' = ap.addTodo todo
                    return app'

                } 
            Expect.isOk added "should be ok"

            let todos = ap.getAllTodos().OkValue 
            Expect.equal todos [todo] "should be equal"
            let result = ap.removeTag tagId
            Expect.isOk result "should be ok"

            let todos = ap.getAllTodos().OkValue 
            Expect.equal (todos |> List.head).TagIds [] "should be equal"

        multipleTestCase "when remove a tag all references to it should be removed from existing todos 2 - Ok" currentTestConfs <| fun (ap, cnf, upgd, shdTstUpgrd) ->
            let _ = setUp()
            let _ = cnf()
            let tagId = Guid.NewGuid()
            let tag1 = { Id = tagId; Name = "test"; Color = Color.Blue }
            let tagId2 = Guid.NewGuid()
            let tag2 = { Id = tagId2; Name = "test2"; Color = Color.Red }
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [tagId; tagId2] }

            let _ = ap.addTag tag1
            let _ = ap.addTag tag2
            let _ = ap.addTodo todo

            let todos = ap.getAllTodos().OkValue 
            Expect.equal todos [todo] "should be equal"
            let result = ap.removeTag tagId
            Expect.isOk result "should be ok"

            let todos = ap.getAllTodos().OkValue 
            Expect.equal (todos |> List.head).TagIds [tagId2] "should be equal"

    ] 
    |> testSequenced