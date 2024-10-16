
namespace Sharpino.EventSourcing.Sample
open Sharpino
open Sharpino.Cache
open Sharpino.Storage
open Sharpino.Utils

open Sharpino.Sample.TodosContext
open Sharpino.Sample.TagsContext
open Sharpino.Sample.CategoriesContext

open Sharpino.Sample.Shared.Entities

open Sharpino.Sample
open Newtonsoft.Json
open System
open Sharpino.Definitions

module AppVersions =
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

    let jsonSerializer = Utils.JsonSerializer(jsonSerSettings) :> ISerializer

    let pgStorage = PgStorage.PgEventStore(connection)

    let memoryStorage = MemoryStorage.MemoryStorage()
    
    let currentPgApp = App.CurrentVersionApp(pgStorage)
    
    let upgradedPgApp = App.UpgradedApp(pgStorage)
    let currentMemApp = App.CurrentVersionApp(memoryStorage)
    let upgradedMemApp = App.UpgradedApp(memoryStorage)

    let resetAppId() =
        ApplicationInstance.ApplicationInstance.Instance.ResetGuid()

    let resetDb (db: IEventStore<string>) =
        db.Reset TodosContext.Version TodosContext.StorageName
        StateCache<TodosContext>.Instance.Clear()

        db.Reset TodosContextUpgraded.Version TodosContextUpgraded.StorageName 
        StateCache<TodosContext.TodosContextUpgraded>.Instance.Clear()

        db.Reset TagsContext.Version TagsContext.StorageName
        StateCache<TagsContext>.Instance.Clear()

        db.Reset CategoriesContext.Version CategoriesContext.StorageName
        StateCache<CategoriesContext>.Instance.Clear()

    type IApplication =
        {
            _notify:            Option<Version -> Name -> List<int * Json> -> List<Confluent.Kafka.DeliveryResult<string, string>>>
            _migrator:          Option<unit -> Result<unit, string>>
            _reset:             unit -> unit
            _addEvents:         EventId * Version * List<Json> * Name * ContextStateId -> unit
            _pingTodo:          unit -> Result<unit, string>
            _pingCategories:    unit -> Result<unit, string>
            _pingTags:          unit -> Result<unit, string>
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
            _notify =           currentPgApp._eventBroker.notify
            _migrator  =        currentPgApp.Migrate |> Some
            // addevents is specifically used test what happens if adding twice the same event (in the sense that the evolve will be able to skip inconsistent events)
            _reset =            fun () -> 
                                    resetDb pgStorage
                                    resetAppId()
            _addEvents =        fun (eventId: EventId, vers: Version, e: List<string>, name, contextStateId ) -> 
                                    let deser = e
                                    (pgStorage :> IEventStore<string>).AddEvents eventId vers name deser |> ignore
            _pingTodo =         currentPgApp.PingTodo
            _pingCategories =   currentPgApp.PingCategory
            _pingTags =         currentPgApp.PingTag
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
            _notify =           upgradedPgApp._eventBroker.notify
            _migrator  =        None
            _reset =            fun () -> 
                                    resetDb pgStorage
                                    resetAppId()
            _addEvents =        fun (eventId, version, e: List<string>, name, contextStateId ) -> 
                                    let deser = e
                                    (pgStorage :> IEventStore<string>).AddEvents eventId version name deser |> ignore
            _pingTodo =         upgradedPgApp.PingTodo
            _pingCategories =   upgradedPgApp.PingCategory
            _pingTags =         upgradedPgApp.PingTag
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
            _notify =           currentMemApp._eventBroker.notify
            _migrator  =        currentMemApp.Migrate |> Some
            _reset =            fun () -> 
                                    resetDb memoryStorage
                                    resetAppId()
            _addEvents =        fun (eventId, version, e: List<string>, name, contextStateId ) -> 
                                    let deser = e
                                    (memoryStorage :> IEventStore<string>).AddEvents eventId version name deser |> ignore
            _pingTodo =         currentMemApp.PingTodo
            _pingCategories =   currentMemApp.PingCategory
            _pingTags =         currentMemApp.PingTag
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
            _notify =           upgradedMemApp._eventBroker.notify
            _migrator =         None
            _reset =            fun () -> 
                                    resetDb memoryStorage
                                    resetAppId()
            _addEvents =        fun (eventId, version, e: List<string>, name, contextStateId ) -> 
                                    let deser = e 
                                    (memoryStorage :> IEventStore<string>).AddEvents eventId version name deser |> ignore
            _pingTodo =         upgradedMemApp.PingTodo
            _pingCategories =   upgradedMemApp.PingCategory
            _pingTags =         upgradedMemApp.PingTag    
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


