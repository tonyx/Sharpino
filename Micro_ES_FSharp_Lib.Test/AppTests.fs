module Tests.Tonyx.EventSourcing.Sample.App

open Expecto
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

let db = Repository.storage

let setUp() =
    db.Reset "_01" TodosAggregate.StorageName 
    Cache.EventCache<TodosAggregate>.Instance.Clear()
    Cache.SnapCache<TodosAggregate>.Instance.Clear()

    db.Reset "_01" TagsAggregate.StorageName
    Cache.EventCache<TagsAggregate>.Instance.Clear()
    Cache.SnapCache<TagsAggregate>.Instance.Clear()

[<Tests>]
let appTests =
    testList "App test" [
        testCase "add todo - Ok" <| fun _ ->
            let _ = setUp()
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [] }
            let result = App.addTodo todo
            Expect.isOk result "should be ok"
            let todos = App.getAllTodos().OkValue 
            Expect.equal todos [todo] "should be equal"

        testCase "add two todos - Ok" <| fun _ -> // this is for checking the case of a command returning two events
            let _ = setUp()
            let todo1 = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [] }
            let todo2 = { Id = Guid.NewGuid(); Description = "test2"; CategoryIds = []; TagIds = [] }
            let result = App.add2Todos (todo1, todo2)
            Expect.isOk result "should be ok"
            let todos = App.getAllTodos().OkValue 
            Expect.equal (todos |> Set.ofList) ([todo1; todo2] |> Set.ofList)  "should be equal"

        testCase "add two todos, one has an unexisting category - Ko" <| fun _ -> // this is for checking the case of a command returning two events
            let _ = setUp()
            let todo1 = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = [Guid.NewGuid()]; TagIds = [] }
            let todo2 = { Id = Guid.NewGuid(); Description = "test2"; CategoryIds = []; TagIds = [] }
            let result = App.add2Todos (todo1, todo2)
            Expect.isError result "should be error"

            let todos = App.getAllTodos().OkValue 
            Expect.equal todos [] "should be equal"

        testCase "add two todos, one has an unexisting tag - Ko" <| fun _ -> // this is for checking the case of a command returning two events
            let _ = setUp()
            let todo1 = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [] }
            let todo2 = { Id = Guid.NewGuid(); Description = "test2"; CategoryIds = []; TagIds = [Guid.NewGuid()] }
            let result = App.add2Todos (todo1, todo2)
            Expect.isError result "should be error"
            let todos = App.getAllTodos().OkValue 
            Expect.equal todos [] "should be equal"

        testCase "add a todo with an unexisting tag - Ok" <| fun _ ->
            let _ = setUp()
            let id1 = Guid.NewGuid()
            let id2 = Guid.NewGuid()
            let todo = { Id = id1; Description = "test"; CategoryIds = []; TagIds = [id2] }
            let result = App.addTodo todo
            Expect.isError result "should be error"

        testCase "when remove a tag then all the reference to that tag are also removed from any todos - Ok" <| fun _ ->
            let _ = setUp()
            let id1 = Guid.NewGuid()
            let id2 = Guid.NewGuid()
            let tag = { Id = id2; Name = "test"; Color = Color.Blue }
            let result = App.addTag tag
            Expect.isOk result "should be ok"

            let todo = { Id = id1; Description = "test"; CategoryIds = []; TagIds = [id2] }
            let result = App.addTodo todo
            Expect.isOk result "should be ok"
            let todos = App.getAllTodos().OkValue
            Expect.equal todos [todo] "should be equal"
            let result = App.removeTag id2
            Expect.isOk result "should be ok"
            let todos = App.getAllTodos().OkValue
            Expect.isTrue (todos.Head.TagIds |> List.isEmpty) "should be true"

        testCase "add and remove a todo - Ok" <| fun _ ->
            let _ = setUp()
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [] }
            let result = App.addTodo todo
            Expect.isOk result "should be ok"
            let todos = App.getAllTodos() |> Result.get
            Expect.equal todos [todo] "should be equal"
            let result = App.removeTodo todo.Id
            Expect.isOk result "should be ok"
            let todos = App.getAllTodos() |> Result.get
            Expect.equal todos [] "should be equal"

        testCase "remove an unexisting todo - Ko" <| fun _ ->
            let _ = setUp()
            let newGuid = Guid.NewGuid()
            let result = App.removeTodo newGuid
            Expect.isError result "should be error"
            let errMsg = result |> getError
            Expect.equal errMsg (sprintf "A Todo with id '%A' does not exist" newGuid) "should be equal"

        testCase "add category" <| fun _ ->
            let _ = setUp()
            let category = { Id = Guid.NewGuid(); Name = "test"}
            let result = App.addCategory category
            Expect.isOk result "should be ok"
            let categories = App.getAllCategories() |> Result.get
            Expect.equal categories [category] "should be equal"

        testCase "add and remove a category" <| fun _ ->
            let _ = setUp()
            let category = { Id = Guid.NewGuid(); Name = "test"}
            let result = App.addCategory category
            Expect.isOk result "should be ok"
            let categories = App.getAllCategories() |> Result.get
            Expect.equal categories [category] "should be equal"
            let result = App.removeCategory category.Id
            Expect.isOk result "should be ok"
            let categories = App.getAllCategories() |> Result.get
            Expect.equal categories [] "should be equal"

        testCase "add a todo with an unexisting category - KO" <| fun _ ->
            let _ = setUp()
            let category = { Id = Guid.NewGuid(); Name = "test"}
            let result = App.addCategory category
            Expect.isOk result "should be ok"
            let category' = App.getAllCategories() |> Result.get
            Expect.equal category' [category] "should be equal"
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = [Guid.NewGuid()]; TagIds = [] }
            let result = App.addTodo todo
            Expect.isError result "should be error"

        testCase "when remove a category all references to it should be removed from todos - Ok" <| fun _ ->
            let _ = setUp()
            let categoryId = Guid.NewGuid()
            let category = { Id = categoryId; Name = "test" }
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = [categoryId]; TagIds = [] }

            let added =
                ceResult {
                    let! _ = App.addCategory category
                    let! app' = App.addTodo todo
                    return app'
                } 
            Expect.isOk added "should be ok"

            let todos = App.getAllTodos().OkValue 
            Expect.equal todos [todo] "should be equal"
            let result = App.removeCategory categoryId
            Expect.isOk result "should be ok"

            let todos = App.getAllTodos().OkValue 
            Expect.equal (todos |> List.head).CategoryIds [] "should be equal"

        testCase "when remove a category all references to it should be removed from todos 2 - Ok" <| fun _ ->
            let _ = setUp()
            let categoryId1 = Guid.NewGuid()
            let categoryId2 = Guid.NewGuid()
            let category = { Id = categoryId1; Name = "test" }
            let category2 = { Id = categoryId2; Name = "test2" }
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = [categoryId1; categoryId2]; TagIds = [] }

            let _ = App.addCategory category
            let _ = App.addCategory category2
            let app' = App.addTodo todo
            Expect.isOk app' "should be ok"

            let todos = App.getAllTodos().OkValue 
            Expect.equal todos [todo] "should be equal"
            let result = App.removeCategory categoryId1
            Expect.isOk result "should be ok"

            let todos = App.getAllTodos().OkValue 
            Expect.equal (todos |> List.head).CategoryIds [categoryId2] "should be equal"

        testCase "add tag" <| fun _ ->
            let _ = setUp()
            let tag = { Id = Guid.NewGuid(); Name = "test"; Color = Color.Blue }
            let result = App.addTag tag
            Expect.isOk result "should be ok"
            let tags = App.getAllTags() |> Result.get
            Expect.equal tags [tag] "should be equal"

        testCase "add and remove a tag" <| fun _ ->
            let _ = setUp()
            let tag = { Id = Guid.NewGuid(); Name = "test"; Color = Color.Blue }
            let result = App.addTag tag
            Expect.isOk result "should be ok"
            let tags = App.getAllTags() |> Result.get
            Expect.equal tags [tag] "should be equal"
            let result = App.removeTag tag.Id
            Expect.isOk result "should be ok"
            let tags = App.getAllTags() |> Result.get
            Expect.equal tags [] "should be equal"

        testCase "when remove a tag all references to it should be removed from existing todos - Ok" <| fun _ ->
            let _ = setUp()
            let tagId = Guid.NewGuid()
            let tag = { Id = tagId; Name = "test"; Color = Color.Blue }
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [tagId] }

            let added =
                ceResult {
                    let! _ = App.addTag tag
                    let! app' = App.addTodo todo
                    return app'

                } 
            Expect.isOk added "should be ok"

            let todos = App.getAllTodos().OkValue 
            Expect.equal todos [todo] "should be equal"
            let result = App.removeTag tagId
            Expect.isOk result "should be ok"

            let todos = App.getAllTodos().OkValue 
            Expect.equal (todos |> List.head).TagIds [] "should be equal"

        testCase "when remove a tag all references to it should be removed from existing todos 2 - Ok" <| fun _ ->
            let _ = setUp()
            let tagId = Guid.NewGuid()
            let tag1 = { Id = tagId; Name = "test"; Color = Color.Blue }
            let tagId2 = Guid.NewGuid()
            let tag2 = { Id = tagId2; Name = "test2"; Color = Color.Red }
            let todo = { Id = Guid.NewGuid(); Description = "test"; CategoryIds = []; TagIds = [tagId; tagId2] }

            let _ = App.addTag tag1
            let _ = App.addTag tag2
            let _ = App.addTodo todo

            let todos = App.getAllTodos().OkValue 
            Expect.equal todos [todo] "should be equal"
            let result = App.removeTag tagId
            Expect.isOk result "should be ok"

            let todos = App.getAllTodos().OkValue 
            Expect.equal (todos |> List.head).TagIds [tagId2] "should be equal"
    ] |> testSequenced



