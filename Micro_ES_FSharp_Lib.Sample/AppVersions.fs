
namespace Tonyx.EventSourcing.Sample
open Tonyx.EventSourcing
open Tonyx.EventSourcing.Utils

open Tonyx.EventSourcing.Sample.TodosAggregate
open Tonyx.EventSourcing.Sample.Todos.TodoEvents
open Tonyx.EventSourcing.Sample.Todos.TodoCommands
open Tonyx.EventSourcing.Sample.Todos.Models.TodosModel
open Tonyx.EventSourcing.Sample.Todos.Models.CategoriesModel

open Tonyx.EventSourcing.Sample.TagsAggregate
open Tonyx.EventSourcing.Sample.Tags.TagsEvents
open Tonyx.EventSourcing.Sample.Tags.TagCommands
open Tonyx.EventSourcing.Sample.Tags.Models.TagsModel

open Tonyx.EventSourcing.Sample
open Tonyx.EventSourcing.Sample.Categories
open Tonyx.EventSourcing.Sample.CategoriesAggregate
open Tonyx.EventSourcing.Sample.Categories.CategoriesCommands
open Tonyx.EventSourcing.Sample.Categories.CategoriesEvents
open System
open FSharpPlus

module AppVersions =

    type IApplication =
        {
            _storage:           IStorage
            _migrator:          Option<unit -> Result<unit, string>>
            getAllTodos:        unit -> Result<List<Todo>, string>
            addTodo:            Todo -> Result<unit, string>
            add2Todos:          Todo * Todo -> Result<unit, string>
            removeTodo:         Guid -> Result<unit, string>

            getAllCategories:   unit -> Result<List<Category>, string> 
            addCategory:        Category -> Result<unit, string>
            removeCategory:     Guid -> Result<unit, string>
            addTag:             Tag -> Result<unit, string>
            removeTag:          Guid -> Result<unit, string>
            getAllTags:         unit -> Result<List<Tag>, string>
        }

    let pgStorage: IStorage = DbStorage.PgDb()
    let pgApp = App.App(pgStorage)

    let applicationPostgresStorage =
        {
            _storage =          pgStorage
            _migrator  =        pgApp.migrate |> Some
            getAllTodos =       pgApp.getAllTodos
            addTodo =           pgApp.addTodo
            add2Todos =         pgApp.add2Todos
            removeTodo =        pgApp.removeTodo
            getAllCategories =  pgApp.getAllCategories
            addCategory =       pgApp.addCategory
            removeCategory =    pgApp.removeCategory
            addTag =            pgApp.addTag 
            removeTag =         pgApp.removeTag
            getAllTags =        pgApp.getAllTags
        }
    let applicationShadowPostgresStorage =
        {
            _storage =          pgStorage
            _migrator  =        None
            getAllTodos =       pgApp.getAllTodos'
            addTodo =           pgApp.addTodo'
            add2Todos =         pgApp.add2Todos'
            removeTodo =        pgApp.removeTodo'
            getAllCategories =  pgApp.getAllCategories'
            addCategory =       pgApp.addCategory'
            removeCategory =    pgApp.removeCategory'
            addTag =            pgApp.addTag 
            removeTag =         pgApp.removeTag'
            getAllTags =        pgApp.getAllTags
        }

    let memStorage: IStorage = MemoryStorage.MemoryStorage()
    let applicationMemoryStorage =
        let app = App.App(memStorage)
        {
            _migrator  =        app.migrate |> Some
            _storage =          memStorage 
            getAllTodos =       app.getAllTodos
            addTodo =           app.addTodo
            add2Todos =         app.add2Todos
            removeTodo =        app.removeTodo
            getAllCategories =  app.getAllCategories
            addCategory =       app.addCategory
            removeCategory =    app.removeCategory
            addTag =            app.addTag 
            removeTag =         app.removeTag
            getAllTags =        app.getAllTags
        }
    let applicationShadowMemoryStorage =
        let app = App.App(memStorage)
        {
            _migrator =         None
            _storage =          memStorage 
            getAllTodos =       app.getAllTodos'
            addTodo =           app.addTodo'
            add2Todos =         app.add2Todos'
            removeTodo =        app.removeTodo'
            getAllCategories =  app.getAllCategories'
            addCategory =       app.addCategory'
            removeCategory =    app.removeCategory'
            addTag =            app.addTag 
            removeTag =         app.removeTag'
            getAllTags =        app.getAllTags
        }
    ()