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
open Sharpino.Utils
open Sharpino.ApplicationInstance
open Sharpino
open Sharpino.Cache

[<Tests>]
let tests =
    let connection =
        "Server=127.0.0.1;"+
        "Database=es_pub_system;" +
        "User Id=safe;"+
        "Password=safe;"
    let memEventStore: IEventStore = MemoryStorage()
    let pgEventStore: IEventStore = PgEventStore(connection)
    let setUp () =
        AggregateCache<Dish.Dish>.Instance.Clear()
        StateCache<Kitchen>.Instance.Clear()
        memEventStore.Reset Kitchen.Version Kitchen.StorageName
        memEventStore.ResetAggregateStream Ingredient.Version Ingredient.StorageName
        memEventStore.ResetAggregateStream Dish.Version Dish.StorageName
        pgEventStore.Reset Kitchen.Version Kitchen.StorageName
        pgEventStore.ResetAggregateStream Ingredient.Version Ingredient.StorageName
        pgEventStore.ResetAggregateStream Dish.Version Dish.StorageName

    // todo: check if you will need it in those tests
    let serializer = JsonSerializer(Utils.serSettings) :> ISerializer
    let doNothingBroker =
        {
            notify = None
            notifyAggregate = None
        }
    let storages = [
         // (memEventStore, 0, 0)
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
            // given 
            let pubSystem = PubSystem.PubSystem(eventStore, doNothingBroker)
            let dish = Dish.Dish(Guid.NewGuid(), "test", [], [])
            
            // when
            let addDish = pubSystem.AddDish dish
            Expect.isOk addDish "should be ok"
            
            // then
            let retrievedDish = pubSystem.GetAllDishes()
            Expect.isOk retrievedDish "should be ok"
            let result = retrievedDish.OkValue
            Expect.equal (result |> List.length) 1 "should be equal"
            let (_, retrievedDish, _, _) = result |> List.head
            Expect.equal retrievedDish.Id dish.Id "should be equal"
        
        multipleTestCase "add an ingredient and check that it does exist - OK" storages <| fun (eventStore, _, _) ->
            setUp ()
        
            // given
            let ingredientGuid = Guid.NewGuid()
            let pubSystem = PubSystem.PubSystem(eventStore, doNothingBroker)
            let addIngredient = pubSystem.AddIngredient (ingredientGuid, "testIngredient")
            
            // when
            Expect.isOk addIngredient "should be ok"
            let retrieved = pubSystem.GetAllIngredientReferences()
            Expect.isOk retrieved "should be ok"
            let retrieved' = retrieved.OkValue
            Expect.equal 1 retrieved'.Length "should be equal"
       
            // then 
            let retrievedIngredients = pubSystem.GetAllIngredients()
            Expect.isOk retrievedIngredients "should be ok"
            let retrievedIngredients' = retrievedIngredients.OkValue
            Expect.equal 1 retrievedIngredients'.Length "should be equal"
            let (_, ingredient, _, _) = retrievedIngredients'.[0]
            Expect.equal ingredient.Name "testIngredient" "should be equal"
            Expect.equal ingredient.Id ingredientGuid "should be equal"
        
        multipleTestCase "add an ingredient and add a type to it - OK" storages <| fun (eventStore, _, _) ->
            setUp ()
            
            // given 
            let ingredientGuid = Guid.NewGuid()
            let pubSystem = PubSystem.PubSystem(eventStore, doNothingBroker)
            let addIngredient = pubSystem.AddIngredient (ingredientGuid, "testIngredient")
            Expect.isOk addIngredient "should be ok"
        
            // when
            let addTypeToIngredient = pubSystem.AddTypeToIngredient (ingredientGuid, IngredientType.Meat)
            Expect.isOk addTypeToIngredient "should be ok"
            
            // then
            let retrievedIngredients = pubSystem.GetAllIngredients()
            Expect.isOk retrievedIngredients "should be ok"
            let retrievedIngredients' = retrievedIngredients.OkValue
            Expect.equal retrievedIngredients'.Length 1 "should be equal"
            let (_, ingredient, _, _) = retrievedIngredients'.[0]
            Expect.equal ingredient.Name "testIngredient" "should be equal"
            Expect.equal ingredient.Id ingredientGuid "should be equal"
            Expect.equal ingredient.IngredientTypes.Length 1 "should be equal"
       
        multipleTestCase "sugar can be measured in term of teaspoon" storages <| fun (eventStore, _, _) ->
            setUp ()
            
            // given 
            let sugarGuid = Guid.NewGuid()
            let pubSystem = PubSystem.PubSystem(eventStore, doNothingBroker)
            let addIngredient = pubSystem.AddIngredient (sugarGuid, "sugar")
            Expect.isOk addIngredient "should be ok"
            let ingredients = pubSystem.GetAllIngredientReferences()
            Expect.isOk ingredients "should be ok"
            let retrieved' = ingredients.OkValue
            Expect.equal retrieved'.Length 1 "should be equal"
            Expect.isTrue true "true"
            
            // when
            let addTypeToIngredient = pubSystem.AddMeasureType (sugarGuid, MeasureType.Teaspoons)
            Expect.isOk addTypeToIngredient "should be ok"
            
            // then
            let retrievedIngredients = pubSystem.GetIngredient sugarGuid
            Expect.isOk retrievedIngredients "should be ok"
            let retrievedIngredients' = retrievedIngredients.OkValue
            Expect.equal retrievedIngredients'.Id sugarGuid "should be equal"
            let measures = retrievedIngredients'.MeasureTypes
            Expect.equal measures.Length 1 "should be equal"
        
        multipleTestCase "add and retrieve a new supplier" storages <| fun (eventStore, _, _) ->
            setUp ()
            
            // given
            let pubSystem = PubSystem.PubSystem(eventStore, doNothingBroker)
            let supplierGuid = Guid.NewGuid()
            
            // when
            let supplier = Supplier(supplierGuid, "testSupplier", "testAddress@me.com", "123-456-7890")
            let addSupplier = pubSystem.AddSupplier supplier
            Expect.isOk addSupplier "should be ok"
            let suppliers  = pubSystem.GetAllSuppliers ()
            Expect.isOk suppliers "should be ok"
            
            // then
            let suppliers' = suppliers.OkValue
            Expect.equal suppliers'.Length 1 "should be equal"
           
        multipleTestCase "can add an ingredient of a certain measure to a receipt if that measeure is part of that ingredient - Ok " storages <| fun (eventStore, _, _) ->
            setUp ()
            
            // given
            let pubSystem = PubSystem.PubSystem(eventStore, doNothingBroker)
            let tomatoGuid = Guid.NewGuid()
            let addIngredient = pubSystem.AddIngredient (tomatoGuid, "tomato")
            Expect.isOk addIngredient "should be ok"
            let addTypeToIngredient = pubSystem.AddMeasureType (tomatoGuid, MeasureType.Milliliters)
            Expect.isOk addTypeToIngredient "should be ok"
            
            let spaghettiGuid = Guid.NewGuid()
            let addDish = pubSystem.AddDish (Dish.Dish(spaghettiGuid, "spaghetti", [], []))
            Expect.isOk addDish "should be ok"
            
            // when
            let oneGlass: MeasureQuantity = { MeasureType = MeasureType.Milliliters; Quantity = 100.0 }
            let oneGlassOfTomato =
                {
                    IngredientId = tomatoGuid
                    Quantity = oneGlass |> Some
                }
            let addIngredientToDish = pubSystem.AddIngredientReceiptItem (spaghettiGuid, oneGlassOfTomato)
            Expect.isOk addIngredientToDish "should be ok"
            
            // then
            let retrievedDish = pubSystem.GetDish spaghettiGuid
            Expect.isOk retrievedDish "should be ok"
            let retrievedDish' = retrievedDish.OkValue
            Expect.equal retrievedDish'.IngredientReceiptItems.Length 1 "should be equal"
            let retrievedIngredientReceiptItem = retrievedDish'.IngredientReceiptItems.[0]
            Expect.equal retrievedIngredientReceiptItem.IngredientId tomatoGuid "should be equal"
            Expect.equal retrievedIngredientReceiptItem.Quantity.Value.Quantity 100.0 "should be equal"
            
        multipleTestCase "can't add an ingredient of a certain measure to a receipt if that measure is not part of that ingredient - Ok " storages <| fun (eventStore, _, _) ->
            setUp ()
            
            // given
            let pubSystem = PubSystem.PubSystem(eventStore, doNothingBroker)
            let tomatoGuid = Guid.NewGuid()
            let addIngredient = pubSystem.AddIngredient (tomatoGuid, "tomato")
            Expect.isOk addIngredient "should be ok"
            let addTypeToIngredient = pubSystem.AddMeasureType (tomatoGuid, MeasureType.Milliliters)
            Expect.isOk addTypeToIngredient "should be ok"
            
            let spaghettiGuid = Guid.NewGuid()
            let addDish = pubSystem.AddDish (Dish.Dish(spaghettiGuid, "spaghetti", [], []))
            Expect.isOk addDish "should be ok"
            
            // when
            // let oneGlass: MeasureQuantity = { MeasureType = MeasureType.Milliliters; Quantity = 100.0 }
            let onePiece: MeasureQuantity = { MeasureType = MeasureType.Pieces; Quantity = 1.00 }
            let oneGlassOfTomato =
                {
                    IngredientId = tomatoGuid
                    Quantity = onePiece |> Some
                }
            
            // then    
            let addIngredientToDish = pubSystem.AddIngredientReceiptItem (spaghettiGuid, oneGlassOfTomato)
            Expect.isError addIngredientToDish "should be error"
            
    ]
    |> testSequenced
        
  
