module Tests

open Expecto
open Sharpino.MemoryStorage
open Sharpino.PgStorage
open Sharpino.Storage
open Sharpino.TestUtils
open Tonyx.Sharpino.Pub
open Tonyx.Sharpino.Pub.Dish
open Tonyx.Sharpino.Pub.Kitchen
open Tonyx.Sharpino.Pub.Ingredient
open Tonyx.Sharpino.Pub.Supplier
open System
open Sharpino.Cache
open DotNetEnv

[<Tests>]
let tests =
    Env.Load()
    let password = Environment.GetEnvironmentVariable("password")
    let connection =
        "Server=127.0.0.1;"+
        "Database=es_pub_system;" +
        "User Id=safe;"+
        $"Password={password};"
    let memEventStore: IEventStore<string> = MemoryStorage()
    let pgEventStore: IEventStore<string> = PgEventStore(connection)
    let setUp () =
        AggregateCache<Dish.Dish, string>.Instance.Clear()
        StateCache2<Kitchen>.Instance.Invalidate()
        memEventStore.Reset Kitchen.Version Kitchen.StorageName
        memEventStore.ResetAggregateStream Ingredient.Version Ingredient.StorageName
        memEventStore.ResetAggregateStream Dish.Version Dish.StorageName
        pgEventStore.Reset Kitchen.Version Kitchen.StorageName
        pgEventStore.ResetAggregateStream Ingredient.Version Ingredient.StorageName
        pgEventStore.ResetAggregateStream Dish.Version Dish.StorageName

    // todo: check if you will need it in those tests
    let doNothingBroker =
        {
            notify = None
            notifyAggregate = None
        }
    let storages = [
        //  (memEventStore, 0, 0)
         (pgEventStore, 0, 0)
    ]
   
    testList "sharpino kitchen examples" [
        multipleTestCase "initial state of kitchen is with no dishes" storages  <| fun (eventStore, _, _) ->
            setUp ()
            
            let pubSystem = PubSystem.PubSystem(eventStore, doNothingBroker)
            let dishes = pubSystem.GetAllDishes()
            Expect.isOk dishes "should be ok"
            let result = dishes.OkValue
            Expect.equal (result |> List.length) 0 "should be equal"

        multipleTestCase "add a dish and retrieve it - OK" storages <| fun (eventStore, _, _) ->
            setUp ()
        
            let pubSystem = PubSystem.PubSystem(eventStore, doNothingBroker)
            let dish = Dish.Dish(Guid.NewGuid(), "test", [])
            let addDish = pubSystem.AddDish dish
            Expect.isOk addDish "should be ok"
            let retrievedDish = pubSystem.GetAllDishes()
            Expect.isOk retrievedDish "should be ok"
            let result = retrievedDish.OkValue
            Expect.equal (result |> List.length) 1 "should be equal"
            let (_, retrievedDish) = result |> List.head
            Expect.equal retrievedDish.Id dish.Id "should be equal"
        
        multipleTestCase "add an ingredient and check that it does exist - OK" storages <| fun (eventStore, _, _) ->
            setUp ()
        
            let guid = Guid.NewGuid()
            let pubSystem = PubSystem.PubSystem(eventStore, doNothingBroker)
            let addIngredient = pubSystem.AddIngredient (guid, "testIngredient")
            Expect.isOk addIngredient "should be ok"
            let retrieved = pubSystem.GetAllIngredientReferences()
            Expect.isOk retrieved "should be ok"
            let retrieved' = retrieved.OkValue
            Expect.equal 1 retrieved'.Length "should be equal"
        
            let retrievedIngredients = pubSystem.GetAllIngredients()
            Expect.isOk retrievedIngredients "should be ok"
            let retrievedIngredients' = retrievedIngredients.OkValue
            Expect.equal 1 retrievedIngredients'.Length "should be equal"
            let (_, ingredient) = retrievedIngredients'.[0]
            Expect.equal ingredient.Name "testIngredient" "should be equal"
            Expect.equal ingredient.Id guid "should be equal"
        
        multipleTestCase "add an ingredient and add a type to it - OK" storages <| fun (eventStore, _, _) ->
            setUp ()
        
            let guid = Guid.NewGuid()
            let pubSystem = PubSystem.PubSystem(eventStore, doNothingBroker)
            let addIngredient = pubSystem.AddIngredient (guid, "testIngredient")
            Expect.isOk addIngredient "should be ok"
            let retrieved = pubSystem.GetAllIngredientReferences()
            Expect.isOk retrieved "should be ok"
            let retrieved' = retrieved.OkValue
            Expect.equal retrieved'.Length 1 "should be equal"
        
            let addTypeToIngredient = pubSystem.AddTypeToIngredient (guid, IngredientType.Meat)
            Expect.isOk addTypeToIngredient "should be ok"
        
            let retrievedIngredients = pubSystem.GetAllIngredients()
            Expect.isOk retrievedIngredients "should be ok"
            let retrievedIngredients' = retrievedIngredients.OkValue
            Expect.equal retrievedIngredients'.Length 1 "should be equal"
            let (_, ingredient) = retrievedIngredients'.[0]
            Expect.equal ingredient.Name "testIngredient" "should be equal"
            Expect.equal ingredient.Id guid "should be equal"
            Expect.equal ingredient.IngredientTypes.Length 1 "should be equal"
       
        multipleTestCase "sugar can be measured in term of teaspoon" storages <| fun (eventStore, _, _) ->
            setUp ()
            
            let guid = Guid.NewGuid()
            let pubSystem = PubSystem.PubSystem(eventStore, doNothingBroker)
            let addIngredient = pubSystem.AddIngredient (guid, "sugar")
            Expect.isOk addIngredient "should be ok"
            let retrieved = pubSystem.GetAllIngredientReferences()
            Expect.isOk retrieved "should be ok"
            let retrieved' = retrieved.OkValue
            Expect.equal retrieved'.Length 1 "should be equal"
            Expect.isTrue true "true"
            
            let addTypeToIngredient = pubSystem.AddMeasureType (guid, MeasureType.Teaspoons)
            Expect.isOk addTypeToIngredient "should be ok"
            let retrievedIngredients = pubSystem.GetIngredient guid
            Expect.isOk retrievedIngredients "should be ok"
            let retrievedIngredients' = retrievedIngredients.OkValue
            Expect.equal retrievedIngredients'.Id guid "should be equal"
            let measures = retrievedIngredients'.MeasureTypes
            Expect.equal measures.Length 1 "should be equal"
        
        multipleTestCase "add and retrieve a new supplier" storages <| fun (eventStore, _, _) ->
            setUp ()
            
            let pubSystem = PubSystem.PubSystem(eventStore, doNothingBroker)
            let guid = Guid.NewGuid()
            let supplier = Supplier(guid, "testSupplier", "testAddress@me.com", "123-456-7890")
            let addSupplier = pubSystem.AddSupplier supplier
            
            let suppliers  = pubSystem.GetAllSuppliers ()
            Expect.isOk suppliers "should be ok"
            let suppliers' = suppliers.OkValue
            Expect.equal suppliers'.Length 1 "should be equal"
    ]
    |> testSequenced
        
  
