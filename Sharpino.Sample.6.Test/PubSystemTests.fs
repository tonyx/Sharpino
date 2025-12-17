module Tests

open System.Threading
open System.Threading.Tasks
open Expecto
open Sharpino
open Sharpino.CommandHandler
open Sharpino.EventBroker
open Sharpino.MemoryStorage
open Sharpino.PgStorage
open Sharpino.RabbitMq
open Sharpino.Storage
open Sharpino.TestUtils
open Tonyx.Sharpino.Pub
open Tonyx.Sharpino.Pub.DishConsumer
open Tonyx.Sharpino.Pub.IngredientConsumer
open Tonyx.Sharpino.Pub.Supplier
open System
open Sharpino.Cache
open DotNetEnv

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Tonyx.Sharpino.Pub.SupplierConsumer

Env.Load()
let password = Environment.GetEnvironmentVariable("password")
let connection =
    "Server=127.0.0.1;"+
    "Database=es_pub_system;" +
    "User Id=safe;"+
    $"Password={password};"

let memEventStore: IEventStore<string> = MemoryStorage()
let pgEventStore: IEventStore<string> = PgEventStore(connection)

#if RABBITMQ
let hostBuilder = 
    Host.CreateDefaultBuilder()
        .ConfigureServices(fun (services: IServiceCollection) ->
            services.AddSingleton<RabbitMqReceiver>() |> ignore
            services.AddHostedService<DishConsumer>() |> ignore
            services.AddHostedService<IngredientConsumer>() |> ignore
            services.AddHostedService<SupplierConsumer>() |> ignore
            ()
        )
    
let host = hostBuilder.Build()
let hostTask = host.StartAsync()
let services = host.Services

let dishConsumer =
    host.Services.GetServices<IHostedService>()
    |> Seq.find (fun s -> s.GetType() = typeof<DishConsumer>)
    :?> DishConsumer

dishConsumer.SetFallbackAggregateStateRetriever (getAggregateStorageFreshStateViewer<Dish, DishEvents, string> pgEventStore)    

let ingredientConsumer =
    host.Services.GetServices<IHostedService>()
    |> Seq.find (fun s -> s.GetType() = typeof<IngredientConsumer>)
    :?> IngredientConsumer

ingredientConsumer.SetFallbackAggregateStateRetriever (getAggregateStorageFreshStateViewer<Ingredient, IngredientEvents, string> pgEventStore)    

let supplierConsumer =
    host.Services.GetServices<IHostedService>()
    |> Seq.find (fun s -> s.GetType() = typeof<SupplierConsumer>)
    :?> SupplierConsumer

supplierConsumer.SetFallbackAggregateStateRetriever (getAggregateStorageFreshStateViewer<Supplier, SupplierEvents, string> pgEventStore)

let aggregateMessageSenders = System.Collections.Generic.Dictionary<string, MessageSender>()

let dishMessageSender =
    mkMessageSender "127.0.0.1" "_01_dish" |> Result.get
    
let ingredientMessageSender =
    mkMessageSender "127.0.0.1" "_01_ingredient" |> Result.get
    
let supplierMessageSender =
    mkMessageSender "127.0.0.1" "_01_supplier" |> Result.get

aggregateMessageSenders.Add("_01_dish", dishMessageSender)
aggregateMessageSenders.Add("_01_ingredient", ingredientMessageSender)
aggregateMessageSenders.Add("_01_supplier", supplierMessageSender)

let rabbitMQmessageSender =
    MessageSenders.MessageSender
        (fun queueName ->
            let sender = aggregateMessageSenders.TryGetValue(queueName)
            match sender with
            | true, sender -> sender |> Ok
            | _ -> (sprintf "not found %s" queueName) |> Error)
#endif

[<Tests>]
let tests =
    
    let setUp () =
        memEventStore.ResetAggregateStream Ingredient.Version Ingredient.StorageName
        memEventStore.ResetAggregateStream Dish.Version Dish.StorageName
        memEventStore.ResetAggregateStream Supplier.Version Supplier.StorageName
        pgEventStore.ResetAggregateStream Ingredient.Version Ingredient.StorageName
        pgEventStore.ResetAggregateStream Dish.Version Dish.StorageName
        pgEventStore.ResetAggregateStream Supplier.Version Supplier.StorageName
        AggregateCache3.Instance.Clear ()
        DetailsCache.Instance.Clear ()
        
    let emptyMessageSender =
        fun queueName ->
            fun message ->
                ValueTask.CompletedTask
    let storages = [
         #if RABBITMQ
            PubSystem.PubSystem(pgEventStore, rabbitMQmessageSender, getAggregateStorageFreshStateViewer<Ingredient, IngredientEvents, string> pgEventStore), pgEventStore, rabbitMQmessageSender, 100, 0
         #else
            PubSystem.PubSystem(pgEventStore, MessageSenders.NoSender, getAggregateStorageFreshStateViewer<Ingredient, IngredientEvents, string> pgEventStore), pgEventStore, emptyMessageSender, 0, 0
         #endif
    ]
   
    testList "sharpino kitchen examples" [
        multipleTestCase "initial state of kitchen is with no dishes" storages  <| fun (pubSystem, eventStore, _, delay, _) ->
            setUp ()
            
            let dishes = pubSystem.GetAllDishes()
            Expect.isOk dishes "should be ok"
            let result = dishes.OkValue
            Expect.equal (result |> List.length) 0 "should be equal"

        multipleTestCase "add a dish and retrieve it - OK" storages <| fun (pubSystem, eventStore, _, delay, _) ->
            setUp ()
        
            let dish = Dish(Guid.NewGuid(), "test", [])
            let addDish = pubSystem.AddDish dish
            Expect.isOk addDish "should be ok"
            Thread.Sleep delay
            let retrievedDish = pubSystem.GetAllDishes()
            Expect.isOk retrievedDish "should be ok"
            let result = retrievedDish.OkValue
            Expect.equal (result |> List.length) 1 "should be equal"
            let retrievedDish = result |> List.head
            Expect.equal retrievedDish.Id dish.Id "should be equal"
        
        multipleTestCase "add an ingredient and check that it does exist - OK" storages <| fun (pubSystem, eventStore, _, delay, _) ->
            setUp ()
        
            let guid = Guid.NewGuid()
            let addIngredient = pubSystem.AddIngredient (guid, "testIngredient")
        
            Thread.Sleep delay
            let retrievedIngredients = pubSystem.GetAllIngredients()
            Expect.isOk retrievedIngredients "should be ok"
            let retrievedIngredients' = retrievedIngredients.OkValue
            Expect.equal 1 retrievedIngredients'.Length "should be equal"
            let ingredient = retrievedIngredients'.[0]
            Expect.equal ingredient.Name "testIngredient" "should be equal"
            Expect.equal ingredient.Id guid "should be equal"
        
        multipleTestCase "add an ingredient and add a type to it - OK" storages <| fun (pubSystem, eventStore, _,  delay, _) ->
            setUp ()
        
            let guid = Guid.NewGuid()
            let addIngredient = pubSystem.AddIngredient (guid, "testIngredient")
            Expect.isOk addIngredient "should be ok"
        
            let addTypeToIngredient = pubSystem.AddTypeToIngredient (guid, IngredientType.Meat)
            Expect.isOk addTypeToIngredient "should be ok"
        
            Thread.Sleep delay
            let retrievedIngredients = pubSystem.GetAllIngredients()
            Expect.isOk retrievedIngredients "should be ok"
            let retrievedIngredients' = retrievedIngredients.OkValue
            Expect.equal retrievedIngredients'.Length 1 "should be equal"
            let ingredient = retrievedIngredients'.[0]
            Expect.equal ingredient.Name "testIngredient" "should be equal"
            Expect.equal ingredient.Id guid "should be equal"
            Expect.equal ingredient.IngredientTypes.Length 1 "should be equal"
       
        multipleTestCase "sugar can be measured in term of teaspoon" storages <| fun (pubSystem, eventStore, _, delay, _) ->
            setUp ()
            
            let guid = Guid.NewGuid()
            let addIngredient = pubSystem.AddIngredient (guid, "sugar")
            Expect.isOk addIngredient "should be ok"
            let addTypeToIngredient = pubSystem.AddMeasureType (guid, MeasureType.Teaspoons)
            Expect.isOk addTypeToIngredient "should be ok"
            Thread.Sleep delay
            let retrievedIngredients = pubSystem.GetIngredient guid
            Expect.isOk retrievedIngredients "should be ok"
            let retrievedIngredients' = retrievedIngredients.OkValue
            Expect.equal retrievedIngredients'.Id guid "should be equal"
            let measures = retrievedIngredients'.MeasureTypes
            Expect.equal measures.Length 1 "should be equal"
        
        multipleTestCase "add and retrieve a new supplier" storages <| fun (pubSystem, eventStore, _, delay, _) ->
            setUp ()
            
            let guid = Guid.NewGuid()
            let supplier = Supplier(guid, "testSupplier", "testAddress@me.com", "123-456-7890")
            let addSupplier = pubSystem.AddSupplier supplier
            
            Thread.Sleep delay
            let suppliers  = pubSystem.GetAllSuppliers ()
            Expect.isOk suppliers "should be ok"
            let suppliers' = suppliers.OkValue
            Expect.equal suppliers'.Length 1 "should be equal"
            
        multipleTestCase "add a dish and retrieve the dishDetails - Ok" storages <| fun (pubSystem, eventStore, _, delay, _) ->
            setUp ()
            let dish = Dish(Guid.NewGuid(), "spaghetti pomodoro", [])
            let addDish = pubSystem.AddDish dish
            Expect.isOk addDish "dish not added"
            Thread.Sleep delay
            let dishDetails = pubSystem.GetDishDetails dish.Id
            Expect.isOk dishDetails "dish details not retrieved"
            
    ]
    |> testSequenced
        
  
