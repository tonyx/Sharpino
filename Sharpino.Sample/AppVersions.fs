
namespace Sharpino.EventSourcing.Sample
open Sharpino
open Sharpino.Storage
open Sharpino.Utils

open Sharpino.Sample.Entities.Todos
open Sharpino.Sample.Entities.Categories
open Sharpino.Sample.Entities.Tags
open Sharpino.Sample.TodosAggregate
open Sharpino.Sample.Entities.Categories
open Sharpino.Sample.Entities.Todos
open Sharpino.Sample.TagsAggregate
open Sharpino.Sample.Entities.Tags
open Sharpino.Sample.CategoriesAggregate
open Sharpino.Sample.EventStoreApp
open Sharpino.Sample
open FSharpPlus.Operators

open System

// todo: this is duplicated code 
type Json = string
type Name = string
type version = string

module AppVersions =
    open Sharpino.Sample.Todos.TodoEvents
    open Sharpino.Sample.Categories.CategoriesEvents
    open Sharpino.Sample.Tags.TagsEvents
    open Newtonsoft.Json
    // beware that this is the test db and so we can reset it for testing
    // this should never be done in production
    let connection = 
        "Server=127.0.0.1;"+
        "Database=es_01;" +
        "User Id=safe;"+
        "Password=safe;"
    let eventStoreConnection = "esdb://localhost:2113?tls=false"
    let jsonSerSettings = JsonSerializerSettings()
    jsonSerSettings.TypeNameHandling <- TypeNameHandling.Objects
    jsonSerSettings.ReferenceLoopHandling <- ReferenceLoopHandling.Ignore

    let jsonSerializer = Utils.JsonSerializer(jsonSerSettings)

    let pgStorage: IStorage = DbStorage.PgDb(connection)
    // let memStorage: IStorage = MemoryStorage.MemoryStorage()
    // let currentPgApp = App.CurrentVersionApp(pgStorage)
    let refactoredStorage = DbStorageRef.PgDb(connection, jsonSerializer)
    let refactoredMemoryStorage = MemoryStorageRef.MemoryStorageRef(jsonSerializer)
    let currentPgApp = App.CurrentVersionApp(refactoredStorage)
    let upgradedPgApp = App.UpgradedApp(refactoredStorage)
    let currentMemApp = App.CurrentVersionApp(refactoredMemoryStorage)
    let upgradedMemApp = App.UpgradedApp(refactoredMemoryStorage)

    let eventStoreBridge = Sharpino.EventStore.EventStoreBridgeFS(eventStoreConnection) :> ILightStorage
    let evStoreApp = EventStoreApp(Sharpino.EventStore.EventStoreBridgeFS(eventStoreConnection))

    let eventualCosmosDbStorage =
        try 
            let cosmosDbStorage = CosmosDbStorage.ComsmosDbStorage("https://localhost:8081", "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==")
            Some cosmosDbStorage
        with 
            | _ -> None

    let refactoredCosmosApp = 
        match eventualCosmosDbStorage with
        | Some x -> AppRefStorage.CurrentVersionAppRef(x) |> Some
        | None -> None

    let refactoredMemoryApp = AppRefStorage.CurrentVersionAppRef(refactoredMemoryStorage)

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

    let resetRefactoredDb (db: IStorageRefactor) =
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

    let resetCosmosDb() =
        ()

    let resetEventStore() =

        Cache.CurrentState<_>.Instance.Clear()
        Cache.CurrentState<TodosAggregate>.Instance.Clear()
        Cache.CurrentState<TodosAggregate'>.Instance.Clear()
        Cache.CurrentState<TagsAggregate>.Instance.Clear()
        Cache.CurrentState<CategoriesAggregate>.Instance.Clear()

        eventStoreBridge.ResetSnapshots "_01" "_tags"
        eventStoreBridge.ResetEvents "_01"  "_tags"
        eventStoreBridge.ResetSnapshots "_01" "_todo"
        eventStoreBridge.ResetEvents "_01" "_todo"
        eventStoreBridge.ResetSnapshots "_02" "_todo"
        eventStoreBridge.ResetEvents "_02" "_todo"
        eventStoreBridge.ResetSnapshots "_01" "_categories"
        eventStoreBridge.ResetEvents "_01" "_categories"


    type IApplication =
        {
            _migrator:          Option<unit -> Result<unit, string>>
            _reset:             unit -> unit
            _addEvents:         version * List<Json> * Name -> unit
            _forceStateUpdate:  option<unit -> unit>
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
            _reset  =           fun () -> resetRefactoredDb refactoredStorage
            _forceStateUpdate = None
            // addevents is specifically used for testing to check what happens if adding twice the same event (in the sense that the evolve will be able to skip inconsistent events)
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

    let eventualRerCosmosDbApp: option<IApplication> =
        match eventualCosmosDbStorage with
        | Some x -> 
            let refCosmosDbApp: IApplication =
                {
                    _migrator =         None
                    _reset =            fun () -> resetCosmosDb()
                    _addEvents =        fun (version, e: List<string>, name) -> 
                                            let deser = e |>> (fun x -> jsonSerializer.Deserialize x |> Result.get)
                                            (x :> IStorageRefactor).AddEvents version deser name |> ignore // ignore?
                    _forceStateUpdate = None
                    getAllTodos =       refactoredCosmosApp.Value.GetAllTodos
                    addTodo =           refactoredCosmosApp.Value.AddTodo
                    add2Todos =         refactoredCosmosApp.Value.Add2Todos
                    removeTodo =        refactoredCosmosApp.Value.RemoveTodo
                    getAllCategories =  refactoredCosmosApp.Value.GetAllCategories
                    addCategory =       refactoredCosmosApp.Value.AddCategory
                    removeCategory =    refactoredCosmosApp.Value.RemoveCategory
                    addTag =            refactoredCosmosApp.Value.AddTag
                    removeTag =         refactoredCosmosApp.Value.RemoveTag
                    getAllTags =        refactoredCosmosApp.Value.GetAllTags
                }
            Some refCosmosDbApp
        | None -> None


    [<UpgradedVersion>]
    let upgradedPostgresApp =
        {
            _migrator  =        None
            _reset =            fun () -> resetRefactoredDb refactoredStorage
            _addEvents =        fun (version, e: List<string>, name ) -> 
                                    let deser = e |>> (fun x -> jsonSerializer.Deserialize x |> Result.get)
                                    (refactoredStorage :> IStorageRefactor).AddEvents version deser name |> ignore
            _forceStateUpdate = None
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
            _reset =            fun () -> resetRefactoredDb refactoredMemoryStorage
            _addEvents =        fun (version, e: List<string>, name ) -> 
                                    let deser = e |>> (fun x -> jsonSerializer.Deserialize x |> Result.get)
                                    (refactoredMemoryStorage :> IStorageRefactor).AddEvents version deser name |> ignore
            _forceStateUpdate = None
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
            _reset =            fun () -> resetRefactoredDb refactoredMemoryStorage
            _addEvents =        fun (version, e: List<string>, name ) -> 
                                    let deser = e |>> (fun x -> jsonSerializer.Deserialize x |> Result.get)
                                    (refactoredMemoryStorage :> IStorageRefactor).AddEvents version deser name |> ignore
            _forceStateUpdate = None
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
        let eventStoreBridge: EventStore.EventStoreBridgeFS = EventStore.EventStoreBridgeFS(eventStoreConnection)
        {
            _migrator =         None
            _reset =            fun () -> resetEventStore()
            _addEvents =        fun (version, e: List<string>, name) -> 
                                    let eventStore = Sharpino.EventStore.EventStoreBridgeFS(eventStoreConnection) :> ILightStorage
                                    async {
                                        let result = eventStore.AddEvents version e name
                                        return result
                                    }
                                    |> Async.RunSynchronously
                                    |> ignore
            _forceStateUpdate = (fun () -> 
                                    (eventStoreBridge |> LightRepository.updateState<TodosAggregate, TodoEvent>)
                                    (eventStoreBridge |> LightRepository.updateState<TodosAggregate', TodoEvent'>) 
                                    (eventStoreBridge |> LightRepository.updateState<TagsAggregate, TagEvent>)
                                    (eventStoreBridge |> LightRepository.updateState<CategoriesAggregate, CategoryEvent>)
                                ) 
                                |> Some
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
