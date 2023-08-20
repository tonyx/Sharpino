
module Tests.Sharpino.Sample.DbStorageTest

open Expecto
open System
open FSharp.Core

open Sharpino
open Sharpino.Storage
open Sharpino.Utils
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
    let eventStoreConnection = "esdb://localhost:2113?tls=false"
    let eventStoreBridge = Sharpino.EventStore.EventStoreBridgeFS(eventStoreConnection) :> ILightStorage
    let SetUp() =
        
        Cache.CurrentStateRef<_>.Instance.Clear()
        Cache.CurrentStateRef<TodosAggregate>.Instance.Clear()
        Cache.CurrentStateRef<TodosAggregate'>.Instance.Clear()
        Cache.CurrentStateRef<TagsAggregate>.Instance.Clear()
        Cache.CurrentStateRef<CategoriesAggregate>.Instance.Clear()

        eventStoreBridge.ResetSnapshots "_01" "_tags"         //    |> Async.AwaitTask      
        eventStoreBridge.ResetEvents "_01" "_tags"
        eventStoreBridge.ResetSnapshots "_01" "_todo"
        eventStoreBridge.ResetEvents "_01" "_todo"
        eventStoreBridge.ResetSnapshots "_02" "_todo"
        eventStoreBridge.ResetEvents "_02" "_todo"      
        eventStoreBridge.ResetSnapshots "_01" "_categories"
        eventStoreBridge.ResetEvents "_01" "_categories"

        async {
            do! Async.Sleep 20
            return ()
        } |> Async.RunSynchronously
        

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
                do! Async.Sleep 30
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

        testCase "snapshot starts empty - Ok" <| fun _ ->
            let _ = SetUp()
            let snapshot = eventStoreBridge.GetLastSnapshot TodosAggregate.Version TodosAggregate.StorageName
            Expect.isNone snapshot "should be none"

        // annoying offset index problems on the following tests make them unpredictable
        ptestCase "add a todo and the snapshot will immediately be generated - Ok" <| fun _ ->
            let _ = SetUp()
            let snapshot = eventStoreBridge.GetLastSnapshot TodosAggregate.Version TodosAggregate.StorageName
            Expect.isNone snapshot "should be none"
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [] } 
            let result = eventStoreApp.AddTodo todo 
            let snapshot = eventStoreBridge.GetLastSnapshot TodosAggregate.Version TodosAggregate.StorageName
            Expect.isSome snapshot "should be none"

        ptestCase "add a todo and the snapshot will correspond to the current state - Ok " <| fun _ ->
            let _ = SetUp()
            let snapshot = eventStoreBridge.GetLastSnapshot TodosAggregate.Version TodosAggregate.StorageName
            Expect.isNone snapshot "should be none"
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [] } 
            let result = eventStoreApp.AddTodo todo 
            Expect.isOk result "should be ok"
            let snapshot = eventStoreBridge.GetLastSnapshot TodosAggregate.Version TodosAggregate.StorageName

            Expect.isSome snapshot "should be none"
            let (_, snapshotValue) = snapshot.Value
            let (_, state) = LightRepository.getState<TodosAggregate>()
            let stateFromSnapshot = snapshotValue |> Utils.deserialize<TodosAggregate>
            Expect.isOk stateFromSnapshot "should be ok"
            Expect.equal state stateFromSnapshot.OkValue "should be equal"

        ptestCase "add two events and the snapshot is the same as the one after the first add - Ok" <| fun _ ->
            let _ = SetUp()
            let snapshot = eventStoreBridge.GetLastSnapshot TodosAggregate.Version TodosAggregate.StorageName
            Expect.isNone snapshot "should be none"
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [] } 
            let result = eventStoreApp.AddTodo todo 
            Expect.isOk result "should be ok"
            let snapshot = eventStoreBridge.GetLastSnapshot TodosAggregate.Version TodosAggregate.StorageName
            Expect.isSome snapshot "should be none"
            let (_, snapshotValue) = snapshot.Value

            let todo2 = { Id = Guid.NewGuid(); Description = "test2"; CategoryIds = []; TagIds = [] } 
            let result = eventStoreApp.AddTodo todo2

            let newSnapshot = eventStoreBridge.GetLastSnapshot TodosAggregate.Version TodosAggregate.StorageName
            let (_, newSnapshotValue) = newSnapshot.Value
            Expect.equal snapshotValue newSnapshotValue "should be equal"

        ptestCase "add two events and the current state is different from the last snapshot - Ok" <| fun _ ->
            let _ = SetUp()
            let snapshot = eventStoreBridge.GetLastSnapshot TodosAggregate.Version TodosAggregate.StorageName
            Expect.isNone snapshot "should be none"
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [] } 
            let result = eventStoreApp.AddTodo todo 
            Expect.isOk result "should be ok"
            let snapshot = eventStoreBridge.GetLastSnapshot TodosAggregate.Version TodosAggregate.StorageName
            Expect.isSome snapshot "should be none"
            let (_, snapshotValue) = snapshot.Value

            let todo2 = { Id = Guid.NewGuid(); Description = "test2"; CategoryIds = []; TagIds = [] } 
            let result = eventStoreApp.AddTodo todo2

            let newSnapshot = eventStoreBridge.GetLastSnapshot TodosAggregate.Version TodosAggregate.StorageName
            let (_, newSnapshotValue) = newSnapshot.Value

            let (_, state) = LightRepository.getState<TodosAggregate>()
            let stateFromSnapshot = newSnapshotValue |> Utils.deserialize<TodosAggregate>
            Expect.notEqual state stateFromSnapshot.OkValue "should be equal"

        ptestCase "add three events and the current state is different from the last snapshot - Ok" <| fun _ ->
            let _ = SetUp()
            let snapshot = eventStoreBridge.GetLastSnapshot TodosAggregate.Version TodosAggregate.StorageName
            Expect.isNone snapshot "should be none"
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [] } 
            let result = eventStoreApp.AddTodo todo 
            Expect.isOk result "should be ok"
            let todo2 = { Id = Guid.NewGuid(); Description = "test2"; CategoryIds = []; TagIds = [] } 
            let todoAdded = eventStoreApp.AddTodo todo2
            Expect.isOk todoAdded "should be ok"
            let todo3 = { Id = Guid.NewGuid(); Description = "test3"; CategoryIds = []; TagIds = [] }
            let todoAdded3 = eventStoreApp.AddTodo todo3

            let snapshot = eventStoreBridge.GetLastSnapshot TodosAggregate.Version TodosAggregate.StorageName
            let (_, snapshotValue) = snapshot.Value
            Expect.isSome snapshot "should be none"
            Expect.isOk todoAdded "should be ok"
            let (_, state) = LightRepository.getState<TodosAggregate>()
            let stateFromSnapshot = snapshotValue |> Utils.deserialize<TodosAggregate>
            Expect.notEqual state stateFromSnapshot.OkValue "should be equal"

        ptestCase "add four events and the current state is not equal to the last snapshot - Ok" <| fun _ ->
            let _ = SetUp()
            let snapshot = eventStoreBridge.GetLastSnapshot TodosAggregate.Version TodosAggregate.StorageName
            Expect.isNone snapshot "should be none"
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [] } 
            let result = eventStoreApp.AddTodo todo 
            Expect.isOk result "should be ok"

            let todo2 = { Id = Guid.NewGuid(); Description = "test2"; CategoryIds = []; TagIds = [] } 
            let todoAdded = eventStoreApp.AddTodo todo2
            Expect.isOk todoAdded "should be ok"

            let todo3 = { Id = Guid.NewGuid(); Description = "test3"; CategoryIds = []; TagIds = [] }
            let todoAdded3 = eventStoreApp.AddTodo todo3
            Expect.isOk todoAdded3 "should be ok"

            let todo4 = { Id = Guid.NewGuid(); Description = "test4"; CategoryIds = []; TagIds = [] }
            let todoAdded4 = eventStoreApp.AddTodo todo4
            Expect.isOk todoAdded4 "should be ok"

            let snapshot = eventStoreBridge.GetLastSnapshot TodosAggregate.Version TodosAggregate.StorageName
            let (_, snapshotValue) = snapshot.Value
            let snapshotState = snapshotValue |> Utils.deserialize<TodosAggregate>
            Expect.isOk snapshotState "should be ok"
            let snapshotStateValue = snapshotState.OkValue

            let (_, state) = LightRepository.getState<TodosAggregate>()
            Expect.notEqual state snapshotStateValue "should be equal"

        ptestCase "add five events and the current state is equal to the last snapshot - Ok" <| fun _ ->
            let _ = SetUp()
            let snapshot = eventStoreBridge.GetLastSnapshot TodosAggregate.Version TodosAggregate.StorageName
            Expect.isNone snapshot "should be none"
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [] } 
            let result = eventStoreApp.AddTodo todo 
            Expect.isOk result "should be ok"

            let todo2 = { Id = Guid.NewGuid(); Description = "test2"; CategoryIds = []; TagIds = [] } 
            let todoAdded = eventStoreApp.AddTodo todo2
            Expect.isOk todoAdded "should be ok"

            let todo3 = { Id = Guid.NewGuid(); Description = "test3"; CategoryIds = []; TagIds = [] }
            let todoAdded3 = eventStoreApp.AddTodo todo3
            Expect.isOk todoAdded3 "should be ok"

            let todo4 = { Id = Guid.NewGuid(); Description = "test4"; CategoryIds = []; TagIds = [] }
            let todoAdded4 = eventStoreApp.AddTodo todo4
            Expect.isOk todoAdded4 "should be ok"

            let todo5 = { Id = Guid.NewGuid(); Description = "test5"; CategoryIds = []; TagIds = [] }
            let todoAdded5 = eventStoreApp.AddTodo todo5
            Expect.isOk todoAdded5 "should be ok"
        
            let snapshot = eventStoreBridge.GetLastSnapshot TodosAggregate.Version TodosAggregate.StorageName
            let (_, snapshotValue) = snapshot.Value
            let snapshotState = snapshotValue |> Utils.deserialize<TodosAggregate>
            Expect.isOk snapshotState "should be ok"
            let snapshotStateValue = snapshotState.OkValue

            let (_, state) = LightRepository.getState<TodosAggregate>()
            Expect.equal state snapshotStateValue "should be equal"


        // todowork in progress
        ptestCase "add six events and the current state is not equal to the last snapshot - Ok" <| fun _ ->
            let _ = SetUp()
            Expect.isTrue true "should be true"


            // let snapshot = eventStoreBridge.GetLastSnapshot TodosAggregate.Version TodosAggregate.StorageName
            // Expect.isNone snapshot "should be none"
            // let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [] } 
            // let result = eventStoreApp.AddTodo todo 
            // Expect.isOk result "should be ok"

            // let todo2 = { Id = Guid.NewGuid(); Description = "test2"; CategoryIds = []; TagIds = [] } 
            // let todoAdded = eventStoreApp.AddTodo todo2
            // Expect.isOk todoAdded "should be ok"

            // let todo3 = { Id = Guid.NewGuid(); Description = "test3"; CategoryIds = []; TagIds = [] }
            // let todoAdded3 = eventStoreApp.AddTodo todo3
            // Expect.isOk todoAdded3 "should be ok"

            // let todo4 = { Id = Guid.NewGuid(); Description = "test4"; CategoryIds = []; TagIds = [] }
            // let todoAdded4 = eventStoreApp.AddTodo todo4
            // Expect.isOk todoAdded4 "should be ok"

            // let todo5 = { Id = Guid.NewGuid(); Description = "test5"; CategoryIds = []; TagIds = [] }
            // let todoAdded5 = eventStoreApp.AddTodo todo5
            // Expect.isOk todoAdded5 "should be ok"

            // let snapshot = eventStoreBridge.GetLastSnapshot TodosAggregate.Version TodosAggregate.StorageName
            // let (_, snapshotValue) = snapshot.Value
            // let snapshotState = snapshotValue |> Utils.deserialize<TodosAggregate>
            // Expect.isOk snapshotState "should be ok"
            // let snapshotStateValue = snapshotState.OkValue

            // let (_, state) = LightRepository.getState<TodosAggregate>()
            // Expect.equal state snapshotStateValue "should be equal"




    ] 
    |> testSequenced