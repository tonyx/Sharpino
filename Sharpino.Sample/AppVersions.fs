
namespace Sharpino.EventSourcing.Sample
open Sharpino
open Sharpino.Utils

open Sharpino.Sample.Models.TodosModel
open Sharpino.Sample.Models.CategoriesModel
open Sharpino.Sample.Models.TagsModel
open Sharpino.Sample

open System

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
            _migrator  =        currentPgApp.Migrate |> Some
            getAllTodos =       currentPgApp.GetAllTodos
            addTodo =           currentPgApp.AddTodo
            add2Todos =         currentPgApp.Add2Todos
            removeTodo =        currentPgApp.RemoveTodo
            getAllCategories =  currentPgApp.GetAllCategories
            addCategory =       currentPgApp.AddCategory
            removeCategory =    currentPgApp.RemoveCategory
            addTag =            currentPgApp.AddTag 
            removeTag =         currentPgApp.RemoveTag
            getAllTags =        currentPgApp.GetAllTags
        }

    let shadowPgApp = App.UpgradedApp(pgStorage)
    [<UpgradedVersion>]
    let applicationShadowPostgresStorage =
        {
            _storage =          pgStorage
            _migrator  =        None
            getAllTodos =       shadowPgApp.GetAllTodos
            addTodo =           shadowPgApp.AddTodo
            add2Todos =         shadowPgApp.Add2Todos
            removeTodo =        shadowPgApp.RemoveTodo
            getAllCategories =  shadowPgApp.GetAllCategories
            addCategory =       shadowPgApp.AddCategory
            removeCategory =    shadowPgApp.RemoveCategory
            addTag =            shadowPgApp.AddTag
            removeTag =         shadowPgApp.removeTag
            getAllTags =        shadowPgApp.GetAllTags
        }

    let memStorage: IStorage = MemoryStorage.MemoryStorage()

    [<CurrentVersion>]
    let applicationMemoryStorage =
        let app = App.CurrentVersionApp(memStorage)
        {
            _migrator  =        app.Migrate |> Some
            _storage =          memStorage 
            getAllTodos =       app.GetAllTodos
            addTodo =           app.AddTodo
            add2Todos =         app.Add2Todos
            removeTodo =        app.RemoveTodo
            getAllCategories =  app.GetAllCategories
            addCategory =       app.AddCategory
            removeCategory =    app.RemoveCategory
            addTag =            app.AddTag 
            removeTag =         app.RemoveTag
            getAllTags =        app.GetAllTags
        }

    [<UpgradedVersion>]
    let applicationShadowMemoryStorage =
        let app = App.UpgradedApp(memStorage)
        {
            _migrator =         None
            _storage =          memStorage 
            getAllTodos =       app.GetAllTodos
            addTodo =           app.AddTodo
            add2Todos =         app.Add2Todos
            removeTodo =        app.RemoveTodo
            getAllCategories =  app.GetAllCategories
            addCategory =       app.AddCategory
            removeCategory =    app.RemoveCategory
            addTag =            app.AddTag
            removeTag =         app.removeTag
            getAllTags =        app.GetAllTags
        }