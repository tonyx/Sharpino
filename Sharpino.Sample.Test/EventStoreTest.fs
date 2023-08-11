
module Tests.Sharpino.Sample.DbStorageTest

open Expecto
open FsCheck
open FsCheck.Prop
open Expecto.Tests
open System
open FSharp.Core

open Sharpino
open Sharpino.Utils
open Sharpino.Lib.EvStore
open Sharpino.Sample.EventStoreApp
open Sharpino.Sample.Models.TagsModel
open Sharpino.Sample.Models.TodosModel
open Sharpino.Sample.Models.CategoriesModel

open Sharpino.Sample.TagsAggregate
open Sharpino.Sample.TodosAggregate
open Sharpino.Sample.CategoriesAggregate

open Sharpino.Sample.Todos.TodoEvents
open Sharpino.Sample.Categories.CategoriesEvents
open Sharpino.Sample.Tags.TagsEvents

[<Tests>]
let utilsTests =
    let eventStoreBridge = EventStoreBridge(Conf.eventStoreConnection)
    let SetUp() =
        Cache.CurrentState<_>.Instance.Clear()
        Cache.CurrentState<TodosAggregate>.Instance.Clear()
        Cache.CurrentState<TodosAggregate'>.Instance.Clear()
        Cache.CurrentState<TagsAggregate>.Instance.Clear()
        Cache.CurrentState<CategoriesAggregate>.Instance.Clear()
        
        // Cache.CurrentStateRef<_>.Instance.Clear()
        // Cache.CurrentStateRef<TodosAggregate>.Instance.Clear()
        // Cache.CurrentStateRef<TodosAggregate'>.Instance.Clear()
        // Cache.CurrentStateRef<TagsAggregate>.Instance.Clear()
        // Cache.CurrentStateRef<CategoriesAggregate>.Instance.Clear()

        async {
            do! eventStoreBridge.ResetSnapshots("_01", "_tags")           |> Async.AwaitTask
            do! eventStoreBridge.ResetSnapshots("_01", "_tags")           |> Async.AwaitTask
            do! eventStoreBridge.ResetEvents("_01", "_tags")              |> Async.AwaitTask
            do! eventStoreBridge.ResetSnapshots("_01", "_todo")           |> Async.AwaitTask
            do! eventStoreBridge.ResetEvents("_01", "_todo")              |> Async.AwaitTask
            do! eventStoreBridge.ResetSnapshots("_02", "_todo")           |> Async.AwaitTask
            do! eventStoreBridge.ResetEvents("_02", "_todo")              |> Async.AwaitTask
            do! eventStoreBridge.ResetSnapshots("_01", "_categories")     |> Async.AwaitTask
            let! result = eventStoreBridge.ResetEvents("_01", "_categories") |> Async.AwaitTask

            return result
        }
        |> Async.RunSynchronously

    let eventStoreApp = EventStoreApp(eventStoreBridge)


    // warning: there is the possibility that some async issue may make some test fail
    // if that happens, you may try adding an explicit update state
    // like eventStore |> LightRepository.updateState<TodosAggregate, TodoEvent>
    // and/or a small delay
    testList "eventstore tests" [
        testCase "add a tag and then verify it is present - ok" <| fun _ ->
            let _ = SetUp()
            let tag = {Id = System.Guid.NewGuid(); Name = "tag1"; Color = Color.Green}     
            let added = eventStoreApp.AddTag tag
            Expect.isOk added "should be ok"

            let tags = eventStoreApp.GetAllTags() |> Result.get 
            Expect.equal tags [tag] "should be equal"

        testCase "add a todo - ok" <| fun _ ->
            let _ = SetUp()
            let todo: Todo = {Id = Guid.NewGuid(); Description = "descZ"; TagIds = []; CategoryIds = []}

            let result = eventStoreApp.AddTodo todo 
            eventStoreBridge |> LightRepository.updateState<TodosAggregate, TodoEvent>

            Expect.isOk result "should be ok"

            let result = eventStoreApp.GetAllTodos() |> Result.get
            Expect.equal result [todo] "should be equal"

        testCase "add a todo with an invalid tag - Ko" <| fun _ ->
            let _ = SetUp()

            let todos = eventStoreApp.GetAllTodos()  |> Result.get
            Expect.equal todos [] "should be equal" 

            let todo: Todo = {Id = Guid.NewGuid(); Description = "desc1"; TagIds = [Guid.NewGuid()]; CategoryIds = []}
            let added = eventStoreApp.AddTodo todo
            Expect.isError added "should be error"

            let todos = eventStoreApp.GetAllTodos()  |> Result.get
            Expect.equal todos [] "should be equal"

        testCase "add and remove a todo - Ok" <| fun _ ->
            let _ = SetUp()
            let todos = eventStoreApp.GetAllTodos() |> Result.get
            Expect.equal todos [] "should be equal" 

            let todo: Todo = {Id = Guid.NewGuid(); Description = "descQQ"; TagIds = []; CategoryIds = []}
            let result = eventStoreApp.AddTodo todo
            eventStoreBridge |> LightRepository.updateState<TodosAggregate, TodoEvent>
            eventStoreBridge |> LightRepository.updateState<TodosAggregate', TodoEvent'>
            Expect.isOk result "should be ok"
            async {
                do! Async.Sleep 20
                return ()
            } |> Async.RunSynchronously

            eventStoreBridge |> LightRepository.updateState<TodosAggregate, TodoEvent>
            eventStoreBridge |> LightRepository.updateState<TodosAggregate', TodoEvent'>

            let todos = eventStoreApp.GetAllTodos()  |> Result.get
            Expect.equal todos [todo] "should be equal"
            let removed = eventStoreApp.RemoveTodo todo.Id
            Expect.isOk removed "should be ok"
            eventStoreBridge |> LightRepository.updateState<TodosAggregate, TodoEvent>
            eventStoreBridge |> LightRepository.updateState<TodosAggregate', TodoEvent'>
            async {
                do! Async.Sleep 10
                return ()
            } |> Async.RunSynchronously

            let result = eventStoreApp.GetAllTodos()  |> Result.get
            Expect.equal result [] "should be equal"

        testCase "add a tag and retrieve it - ok" <| fun _ ->
            let _ = SetUp()
            let tag = {Id = Guid.NewGuid(); Name = "tag1"; Color = Color.Blue}     
            let added = eventStoreApp.AddTag tag
            Expect.isOk added "should be ok"
            async {
                do! Async.Sleep 20
                return ()
            } 
            |> Async.RunSynchronously
            eventStoreBridge |> LightRepository.updateState<TagsAggregate, TagEvent> 
            let result = eventStoreApp.GetAllTags()    
            Expect.isOk result "should be ok"
            Expect.equal (result |> Result.get) [tag] "should be equal"

        testCase "add a category and retrieve it - ok" <| fun _ ->
            let _ = SetUp()
            let category: Category = {Id = System.Guid.NewGuid(); Name = "cat1"}
            let added = eventStoreApp.AddCategory category
            Expect.isOk added "should be ok"
            eventStoreBridge |> LightRepository.updateState<CategoriesAggregate, CategoryEvent> 
            eventStoreBridge |> LightRepository.updateState<TodosAggregate, TodoEvent> 
            let result = eventStoreApp.GetAllCategories()    
            Expect.isOk result "should be ok"
            Expect.equal (result |> Result.get) [category] "should be equal"

        testCase "add a category and remove it - ok" <| fun _ ->
            let _ = SetUp()
            let id = Guid.NewGuid()
            let category: Category = {Id = id; Name = "cat1"}
            let _ = eventStoreApp.AddCategory category
            eventStoreBridge |> LightRepository.updateState<CategoriesAggregate, CategoryEvent> 
            eventStoreBridge |> LightRepository.updateState<TodosAggregate, TodoEvent> 
            let removed = eventStoreApp.RemoveCategory id
            eventStoreBridge |> LightRepository.updateState<CategoriesAggregate, CategoryEvent> 
            eventStoreBridge |> LightRepository.updateState<TodosAggregate, TodoEvent> 
            Expect.isOk removed "should be ok"
            let result = eventStoreApp.GetAllCategories() |> Result.get
            Expect.equal result [] "should be equal"

        testCase "add two todos, one has an unexisting category - Ko" <| fun _ ->
            let _ = SetUp()
            let todo1 = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = [Guid.NewGuid()]; TagIds = [] }
            let todo2 = { Id = Guid.NewGuid(); Description = "test2"; CategoryIds = []; TagIds = [] }
            let result = eventStoreApp.Add2Todos (todo1, todo2)
            Expect.isError result "should be error"
            let todos = eventStoreApp.GetAllTodos().OkValue
            Expect.equal todos [] "should be equal"

        testCase "add two todos, one has an unexisting tag - KO" <| fun _ ->
            let _ = SetUp()
            let todo1 = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [] }
            let todo2 = { Id = Guid.NewGuid(); Description = "test2"; CategoryIds = []; TagIds = [Guid.NewGuid()] }
            let triedAdding = eventStoreApp.Add2Todos (todo1, todo2)
            Expect.isError triedAdding "should be error"
            let todos = eventStoreApp.GetAllTodos() |> Result.get
            Expect.equal todos [] "should be equal"

        testCase "when remove a tag then all the references to that tag are also removed from any todos - OK" <| fun _ ->
            let _ = SetUp()

            let id1 = Guid.NewGuid()
            let id2 = Guid.NewGuid()

            let tag = { Id = id2; Name = "test"; Color = Color.Blue }
            let result = eventStoreApp.AddTag tag
            Expect.isOk result "should be ok"

            async {
                do! Async.Sleep 20
                return ()
            } |> Async.RunSynchronously
            eventStoreBridge |> LightRepository.updateState<TagsAggregate, TagEvent> 

            let todo = { Id = id1; Description = "test"; CategoryIds = []; TagIds = [id2] }
            let result = eventStoreApp.AddTodo todo
            eventStoreBridge |> LightRepository.updateState<TodosAggregate, TodoEvent> 
            eventStoreBridge |> LightRepository.updateState<TodosAggregate', TodoEvent'> 
            async {
                do! Async.Sleep 20
                return ()
            } |> Async.RunSynchronously
            eventStoreBridge |> LightRepository.updateState<TodosAggregate, TodoEvent> 
            eventStoreBridge |> LightRepository.updateState<TodosAggregate', TodoEvent'> 
            Expect.isOk result "should be ok"

            let todos = eventStoreApp.GetAllTodos().OkValue
            Expect.equal todos [todo] "should be equal"

            let result = eventStoreApp.RemoveTag id2
            eventStoreBridge |> LightRepository.updateState<TagsAggregate, TagEvent> 
            Expect.isOk result "should be ok"
            let todos = eventStoreApp.GetAllTodos().OkValue
            Expect.equal (todos.Head.TagIds) [] "should be equal"

        testCase "will not remove the tag if its ref can't be removed in todos that contains them - OK" <| fun _ ->
            let _ = SetUp()

            let id1 = Guid.NewGuid()
            let id2 = Guid.NewGuid()

            let tag = { Id = id2; Name = "test"; Color = Color.Blue }
            let result = eventStoreApp.AddTag tag
            Expect.isOk result "should be ok"
            async {
                do! Async.Sleep 20
                return ()
            } |> Async.RunSynchronously
            eventStoreBridge |> LightRepository.updateState<TagsAggregate, TagEvent> 

            let tags = eventStoreApp.GetAllTags().OkValue
            Expect.equal tags [tag] "should be equal"

            let todo = { Id = id1; Description = "test"; CategoryIds = []; TagIds = [id2] }
            let result = eventStoreApp.AddTodo todo
            Expect.isOk result "should be ok"
            eventStoreBridge |> LightRepository.updateState<TodosAggregate, TodoEvent> 
            eventStoreBridge |> LightRepository.updateState<TodosAggregate', TodoEvent'> 
            async {
                do! Async.Sleep 30
                return ()
            } |> Async.RunSynchronously

            let todos = eventStoreApp.GetAllTodos().OkValue
            Expect.equal todos [todo] "should be equal"

            // fake impl of runtwocommands where the second command fails so no tag is removed either
            let result = eventStoreApp.RemoveTagFakingErrorOnSecondCommand id2
            let tags = eventStoreApp.GetAllTags().OkValue
            Expect.equal tags [tag] "should be equal"

        testCase "remove an unexisting todo - KO" <| fun _ ->
            let _ = SetUp()
            let newGuid = Guid.NewGuid()
            let result = eventStoreApp.RemoveTodo newGuid
            Expect.isError result "should be error"
            let errMsg = result |> getError
            Expect.equal errMsg (sprintf "A Todo with id '%A' does not exist" newGuid) "should be equal"

        testCase "add a category and remove it - Ko" <| fun _ ->
            let _ = SetUp()
            let id = Guid.NewGuid()
            let category: Category = {Id = id; Name = "cat1"}
            let _ = eventStoreApp.AddCategory category
            eventStoreBridge |> LightRepository.updateState<CategoriesAggregate, CategoryEvent> 
            eventStoreBridge |> LightRepository.updateState<TodosAggregate, TodoEvent> 
            let removed = eventStoreApp.RemoveCategory id
            eventStoreBridge |> LightRepository.updateState<CategoriesAggregate, CategoryEvent> 
            eventStoreBridge |> LightRepository.updateState<TodosAggregate, TodoEvent> 
            Expect.isOk removed "should be ok"
            let result = eventStoreApp.GetAllCategories() |> Result.get
            Expect.equal result [] "should be equal"

        testCase "when remove a category then all the references to that category are also removed from any todos - OK" <| fun _ ->
            let _ = SetUp()
            let id1 = Guid.NewGuid()
            let id2 = Guid.NewGuid()

            let category = { Id = id2; Name = "test" }
            let added = eventStoreApp.AddCategory category
            Expect.isOk added "should be ok"

            eventStoreBridge |> LightRepository.updateState<CategoriesAggregate, CategoryEvent> 
            eventStoreBridge |> LightRepository.updateState<TodosAggregate, TodoEvent> 
            let todo = { Id = id1; Description = "test"; CategoryIds = [id2]; TagIds = [] }
            let result = eventStoreApp.AddTodo todo
            Expect.isOk result "should be ok"

            let todos = eventStoreApp.GetAllTodos().OkValue
            Expect.equal todos [todo] "should be equal"

            let removed = eventStoreApp.RemoveCategory id2
            Expect.isOk removed "should be ok"
            let result = eventStoreApp.GetAllTodos().OkValue
            Expect.equal (result.Head.CategoryIds) [] "should be equal"
    ] 
    |> testSequenced