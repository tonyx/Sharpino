
namespace Sharpino.EventSourcing.Sample
open Sharpino
open Sharpino.Utils

open Sharpino.Sample.Models.TodosModel
open Sharpino.Sample.Models.CategoriesModel
open Sharpino.Sample.Models.TagsModel
open Sharpino.Sample

open System

module AppVersions =
    // beware that this is the test db and so we can reset it for testing
    // this should never be done in production
    let connection = 
        "Server=127.0.0.1;"+
        "Database=es_01;" +
        "User Id=safe;"+
        "Password=safe;"
    let pgStorage: IStorage = DbStorage.PgDb(connection)
    let memStorage: IStorage = MemoryStorage.MemoryStorage()
    let currentPgApp = App.CurrentVersionApp(pgStorage)
    let upgradedPgApp = App.UpgradedApp(pgStorage)
    let currentMemApp = App.CurrentVersionApp(memStorage)
    let upgradedMemApp = App.UpgradedApp(memStorage)

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

    [<CurrentVersion>]
    let currentPostgresApp =
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

    [<UpgradedVersion>]
    let upgradedPostgresApp =
        {
            _storage =          pgStorage
            _migrator  =        None
            getAllTodos =       upgradedPgApp.GetAllTodos
            addTodo =           upgradedPgApp.AddTodo
            add2Todos =         upgradedPgApp.Add2Todos
            removeTodo =        upgradedPgApp.RemoveTodo
            getAllCategories =  upgradedPgApp.GetAllCategories
            addCategory =       upgradedPgApp.AddCategory
            removeCategory =    upgradedPgApp.RemoveCategory
            addTag =            upgradedPgApp.AddTag
            removeTag =         upgradedPgApp.removeTag
            getAllTags =        upgradedPgApp.GetAllTags
        }


    [<CurrentVersion>]
    let currentMemoryApp =
        {
            _migrator  =        currentMemApp.Migrate |> Some
            _storage =          memStorage 
            getAllTodos =       currentMemApp.GetAllTodos
            addTodo =           currentMemApp.AddTodo
            add2Todos =         currentMemApp.Add2Todos
            removeTodo =        currentMemApp.RemoveTodo
            getAllCategories =  currentMemApp.GetAllCategories
            addCategory =       currentMemApp.AddCategory
            removeCategory =    currentMemApp.RemoveCategory
            addTag =            currentMemApp.AddTag 
            removeTag =         currentMemApp.RemoveTag
            getAllTags =        currentMemApp.GetAllTags
        }

    [<UpgradedVersion>]
    let upgradedMemoryApp =
        {
            _migrator =         None
            _storage =          memStorage 
            getAllTodos =       upgradedMemApp.GetAllTodos
            addTodo =           upgradedMemApp.AddTodo
            add2Todos =         upgradedMemApp.Add2Todos
            removeTodo =        upgradedMemApp.RemoveTodo
            getAllCategories =  upgradedMemApp.GetAllCategories
            addCategory =       upgradedMemApp.AddCategory
            removeCategory =    upgradedMemApp.RemoveCategory
            addTag =            upgradedMemApp.AddTag
            removeTag =         upgradedMemApp.removeTag
            getAllTags =        upgradedMemApp.GetAllTags
        }