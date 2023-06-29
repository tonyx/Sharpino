
module Tests.Sharpino.Sample.DbStorageTest

open Expecto
open FsCheck
open FsCheck.Prop
open Expecto.Tests
open System
open FSharp.Core

open Sharpino
open Sharpino.Lib.EvStore
open Sharpino.Sample.EventStoreApp
open Sharpino.Sample.Models.TagsModel
open Sharpino.Sample.Models.TodosModel
open Sharpino.Sample.Models.CategoriesModel
open Sharpino.Sample.TagsAggregate
open Sharpino.Sample.TodosAggregate
open Sharpino.Sample.CategoriesAggregate

[<Tests>]
let utilsTests =
    let SetUp() =
        Cache.CurrentState<_>.Instance.Clear()
        Cache.CurrentState<TodosAggregate>.Instance.Clear()
        Cache.CurrentState<TodosAggregate'>.Instance.Clear()
        Cache.CurrentState<TagsAggregate>.Instance.Clear()
        Cache.CurrentState<CategoriesAggregate>.Instance.Clear()

        let eventStore = EventStoreBridge()
        async {
            let result = eventStore.Reset("_01", "_tags")
            let result = eventStore.Reset("_01", "_todo")
            let result = eventStore.Reset("_02", "_todo")
            let result = eventStore.Reset("_01", "_categories")
            return result
        }
        |> Async.RunSynchronously

    ftestList "dbstorage spike" [
        testCase "add a tag and then verify it is present - ok" <| fun _ ->
            let _ = SetUp()
            let eventStore = EventStoreBridge()
            let eventStoreApp = EventStoreApp(eventStore)
            let tag = {Id = System.Guid.NewGuid(); Name = "tag1"; Color = Color.Green}     
            let result = eventStoreApp.AddTag tag

            Expect.isTrue true "true"
            let tags = eventStoreApp.GetAllTags()  |> Result.get 
            Expect.equal tags [tag] "should be equal"

        testCase "add a todo - ok" <| fun _ ->
            let _ = SetUp()
            let eventStore = EventStoreBridge()
            let eventStoreApp = EventStoreApp(eventStore)
            let todo: Todo = {Id = System.Guid.NewGuid(); Description = "descZ"; TagIds = []; CategoryIds = []}

            let result = eventStoreApp.AddTodo todo 

            Expect.isOk result "should be ok"

            let todos = eventStoreApp.GetAllTodos() |> Result.get
            Expect.equal todos [todo] "should be equal"

        testCase "add a todo with an invalid tag - Ko" <| fun _ ->
            let _ = SetUp()

            let eventStore = EventStoreBridge()
            let eventStoreApp = EventStoreApp(eventStore)
            let todos = eventStoreApp.GetAllTodos()  |> Result.get
            Expect.equal todos [] "should be equal" 

            let todo: Todo = {Id = System.Guid.NewGuid(); Description = "desc1"; TagIds = [Guid.NewGuid()]; CategoryIds = []}
            let result = eventStoreApp.AddTodo todo
            Expect.isError result "should be error"
            printf "result: %A\n" result

            let todos = eventStoreApp.GetAllTodos()  |> Result.get
            Expect.equal todos [] "should be equal"

        testCase "add and remove a todo - Ok" <| fun _ ->
            let _ = SetUp()
            let eventStore = EventStoreBridge()
            let eventStoreApp = EventStoreApp(eventStore)
            let todos = eventStoreApp.GetAllTodos()  |> Result.get
            Expect.equal todos [] "should be equal" 
            let id = Guid.NewGuid()
            let todo: Todo = {Id = System.Guid.NewGuid(); Description = "descQQ"; TagIds = []; CategoryIds = []}
            let result = eventStoreApp.AddTodo todo
            let todos = eventStoreApp.GetAllTodos()  |> Result.get
            Expect.equal todos [todo] "should be equal"
            let result = eventStoreApp.RemoveTodo todo.Id

            let todos = eventStoreApp.GetAllTodos()  |> Result.get
            Expect.equal todos [] "should be equal"

        testCase "add a tag and retrieve it - ok" <| fun _ ->
            let deleted = SetUp()
            let eventStore = EventStoreBridge()
            let eventStoreApp = EventStoreApp(eventStore)
            let tag = {Id = System.Guid.NewGuid(); Name = "tag1"; Color = Color.Blue}     
            let result = eventStoreApp.AddTag tag
            Expect.isTrue true "true"
            let result = eventStoreApp.GetAllTags()    
            Expect.isOk result "should be ok"
            Expect.equal (result |> Result.get) [tag] "should be equal"

        testCase "add a category and retrieve it - ok" <| fun _ ->
            let deleted = SetUp()
            let eventStore = EventStoreBridge()
            let eventStoreApp = EventStoreApp(eventStore)
            let category: Category = {Id = System.Guid.NewGuid(); Name = "cat1"}
            let result = eventStoreApp.AddCategory category
            let result = eventStoreApp.GetAllCategories()    
            Expect.isOk result "should be ok"
            Expect.equal (result |> Result.get) [category] "should be equal"

        testCase "add a category and remove it - ok" <| fun _ ->
            let _ = SetUp()
            let eventStore = EventStoreBridge()
            let eventStoreApp = EventStoreApp(eventStore)
            let id = Guid.NewGuid()
            let category: Category = {Id = id; Name = "cat1"}
            let _ = eventStoreApp.AddCategory category
            let removed = eventStoreApp.RemoveCategory id
            Expect.isOk removed "should be ok"
            let result = eventStoreApp.GetAllCategories() |> Result.get
            Expect.equal result [] "should be equal"



            


    ] 
    |> testSequenced