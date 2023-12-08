
namespace Sharpino.EventSourcing.Sample
open Sharpino
open Sharpino.Cache
open Sharpino.Storage
open Sharpino.Utils

open Sharpino.Sample.Entities.Todos
open Sharpino.Sample.Entities.Categories
open Sharpino.Sample.Entities.Tags
open Sharpino.Sample.TodosCluster
open Sharpino.Sample.TagsCluster
open Sharpino.Sample.CategoriesCluster
open Sharpino.Sample.EventStoreApp
open Sharpino.Sample.Entities.TodosReport

open Sharpino.Sample.Shared.Entities

open Sharpino.Sample
open Newtonsoft.Json
open System
open Sharpino.Definitions

open Confluent.Kafka

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

    // I had to comment this out because it gives annoying messages when kafka is not enabled

    let localHostbroker = KafkaBroker.getKafkaBroker("localhost:9092", connection)

    let memoryStorage = MemoryStorage.MemoryStorage()
    let currentPgApp = App.CurrentVersionApp(pgStorage)

    // I had to comment this out because it gives annoying messages when kafka is not enabled
    let currentPgAppWithKafka = App.CurrentVersionApp(pgStorage, localHostbroker)

    let upgradedPgApp = App.UpgradedApp(pgStorage)
    let currentMemApp = App.CurrentVersionApp(memoryStorage)
    let upgradedMemApp = App.UpgradedApp(memoryStorage)

    let eventStoreBridge = Sharpino.EventStore.EventStoreStorage(eventStoreConnection, jsonSerializer) :> ILightStorage
    let evStoreApp = EventStoreApp(Sharpino.EventStore.EventStoreStorage(eventStoreConnection, jsonSerializer))

    let resetAppId() =
        ApplicationInstance.ApplicationInstance.Instance.ResetGuid()

    let resetDb (db: IEventStore) =
        db.Reset TodosCluster.Version TodosCluster.StorageName
        StateCache<TodosCluster>.Instance.Clear()

        db.Reset TodosAggregate'.Version TodosAggregate'.StorageName 
        StateCache<TodosCluster.TodosAggregate'>.Instance.Clear()

        db.Reset TagsCluster.Version TagsCluster.StorageName
        StateCache<TagsCluster>.Instance.Clear()

        db.Reset CategoriesCluster.Version CategoriesCluster.StorageName
        StateCache<CategoriesCluster>.Instance.Clear()

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
            _notify:            Option<Version -> Name -> List<int * Json> -> Result< List<Confluent.Kafka.DeliveryResult<Confluent.Kafka.Null,string>>, string >>    
            _migrator:          Option<unit -> Result<unit, string>>
            _reset:             unit -> unit
            _addEvents:         Version * List<Json> * Name -> unit
            getAllTodos:        unit -> Result<List<Todo>, string>
            addTodo:            Todo -> Result< List<List<int>> * List<Option<List<DeliveryResult<Null, string>>>>, string>
            add2Todos:          Todo * Todo -> Result< List<List<int>> * List<Option<List<DeliveryResult<Null, string>>>>, string>
            removeTodo:         Guid -> Result< List<List<int>> * List<Option<List<DeliveryResult<Null, string>>>>, string>
            getAllCategories:   unit -> Result<List<Category>, string> 
            addCategory:        Category -> Result< List<List<int>> * List<Option<List<DeliveryResult<Null, string>>>>, string>
            removeCategory:     Guid -> Result< List<List<int>> * List<Option<List<DeliveryResult<Null, string>>>>, string>
            addTag:             Tag -> Result< List<List<int>> * List<Option<List<DeliveryResult<Null, string>>>>, string>
            removeTag:          Guid -> Result< List<List<int>> * List<Option<List<DeliveryResult<Null, string>>>>, string>
            getAllTags:         unit -> Result<List<Tag>, string>
            todoReport:         DateTime -> DateTime -> TodosEvents
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
            _addEvents =        fun (vers: Version, e: List<string>, name ) -> 
                                    let deser = e
                                    (pgStorage :> IEventStore).AddEvents vers name deser |> ignore
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
            _notify =           upgradedPgApp._eventBroker.notify
            _migrator  =        None
            _reset =            fun () -> 
                                    resetDb pgStorage
                                    resetAppId()
            _addEvents =        fun (version, e: List<string>, name ) -> 
                                    let deser = e
                                    (pgStorage :> IEventStore).AddEvents version name deser |> ignore
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
            _notify =           currentMemApp._eventBroker.notify
            _migrator  =        currentMemApp.Migrate |> Some
            _reset =            fun () -> 
                                    resetDb memoryStorage
                                    resetAppId()
            _addEvents =        fun (version, e: List<string>, name ) -> 
                                    let deser = e
                                    (memoryStorage :> IEventStore).AddEvents version name deser |> ignore
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

    // I had to comment this out because it gives annoying messages when kafka is not enabled
    [<CurrentVersion>]
    let currentVersionPgWithKafkaApp =
        {
            _notify =           currentPgAppWithKafka._eventBroker.notify
            _migrator =         None
            _reset =            fun () -> 
                                    resetDb pgStorage
                                    resetAppId()
            _addEvents =        fun (version, e: List<string>, name ) -> 
                                    let deser = e
                                    (pgStorage :> IEventStore).AddEvents version name deser |> ignore
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
            _notify =           upgradedMemApp._eventBroker.notify
            _migrator =         None
            _reset =            fun () -> 
                                    resetDb memoryStorage
                                    resetAppId()
            _addEvents =        fun (version, e: List<string>, name ) -> 
                                    let deser = e 
                                    (memoryStorage :> IEventStore).AddEvents version name deser |> ignore
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

    // [<CurrentVersion>]
    // are we going to remove totally eventstoredb support? 
    // let evSApp =
    //     {
    //         _notify =           None
    //         _migrator =         None
    //         _reset =            fun () -> 
    //                                 resetEventStore()
    //                                 resetAppId()
    //         _addEvents =        fun (version, e: List<string>, name) -> 
    //                                 let eventStore = Sharpino.EventStore.EventStoreStorage(eventStoreConnection, jsonSerializer) :> ILightStorage
    //                                 let deser = e |> List.map (fun x -> x |> jsonSerializer.Deserialize  |> Result.get)
    //                                 async {
    //                                     let result = eventStore.AddEvents version deser name
    //                                     return result
    //                                 }
    //                                 |> Async.RunSynchronously
    //                                 |> ignore
    //         getAllTodos =       evStoreApp.GetAllTodos
    //         addTodo =           evStoreApp.AddTodo
    //         add2Todos =         evStoreApp.Add2Todos
    //         removeTodo =        evStoreApp.RemoveTodo
    //         getAllCategories =  evStoreApp.GetAllCategories
    //         addCategory =       evStoreApp.AddCategory
    //         removeCategory =    evStoreApp.RemoveCategory
    //         addTag =            evStoreApp.AddTag
    //         removeTag =         evStoreApp.RemoveTag
    //         getAllTags =        evStoreApp.GetAllTags
    //         todoReport =        evStoreApp.TodoReport
    //     }
