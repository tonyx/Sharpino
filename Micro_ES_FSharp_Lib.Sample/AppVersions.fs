
namespace Sharpino.EventSourcing.Sample
open Sharpino.EventSourcing
open Sharpino.EventSourcing.Utils

open Sharpino.EventSourcing.Sample.TodosAggregate
open Sharpino.EventSourcing.Sample.Todos.TodoEvents
open Sharpino.EventSourcing.Sample.Todos.TodoCommands
open Sharpino.EventSourcing.Sample.Todos.Models.TodosModel
open Sharpino.EventSourcing.Sample.Todos.Models.CategoriesModel

open Sharpino.EventSourcing.Sample.TagsAggregate
open Sharpino.EventSourcing.Sample.Tags.TagsEvents
open Sharpino.EventSourcing.Sample.Tags.TagCommands
open Sharpino.EventSourcing.Sample.Tags.Models.TagsModel

open Sharpino.EventSourcing.Sample
open Sharpino.EventSourcing.Sample.Categories
open Sharpino.EventSourcing.Sample.CategoriesAggregate
open Sharpino.EventSourcing.Sample.Categories.CategoriesCommands
open Sharpino.EventSourcing.Sample.Categories.CategoriesEvents
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

    let currentPgApp = App.CurrentVersionApp(pgStorage)
    [<CurrentVersion>]
    let applicationPostgresStorage =
        {
            _storage =          pgStorage
            _migrator  =        currentPgApp.migrate |> Some
            getAllTodos =       currentPgApp.getAllTodos
            addTodo =           currentPgApp.addTodo
            add2Todos =         currentPgApp.add2Todos
            removeTodo =        currentPgApp.removeTodo
            getAllCategories =  currentPgApp.getAllCategories
            addCategory =       currentPgApp.addCategory
            removeCategory =    currentPgApp.removeCategory
            addTag =            currentPgApp.addTag 
            removeTag =         currentPgApp.removeTag
            getAllTags =        currentPgApp.getAllTags
        }

    let shadowPgApp = App.UpgradedApp(pgStorage)
    [<UpgradeToVersion>]
    let applicationShadowPostgresStorage =
        {
            _storage =          pgStorage
            _migrator  =        None
            getAllTodos =       shadowPgApp.getAllTodos
            addTodo =           shadowPgApp.addTodo
            add2Todos =         shadowPgApp.add2Todos
            removeTodo =        shadowPgApp.removeTodo
            getAllCategories =  shadowPgApp.getAllCategories
            addCategory =       shadowPgApp.addCategory
            removeCategory =    shadowPgApp.removeCategory
            addTag =            shadowPgApp.addTag
            removeTag =         shadowPgApp.removeTag
            getAllTags =        shadowPgApp.getAllTags
        }

    let memStorage: IStorage = MemoryStorage.MemoryStorage()

    [<CurrentVersion>]
    let applicationMemoryStorage =
        let app = App.CurrentVersionApp(memStorage)
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

    [<UpgradeToVersion>]
    let applicationShadowMemoryStorage =
        let app = App.UpgradedApp(memStorage)
        {
            _migrator =         None
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