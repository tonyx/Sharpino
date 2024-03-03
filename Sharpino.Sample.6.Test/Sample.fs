module Tests

open Expecto
open Sharpino.MemoryStorage
open Sharpino.Storage
open Tonyx.Sharpino.Pub
open System
open Sharpino.Utils
open Sharpino
open Sharpino.Cache

[<Tests>]
let tests =
    let setUp () =
        AggregateCache<Dish.Dish>.Instance.Clear()
        StateCache<Kitchen.Kitchen>.Instance.Clear()

    let memoryStorage: IEventStore = MemoryStorage()
    let serializer = JsonSerializer(Utils.serSettings) :> ISerializer
    let doNothingBroker =
        {
            notify = None
            notifyAggregate = None
        }
    ftestList "sharpino kitchen examples" [
        testCase "initial state of kitchen is with no dishes" <| fun _ ->
            setUp ()
            let pubSystem = PubSystem.PubSystem(memoryStorage, doNothingBroker)
            let dishes = pubSystem.GetAllDishes()
            Expect.isOk dishes "should be ok"
            let result = dishes.OkValue
            Expect.equal (result |> List.length) 0 "should be equal"

        testCase "add a dish and retrieve it - OK" <| fun _ ->
            setUp ()

            let pubSystem = PubSystem.PubSystem(memoryStorage, doNothingBroker)
            let dish = Dish.Dish(Guid.NewGuid(), "test", [])
            let addDish = pubSystem.AddDish dish
            Expect.isOk addDish "should be ok"
            let retrievedDish = pubSystem.GetAllDishes()
            Expect.isOk retrievedDish "should be ok"
            let result = retrievedDish.OkValue
            Expect.equal (result |> List.length) 1 "should be equal"
            let (_, retrievedDish, _, _) = result |> List.head
            Expect.equal retrievedDish.Id dish.Id "should be equal"

        testCase "add an ingredient and retrieve it - OK" <| fun _ ->
            setUp ()

            let pubSystem = PubSystem.PubSystem(memoryStorage, doNothingBroker)
            // let ingredient = Ingredient.Ingredient(Guid.NewGuid(), "testIngredient")
            let addIngredient = pubSystem.AddIngredient (Guid.NewGuid(), "testIngredient")
            Expect.isOk addIngredient "should be ok"
    ]
    |> testSequenced
  
