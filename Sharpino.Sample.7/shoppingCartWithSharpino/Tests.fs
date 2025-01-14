module Tests

open ShoppingCart.Good
open ShoppingCart.Commons

open ShoppingCart.GoodsContainer
open ShoppingCart.GoodsContainer
open ShoppingCart.Supermarket
open ShoppingCart.Cart
open System
open Sharpino.Storage
open Sharpino.PgStorage
open Sharpino.KafkaBroker
open Sharpino.TestUtils
open Sharpino.PgBinaryStore
open Expecto
open Sharpino.MemoryStorage
open ShoppingCart.Supermarket
open ShoppingCart.Cart
open Sharpino.TestUtils
open Sharpino.MemoryStorage
open Expecto
open Sharpino.Definitions
open Sharpino.KafkaBroker
open Sharpino.Cache

// open FsKafka

// open Confluent.Kafka
open Sharpino.CommandHandler
open ShoppingCart.CartEvents
open ShoppingCart.GoodEvents
open Sharpino.KafkaReceiver
open ShoppingCart.GoodsContainerEvents
open ShoppingCart.GoodCommands
open Sharpino.Core
open DotNetEnv

let setUp (eventStore: IEventStore<'F>) =
    Env.Load() |> ignore
    let password = Environment.GetEnvironmentVariable("password")

    eventStore.Reset GoodsContainer.Version GoodsContainer.StorageName
    eventStore.Reset Good.Version Good.StorageName
    eventStore.ResetAggregateStream Good.Version Good.StorageName

    eventStore.Reset Cart.Version Cart.StorageName
    eventStore.ResetAggregateStream Cart.Version Cart.StorageName

    StateCache2<GoodsContainer>.Instance.Invalidate()
    AggregateCache<Good,string>.Instance.Clear()
    AggregateCache<Cart,string>.Instance.Clear()

let connection = 

        Env.Load() |> ignore
        let password = Environment.GetEnvironmentVariable("password")

        "Server=127.0.0.1;" +
        "Database=es_shopping_cart;" +
        "User Id=safe;"+
        $"Password={password};"

let byteAConnection =
        Env.Load() |> ignore
        let password = Environment.GetEnvironmentVariable("password")
        "Server=127.0.0.1;" +
        "Database=es_shopping_cart_bin;" +
        "User Id=safe;"+
        $"Password={password};"

let eventStoreMemory = MemoryStorage() //:> IEventStore<string>
let eventStorePostgres = PgEventStore(connection) //:> IEventStore<string>

let goodsViewer = getAggregateStorageFreshStateViewer<Good, GoodEvents, 'F> eventStorePostgres
let cartViewer = getAggregateStorageFreshStateViewer<Cart, CartEvents, 'F> eventStorePostgres
let goodsContainerViewer = getStorageFreshStateViewer<GoodsContainer, GoodsContainerEvents, 'F> eventStorePostgres


let onlyDbSetup () =
    setUp eventStorePostgres
    ()

let setUpDbAndTopics () =
    setUp eventStorePostgres
    ()  

let doNothingBroker: IEventBroker<string> =
    {  notify = None
       notifyAggregate = None }

let marketInstances =
    [
        Supermarket(eventStorePostgres, doNothingBroker), "eventStorePostgres", onlyDbSetup , (fun () -> ())  ;

        // Supermarket(eventStorePostgres, doNothingBroker, goodsViewer, cartViewer), "eventStorePostgres", (fun () -> setUp eventStorePostgres), (fun () -> ())  ;
    ]
[<Tests>]
let tests =

    testList "samples" [
        multipleTestCase "there are no good in a Supermarket" marketInstances <| fun (supermarket, eventStore, setup, _) ->
            setup()

            let goods = supermarket.Goods

            Expect.isOk goods "should be ok"
            Expect.equal goods.OkValue [] "There are no goods in the supermarket."
        
        multipleTestCase "add a good to the supermarket and retrieve it" marketInstances <| fun (supermarket, eventStore, setup, refresh) ->
            setup ()

            let good = Good(Guid.NewGuid(), "Good", 10.0m, [])
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"
            refresh()
            let retrieved = supermarket.GetGood good.Id
            Expect.isOk retrieved "should be ok"
            let retrieved' = retrieved.OkValue
            Expect.equal  retrieved'.Id good.Id "should be the same good"

        multipleTestCase "add a good and put it in the supermarket" marketInstances <| fun (supermarket, eventStore, setup, _) ->
            setup ()

            let good = Good(Guid.NewGuid(), "Good", 10.0m, [])
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"

        multipleTestCase "add a good and its quantity is zero" marketInstances <| fun (supermarket, eventStore, setup, refresh) ->
            setup ()

            let id = Guid.NewGuid()
            let good = Good(id, "Good", 10.0m, [])
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"
            refresh ()
            let retrievedQuantity = supermarket.GetGoodsQuantity id
            Expect.isOk retrievedQuantity "should be ok"
            let result = retrievedQuantity.OkValue
            Expect.equal result 0 "should be the same quantity"

        multipleTestCase "add a good, then increase its quantity - Ok" marketInstances <| fun (supermarket, eventStore, setup, refresh) ->
            setup ()

            let id = Guid.NewGuid()
            let good = Good(id, "Good", 10.0m, [])
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"
            let setQuantity = supermarket.AddQuantity(id, 10)
            Expect.isOk setQuantity "should be ok"
            refresh ()
            let retrievedQuantity = supermarket.GetGoodsQuantity id
            Expect.isOk retrievedQuantity "should be ok"
            let result = retrievedQuantity.OkValue
            Expect.equal result 10 "should be the same quantity"


        multipleTestCase "create a cart" marketInstances <| fun (supermarket, eventStore, setup, _) ->
            setup ()
            let cartId = Guid.NewGuid()
            let cart = Cart(cartId, Map.empty)
            let basket = supermarket.AddCart cart
            Expect.isOk basket "should be ok"

        multipleTestCase "add a good, add a quantity and then put something in a cart. the total quantity will be decreased - Ok" marketInstances <| fun (supermarket, eventStore, setup,  refresh) ->
            setup ()

            let cartId = Guid.NewGuid()
            let cart = Cart(cartId, Map.empty)
            let cartAdded = supermarket.AddCart cart
            Expect.isOk cartAdded "should be ok"

            let good = Good(Guid.NewGuid(), "Good", 10.0m, [])
            let GoodAdded = supermarket.AddGood good
            Expect.isOk GoodAdded "should be ok"

            refresh ()

            let addQuantity = supermarket.AddQuantity(good.Id, 10)

            let addedToCart = supermarket.AddGoodToCart(cartId, good.Id, 1)
            Expect.isOk addedToCart "should be ok"

            refresh ()
            let retrieved = supermarket.GetCart cartId

            Expect.isOk retrieved "should be ok"


        multipleTestCase "try adding more items than available. - Error" marketInstances <| fun (supermarket, eventStore, setup, refresh) ->
            setup ()

            let cartId = Guid.NewGuid()
            let cart = Cart(cartId, Map.empty)
            let cartAdded = supermarket.AddCart cart

            refresh ()

            Expect.isOk cartAdded "should be ok"

            let good = Good(Guid.NewGuid(), "Good", 10.0m, [])
            let GoodAdded = supermarket.AddGood good
            Expect.isOk GoodAdded "should be ok"

            let addQuantity = supermarket.AddQuantity(good.Id, 10)
            refresh ()

            let addedToCart = supermarket.AddGoodToCart(cartId, good.Id, 11)
            Expect.isError addedToCart "should be an error"

        multipleTestCase "try adding a good into an unexisting cart - Error" marketInstances <| fun (supermarket, eventStore, setup, refresh) ->
            setup ()

            let good = Good(Guid.NewGuid(), "Good", 10.0m, [])
            let GoodAdded = supermarket.AddGood good
            Expect.isOk GoodAdded "should be ok"

            refresh ()

            let addedToCart = supermarket.AddGoodToCart(Guid.NewGuid(), good.Id, 1)
            Expect.isError addedToCart "should be an error"

        multipleTestCase "try adding an unexisting good to a cart - Error" marketInstances <| fun (supermarket, eventStore, setup, _) ->
            setup ()

            let cartId = Guid.NewGuid()
            let cart = Cart(cartId, Map.empty)
            let cartAdded = supermarket.AddCart cart
            Expect.isOk cartAdded "should be ok"

            let addedToCart = supermarket.AddGoodToCart(cartId, Guid.NewGuid(), 1)
            Expect.isError addedToCart "should be an error" 

        fmultipleTestCase "add multiple goods to a cart - Ok" marketInstances <| fun (supermarket, eventStore, setup, refresh) ->
            setup ()

            let cartId = Guid.NewGuid()
            let cart = Cart(cartId, Map.empty)
            let cartAdded = supermarket.AddCart cart
            Expect.isOk cartAdded "should be ok"

            let good1 = Good(Guid.NewGuid(), "Good1", 10.0m, [])
            let GoodAdded1 = supermarket.AddGood good1
            Expect.isOk GoodAdded1 "should be ok"

            let good2 = Good(Guid.NewGuid(), "Good2", 10.0m, [])
            let GoodAdded2 = supermarket.AddGood good2
            Expect.isOk GoodAdded2 "should be ok"
            refresh ()

            let _ = supermarket.AddQuantity(good1.Id, 8)
            let _ = supermarket.AddQuantity(good2.Id, 10)

            let addedToCart1 = supermarket.AddGoodsToCart(cartId, [(good1.Id, 1); (good2.Id, 1)])

            let cart = supermarket.GetCart cartId
            Expect.isOk cart "should be ok"

            let result = cart.OkValue.Goods
            Expect.equal result.Count 2 "should be the same quantity"  

            Expect.equal result.[good1.Id] 1 "should be the same quantity"
            Expect.equal result.[good2.Id] 1 "should be the same quantity"

            refresh ()

            let good1Quantity = supermarket.GetGoodsQuantity good1.Id
            Expect.isOk good1Quantity "should be ok"
            Expect.equal good1Quantity.OkValue 7 "should be the same quantity"

            let Good2Quantity = supermarket.GetGoodsQuantity good2.Id
            Expect.isOk Good2Quantity "should be ok"
            Expect.equal Good2Quantity.OkValue 9 "should be the same quantity"

        multipleTestCase "add multiple good to a cart, exceeding quantity of one - Error" marketInstances <| fun (supermarket, eventStore, setup, refresh) ->
            setup ()

            let cartId = Guid.NewGuid()

            let cartId = Guid.NewGuid()
            let cart = Cart(cartId, Map.empty)
            let cartAdded = supermarket.AddCart cart
            Expect.isOk cartAdded "should be ok"

            let good1 = Good(Guid.NewGuid(), "Good1", 10.0m, [])
            let GoodAdded1 = supermarket.AddGood good1
            Expect.isOk GoodAdded1 "should be ok"

            let good2 = Good(Guid.NewGuid(), "Good2", 10.0m, [])
            let GoodAdded2 = supermarket.AddGood good2
            Expect.isOk GoodAdded2 "should be ok"

            refresh ()

            let _ = supermarket.AddQuantity(good1.Id, 10)
            let _ = supermarket.AddQuantity(good2.Id, 10)

            let addedToCart1 = supermarket.AddGoodsToCart(cartId, [(good1.Id, 11); (good2.Id, 1)])
            
            Expect.isError addedToCart1 "should be an error"

            refresh ()
            let cart = supermarket.GetCart cartId
            Expect.isOk cart "should be ok"
            Expect.equal cart.OkValue.Goods.Count 0 "should be the same quantity"   

            let retrievedGood1 = supermarket.GetGoodsQuantity good1.Id
            Expect.isOk retrievedGood1 "should be ok"

            let result1 = retrievedGood1.OkValue
            Expect.equal result1 10 "should be the same quantity"

            let result2 = supermarket.GetGoodsQuantity good2.Id
            Expect.isOk result2 "should be ok"
            Expect.equal result2.OkValue 10 "should be the same quantity"

        multipleTestCase "add and a good and it's quantity will be zero - Ok" marketInstances <| fun (supermarket, eventStore, setup, _) ->
            setup ()

            let cartId = Guid.NewGuid()
            let cart = Cart(cartId, Map.empty)
            let cartAdded = supermarket.AddCart cart
            Expect.isOk cartAdded "should be ok"

            let good = Good(Guid.NewGuid(), "Good", 10.0m, [])
            let GoodAdded = supermarket.AddGood good
            Expect.isOk GoodAdded "should be ok"
            let quantity = supermarket.GetGoodsQuantity good.Id
            Expect.isOk quantity "should be ok"

            let result = quantity.OkValue
            Expect.equal result 0 "should be the same quantity"

        multipleTestCase "can't add twice a good with the same name - Error" marketInstances <| fun (supermarket, eventStore, setup, _) ->
            setup()

            let good = Good(Guid.NewGuid(), "Good", 10.0m, [])
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"

            let good2 = Good(Guid.NewGuid(), "Good", 10.0m, [])
            let addedTwice = supermarket.AddGood good2
            Expect.isError addedTwice "should be an error"

        multipleTestCase "add a good and remove it - Ok" marketInstances <| fun (supermarket, eventStore, setup, refresh) ->
            setup ()

            let good = Good(Guid.NewGuid(), "Good", 10.0m, [])
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"
            let removed = supermarket.RemoveGood good.Id
            Expect.isOk removed "should be ok"

            let retrieved = supermarket.GetGood good.Id
            Expect.isError retrieved "should be an error"

        multipleTestCase  "when remove a good then can gets its quantity - Error" marketInstances <| fun (supermarket, eventStore, setup, _) ->
            setup ()
            let good = Good(Guid.NewGuid(), "Good", 10.0m, [])
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"
            let removed = supermarket.RemoveGood good.Id
            Expect.isOk removed "should be ok"

            let quantity = supermarket.GetGoodsQuantity good.Id
            Expect.isError quantity "should be an error"

        multipleTestCase "Initial state. Add many goods and add quantity to them many times. Verify multiple events and multiple aggregate updates - Ok" marketInstances <| fun (supermarket, eventStore, setup, refresh) ->
            setup ()
            let good1Id = Guid.NewGuid()
            let good1 = Good (good1Id, "Good1", 10.0m, [])
            let added = supermarket.AddGood good1

            let good2Id = Guid.NewGuid()
            let good2 = Good (good2Id, "Good2", 20.0m, [])
            let added2 = supermarket.AddGood good2
            let good3Id = Guid.NewGuid()
            let good3 = Good (good3Id, "Good3", 30.0m, [])
            let added3 = supermarket.AddGood good3

            let topic = (Good.StorageName + "-" + Good.Version).Replace("_", "")

            let quantityAdded = supermarket.AddQuantity (good1Id, 10)
            let quantityAdded21 = supermarket.AddQuantity (good2Id, 2)
            let quantityAdded22 = supermarket.AddQuantity (good3Id, 99)
            refresh ()

            let _ = supermarket.GetGood good1Id
            let _ = supermarket.GetGood good2Id 
            let _ = supermarket.GetGood good3Id

            let quantityAdded3 = supermarket.AddQuantity (good1Id, 3)
            let quantityAdded4 = supermarket.AddQuantity (good1Id, 2)
            let quantityAdded2 = supermarket.AddQuantity (good1Id, 5)

            let quantityAdded21 = supermarket.AddQuantity (good2Id, 2)
            let quantityAdded31 = supermarket.AddQuantity (good2Id, 5)
            let quantityAdded41 = supermarket.AddQuantity (good2Id, 6)
            let quantityAdded51 = supermarket.AddQuantity (good2Id, 7)

            let quantityAdded32 = supermarket.AddQuantity (good3Id, 51)
            let quantityAdded42 = supermarket.AddQuantity (good3Id, 69)
            let quantityAdded52 = supermarket.AddQuantity (good3Id, 73)
            let quantityAdded62 = supermarket.AddQuantity (good3Id, 99)
            refresh ()

            let supermarketGoodState = supermarket.GetGood good1Id |> Result.get
            Expect.equal supermarketGoodState.Quantity 20 "should be the same state"


        multipleTestCase "retrieve the undoer of a command, apply the command, then retrieve the events from the undoer and check that they will be the events that works as the anticommand - Ok" marketInstances <| fun (supermarket, eventStore, setup, refresh) ->
            setup ()

            let good = Good(Guid.NewGuid(), "Good", 10.0m, [])
            let goodAdded = supermarket.AddGood good

            Expect.isOk goodAdded "should be ok"

            let addQuantityCommand:AggregateCommand<Good,GoodEvents> = GoodCommands.AddQuantity 1

            let undoer = addQuantityCommand.Undoer
            let firstShotUndoer = undoer |> Option.get
            let undoerEvents = firstShotUndoer good goodsViewer
            Expect.isOk undoerEvents "should be ok"

            let addQuantity = runAggregateCommand<Good, GoodEvents, string> good.Id eventStorePostgres doNothingBroker addQuantityCommand
            Expect.isOk addQuantity "should be ok"
            let goodRetrieved = supermarket.GetGood good.Id |> Result.get
            Expect.equal goodRetrieved.Quantity 1 "should be the same quantity"

            let undoerEvents' = undoerEvents |> Result.get

            let undoerEventsResult = undoerEvents' () 
            Expect.isOk undoerEventsResult "should be ok"
            let result = undoerEventsResult |> Result.get
            Expect.equal result.Length 1 "should be the same quantity"
            Expect.equal result.[0] (QuantityRemoved 1) "should be the same quantity"


        multipleTestCase "can't apply the undoer of a command before the related command has actually been applied - Error" marketInstances <| fun (supermarket, eventStore, setup, refresh) ->
            setup ()
            let good = Good(Guid.NewGuid(), "Good", 10.0m, [])
            let goodAdded = supermarket.AddGood good

            Expect.isOk goodAdded "should be ok"

            let addQuantityCommand:AggregateCommand<Good,GoodEvents> = GoodCommands.AddQuantity 1

            let undoer = addQuantityCommand.Undoer
            let firstShotUndoer = undoer |> Option.get
            let undoerEvents = firstShotUndoer good goodsViewer
            Expect.isOk undoerEvents "should be ok"

            let undoerEvents' = undoerEvents |> Result.get

            let undoerEventsResult = undoerEvents' () 
            Expect.isError undoerEventsResult "should be an error"

    ]
    |> testSequenced
