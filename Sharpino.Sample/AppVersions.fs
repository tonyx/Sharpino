
namespace Sharpino.EventSourcing.Sample
open Sharpino
open Sharpino.Utils

open Sharpino.Sample.Models.TodosModel
open Sharpino.Sample.Models.CategoriesModel
open Sharpino.Sample.Models.TagsModel
open Sharpino.Sample.TodosAggregate
open Sharpino.Sample.Models.CategoriesModel
open Sharpino.Sample.Models.TodosModel
open Sharpino.Sample.TagsAggregate
open Sharpino.Sample.Models.TagsModel
open Sharpino.Sample.CategoriesAggregate
open Sharpino.Sample.EventStoreApp
open Sharpino.Sample

open System

// todo: this is duplicated code 
type Json = string
type Name = string
type version = string

module AppVersions =
    // beware that this is the test db and so we can reset it for testing
    // this should never be done in production
    let connection = 
        "Server=127.0.0.1;"+
        "Database=es_01;" +
        "User Id=safe;"+
        "Password=safe;"
    let eventStoreConnection = "esdb://localhost:2113?tls=false"
    let pgStorage: IStorage = DbStorage.PgDb(connection)
    let memStorage: IStorage = MemoryStorage.MemoryStorage()
    let currentPgApp = App.CurrentVersionApp(pgStorage)
    let upgradedPgApp = App.UpgradedApp(pgStorage)
    let currentMemApp = App.CurrentVersionApp(memStorage)
    let upgradedMemApp = App.UpgradedApp(memStorage)

    let eventStoreBridge = Sharpino.EventStore.EventStoreBridgeFS(eventStoreConnection) 
    let evStoreApp = EventStoreApp(Sharpino.EventStore.EventStoreBridgeFS(eventStoreConnection))

    let resetDb(db: IStorage) =
        db.Reset TodosAggregate.Version TodosAggregate.StorageName 
        Cache.EventCache<TodosAggregate>.Instance.Clear()
        Cache.SnapCache<TodosAggregate>.Instance.Clear()
        Cache.StateCache<TodosAggregate>.Instance.Clear()

        db.Reset TodosAggregate'.Version TodosAggregate'.StorageName 
        Cache.EventCache<TodosAggregate.TodosAggregate'>.Instance.Clear()
        Cache.SnapCache<TodosAggregate.TodosAggregate'>.Instance.Clear()
        Cache.StateCache<TodosAggregate.TodosAggregate'>.Instance.Clear()

        db.Reset TagsAggregate.Version TagsAggregate.StorageName
        Cache.EventCache<TagsAggregate>.Instance.Clear()
        Cache.SnapCache<TagsAggregate>.Instance.Clear()
        Cache.StateCache<TagsAggregate>.Instance.Clear()

        db.Reset CategoriesAggregate.Version CategoriesAggregate.StorageName
        Cache.EventCache<CategoriesAggregate>.Instance.Clear()
        Cache.SnapCache<CategoriesAggregate>.Instance.Clear()
        Cache.StateCache<CategoriesAggregate>.Instance.Clear()

    let resetEventStore() =

        Cache.CurrentStateRef<_>.Instance.Clear()
        Cache.CurrentStateRef<TodosAggregate>.Instance.Clear()
        Cache.CurrentStateRef<TodosAggregate'>.Instance.Clear()
        Cache.CurrentStateRef<TagsAggregate>.Instance.Clear()
        Cache.CurrentStateRef<CategoriesAggregate>.Instance.Clear()

        // let eventStore = Sharpino.Lib.EvStore.EventStoreBridge(eventStoreConnection)
        // async {
        eventStoreBridge.ResetSnapshots "_01" "_tags"
        eventStoreBridge.ResetEvents "_01"  "_tags"
        eventStoreBridge.ResetSnapshots "_01" "_todo"
        eventStoreBridge.ResetEvents "_01" "_todo"
        eventStoreBridge.ResetSnapshots "_02" "_todo"
        eventStoreBridge.ResetEvents "_02" "_todo"
        eventStoreBridge.ResetSnapshots "_01" "_categories"
        eventStoreBridge.ResetEvents "_01" "_categories"

        //     return result
        // }
        // |> Async.RunSynchronously

    type IApplication =
        {
            _migrator:          Option<unit -> Result<unit, string>>
            _reset:             unit -> unit
            _addEvents:         version * List<Json> * Name -> unit
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
            _migrator  =        currentPgApp.Migrate |> Some
            _reset  =           fun () -> resetDb pgStorage
            _addEvents =        fun (version, e: List<string>, name) -> pgStorage.AddEvents version e name |> ignore // ignore?
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
            _migrator  =        None
            _reset =            fun () -> resetDb pgStorage
            _addEvents =        fun (version, e: List<string>, name ) -> pgStorage.AddEvents version e name |> ignore
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
            _reset =            fun () -> resetDb memStorage
            _addEvents =        fun (version, e: List<string>, name) -> memStorage.AddEvents version e name |> ignore
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
            _reset =            fun () -> resetDb memStorage
            _addEvents =        fun (version, e: List<string>, name) -> memStorage.AddEvents version e name |> ignore
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

    [<CurrentVersion>]
    let evSApp =
        {
            _migrator =         None
            _reset =            fun () -> resetEventStore()
            _addEvents =        fun (version, e: List<string>, name) -> 
                                    let eventStore = Sharpino.EventStore.EventStoreBridgeFS(eventStoreConnection)
                                    async {
                                        let result = eventStore.AddEvents version e name
                                        return result
                                    }
                                    |> Async.RunSynchronously
            getAllTodos =       evStoreApp.GetAllTodos
            addTodo =           evStoreApp.AddTodo
            add2Todos =         evStoreApp.Add2Todos
            removeTodo =        evStoreApp.RemoveTodo
            getAllCategories =  evStoreApp.GetAllCategories
            addCategory =       evStoreApp.AddCategory
            removeCategory =    evStoreApp.RemoveCategory
            addTag =            evStoreApp.AddTag
            removeTag =         evStoreApp.RemoveTag
            getAllTags =        evStoreApp.GetAllTags
        }
