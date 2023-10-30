
namespace Sharpino.EventSourcing.Sample
open Sharpino
open Sharpino.Cache
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
open Sharpino.Sample.Todos.TodoEvents
open Sharpino.Sample.Categories.CategoriesEvents
open Sharpino.Sample.Tags.TagsEvents
open Sharpino.Sample.Entities.TodosReport

open Sharpino.Sample
open FSharpPlus.Operators
open Newtonsoft.Json

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
    let jsonSerSettings = JsonSerializerSettings()
    jsonSerSettings.TypeNameHandling <- TypeNameHandling.Objects
    jsonSerSettings.ReferenceLoopHandling <- ReferenceLoopHandling.Ignore

    let jsonSerializer = Utils.JsonSerializer(jsonSerSettings) :> ISerializer

    let storage = PgStorage.PgStorage(connection)

    let doNothingBroker = 
        {
            notify = None
        }

    let localHostbroker = KafkaBroker.getKafkaBroker("localhost:9092", connection)

    let memoryStorage = MemoryStorage.MemoryStorage()
    let currentPgApp = App.CurrentVersionApp(storage, doNothingBroker)

    let currentPgAppWithKafka = App.CurrentVersionApp(storage, localHostbroker)

    let upgradedPgApp = App.UpgradedApp(storage, doNothingBroker)
    let currentMemApp = App.CurrentVersionApp(memoryStorage, doNothingBroker)
    let upgradedMemApp = App.UpgradedApp(memoryStorage, doNothingBroker)

    let eventStoreBridge = Sharpino.EventStore.EventStoreStorage(eventStoreConnection, jsonSerializer) :> ILightStorage
    let evStoreApp = EventStoreApp(Sharpino.EventStore.EventStoreStorage(eventStoreConnection, jsonSerializer))

    let resetDb (db: IStorage) =
        db.Reset TodosAggregate.Version TodosAggregate.StorageName
        EventCache<TodosAggregate>.Instance.Clear()
        SnapCache<TodosAggregate>.Instance.Clear()
        StateCache<TodosAggregate>.Instance.Clear()

        db.Reset TodosAggregate'.Version TodosAggregate'.StorageName 
        EventCache<TodosAggregate.TodosAggregate'>.Instance.Clear()
        SnapCache<TodosAggregate.TodosAggregate'>.Instance.Clear()
        StateCache<TodosAggregate.TodosAggregate'>.Instance.Clear()

        db.Reset TagsAggregate.Version TagsAggregate.StorageName
        EventCache<TagsAggregate>.Instance.Clear()
        SnapCache<TagsAggregate>.Instance.Clear()
        StateCache<TagsAggregate>.Instance.Clear()

        db.Reset CategoriesAggregate.Version CategoriesAggregate.StorageName
        EventCache<CategoriesAggregate>.Instance.Clear()
        SnapCache<CategoriesAggregate>.Instance.Clear()
        StateCache<CategoriesAggregate>.Instance.Clear()

    let resetEventStore() =

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
            _forceStateUpdate:  option<unit -> unit> // obsolete
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
            todoReport:         DateTime -> DateTime -> TodosEvents

        }

    [<CurrentVersion>]
    let currentPostgresApp =
        {
            _migrator  =        currentPgApp.Migrate |> Some
            _forceStateUpdate = None
            // addevents is specifically used test what happens if adding twice the same event (in the sense that the evolve will be able to skip inconsistent events)
            _reset =            fun () -> resetDb storage
            _addEvents =        fun (version, e: List<string>, name ) -> 
                                    let deser = e
                                    (storage :> IStorage).AddEvents version name deser |> ignore
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
            todoReport =        currentPgApp.TodoReport
        }

    [<UpgradedVersion>]
    let upgradedPostgresApp =
        {
            _migrator  =        None
            _reset =            fun () -> resetDb storage
            _addEvents =        fun (version, e: List<string>, name ) -> 
                                    let deser = e
                                    (storage :> IStorage).AddEvents version name deser |> ignore
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
            todoReport =        upgradedPgApp.TodoReport
        }


    [<CurrentVersion>]
    let currentMemoryApp =
        {
            _migrator  =        currentMemApp.Migrate |> Some
            _reset =            fun () -> resetDb memoryStorage
            _addEvents =        fun (version, e: List<string>, name ) -> 
                                    let deser = e
                                    (memoryStorage :> IStorage).AddEvents version name deser |> ignore
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
            todoReport =        currentMemApp.TodoReport
        }

    [<CurrentVersion>]
    let currentVersionPgWithKafkaApp =
        {
            _migrator =         None
            _reset =            fun () -> resetDb storage
            _addEvents =        fun (version, e: List<string>, name ) -> 
                                    let deser = e
                                    (storage :> IStorage).AddEvents version name deser |> ignore
            _forceStateUpdate = None
            getAllTodos =       currentPgAppWithKafka.GetAllTodos
            addTodo =           currentPgAppWithKafka.AddTodo
            add2Todos =         currentPgAppWithKafka.Add2Todos
            removeTodo =        currentPgAppWithKafka.RemoveTodo
            getAllCategories =  currentPgAppWithKafka.GetAllCategories
            addCategory =       currentPgAppWithKafka.AddCategory
            removeCategory =    currentPgAppWithKafka.RemoveCategory
            addTag =            currentPgAppWithKafka.AddTag
            removeTag =         currentPgAppWithKafka.RemoveTag
            getAllTags =        currentPgAppWithKafka.GetAllTags
            todoReport =        currentPgAppWithKafka.TodoReport
        }

    [<UpgradedVersion>]
    let upgradedMemoryApp =
        {
            _migrator =         None
            _reset =            fun () -> resetDb memoryStorage
            _addEvents =        fun (version, e: List<string>, name ) -> 
                                    let deser = e 
                                    (memoryStorage :> IStorage).AddEvents version name deser |> ignore
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
            todoReport =        upgradedMemApp.TodoReport
        }

    [<CurrentVersion>]
    let evSApp =
        {
            _migrator =         None
            _reset =            fun () -> resetEventStore()
            _addEvents =        fun (version, e: List<string>, name) -> 
                                    let eventStore = Sharpino.EventStore.EventStoreStorage(eventStoreConnection, jsonSerializer) :> ILightStorage
                                    let deser = e |> List.map (fun x -> x |> jsonSerializer.Deserialize  |> Result.get)
                                    async {
                                        // todo: refactor here remember that addevents returns a result now
                                        let result = eventStore.AddEvents version deser name
                                        return result
                                    }
                                    |> Async.RunSynchronously
                                    |> ignore
            _forceStateUpdate = None
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
            todoReport =        evStoreApp.TodoReport
        }
