module Tests

open ShoppingCart.Good
open ShoppingCart.GoodsContainer
open ShoppingCart.Supermarket
open ShoppingCart.Cart
open ShoppingCart.GoodEvents
open ShoppingCart.GoodCommands
open ShoppingCart.CartEvents
open ShoppingCart.GoodsContainerEvents
open System
open Sharpino.Storage
open Sharpino.Core
open Sharpino.PgStorage
open Sharpino.TestUtils
open Expecto
open Sharpino.MemoryStorage
open Sharpino.CommandHandler
open Sharpino.Cache
open DotNetEnv

let setUp (eventStore: IEventStore<'F>) =

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

let eventStoreMemory = MemoryStorage() 
let eventStorePostgres = PgEventStore(connection)

let goodsViewer = getAggregateStorageFreshStateViewer<Good, GoodEvents, 'F> eventStorePostgres
let cartViewer = getAggregateStorageFreshStateViewer<Cart, CartEvents, 'F> eventStorePostgres
let goodsContainerViewer = getStorageFreshStateViewer<GoodsContainer, GoodsContainerEvents, 'F> eventStorePostgres

let onlyDbSetup () =
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
        multipleTestCase "Initial state: there are no good in a Supermarket - Ok" marketInstances <| fun (supermarket, _, setup, _) ->
            setup()

            let goods = supermarket.Goods

            Expect.isOk goods "should be ok"
            Expect.equal goods.OkValue [] "There are no goods in the supermarket."
        
        multipleTestCase "after added a good to the supermarket then can retrieve it - Ok" marketInstances <| fun (supermarket, _, setup, _) ->
            setup ()

            // given

            let good = Good(Guid.NewGuid(), "Good", 10.0m, [])
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"
            
            // when
            let retrieved = supermarket.GetGood good.Id
            Expect.isOk retrieved "should be ok"
            let retrieved' = retrieved.OkValue

            // then
            Expect.equal  retrieved'.Id good.Id "should be the same good"

        multipleTestCase "create a good and put it in the supermarket" marketInstances <| fun (supermarket, _, setup, _) ->
            setup ()

            let good = Good(Guid.NewGuid(), "Good", 10.0m, [])
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"

        multipleTestCase "after added a good, its quantity is zero" marketInstances <| fun (supermarket, _, setup, _) ->
            setup ()
            let id = Guid.NewGuid()

            // given
            let good = Good(id, "Good", 10.0m, [])
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"
            let retrievedQuantity = supermarket.GetGoodsQuantity id
            Expect.isOk retrievedQuantity "should be ok"
            let result = retrievedQuantity.OkValue
            Expect.equal result 0 "should be the same quantity"

        multipleTestCase "Add a good. Increase its quantity, retrieve checking the quantity - Ok" marketInstances <| fun (supermarket, _, setup, _) ->
            setup ()
            let id = Guid.NewGuid()
            // given
            let good = Good(id, "Good", 10.0m, [])
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"
            
            // when
            let addQuantity = supermarket.AddQuantity(id, 10)
            Expect.isOk addQuantity "should be ok"
            
            // then
            let retrievedQuantity = supermarket.GetGoodsQuantity id
            Expect.isOk retrievedQuantity "should be ok"
            let result = retrievedQuantity.OkValue
            Expect.equal result 10 "should be the same quantity"

        multipleTestCase "create a cart" marketInstances <| fun (supermarket, _, setup, _) ->
            setup ()
            // given
            let cartId = Guid.NewGuid()
            let cart = Cart(cartId, Map.empty)
            // when
            let addCart = supermarket.AddCart cart
            
            // then
            Expect.isOk addCart "should be ok"

        multipleTestCase "add a good, increase its quantity and then put some of that good in a cart. the total quantity in the supermarket will be decreased - Ok" marketInstances <| fun (supermarket, _, setup, _) ->
            setup ()
            
            // given

            let cartId = Guid.NewGuid()
            let cart = Cart(cartId, Map.empty)
            let cartAdded = supermarket.AddCart cart
            Expect.isOk cartAdded "should be ok"
            let good = Good(Guid.NewGuid(), "Good", 10.0m, [])
            
            // when
            let GoodAdded = supermarket.AddGood good
            Expect.isOk GoodAdded "should be ok"

            // then
            let addQuantity = supermarket.AddQuantity(good.Id, 10)

            let addedToCart = supermarket.AddGoodToCart(cartId, good.Id, 1)
            Expect.isOk addedToCart "should be ok"
            let retrieved = supermarket.GetCart cartId
            Expect.isOk retrieved "should be ok"
            let result = retrieved.OkValue.Goods
            Expect.equal result.Count 1 "should be the same quantity"

        multipleTestCase "try adding more items than available. - Error" marketInstances <| fun (supermarket, _, setup, _) ->
            setup ()

            // given
            let cartId = Guid.NewGuid()
            let cart = Cart(cartId, Map.empty)
            let cartAdded = supermarket.AddCart cart

            Expect.isOk cartAdded "should be ok"

            let good = Good(Guid.NewGuid(), "Good", 10.0m, [])
            let GoodAdded = supermarket.AddGood good
            Expect.isOk GoodAdded "should be ok"

            let addQuantity = supermarket.AddQuantity(good.Id, 10)
            Expect.isOk addQuantity "should be ok"

            // when
            let addedToCart = supermarket.AddGoodToCart(cartId, good.Id, 11)
            
            // then
            Expect.isError addedToCart "should be an error"
            let (Error e) = addedToCart
            Expect.equal e "Quantity not available" "should be the same error"

        multipleTestCase "when adding a good into an unexisting cart will get an Error" marketInstances <| fun (supermarket, _, setup, _) ->
            setup ()
            
            // given
            let good = Good(Guid.NewGuid(), "Good", 10.0m, [])
            let GoodAdded = supermarket.AddGood good
            Expect.isOk GoodAdded "should be ok"
            
            // when
            let addedToCart = supermarket.AddGoodToCart(Guid.NewGuid(), good.Id, 1)
            
            // then
            Expect.isError addedToCart "should be an error"

        multipleTestCase "try adding an unexisting good to a cart - Error" marketInstances <| fun (supermarket, _, setup, _) ->
            setup ()

            // given
            let cartId = Guid.NewGuid()
            let cart = Cart(cartId, Map.empty)
            let cartAdded = supermarket.AddCart cart
            Expect.isOk cartAdded "should be ok"

            // when
            let addedToCart = supermarket.AddGoodToCart(cartId, Guid.NewGuid(), 1)
            
            // then
            Expect.isError addedToCart "should be an error" 

        multipleTestCase "add multiple goods to a cart - Ok" marketInstances <| fun (supermarket, _, setup, _) ->
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

            let _ = supermarket.AddQuantity(good1.Id, 8)
            let _ = supermarket.AddQuantity(good2.Id, 10)

            let addedToCart1 = supermarket.AddGoodsToCart(cartId, [(good1.Id, 1); (good2.Id, 1)])

            let cart = supermarket.GetCart cartId
            Expect.isOk cart "should be ok"

            let result = cart.OkValue.Goods
            Expect.equal result.Count 2 "should be the same quantity"  

            Expect.equal result.[good1.Id] 1 "should be the same quantity"
            Expect.equal result.[good2.Id] 1 "should be the same quantity"

            let good1Quantity = supermarket.GetGoodsQuantity good1.Id
            Expect.isOk good1Quantity "should be ok"
            Expect.equal good1Quantity.OkValue 7 "should be the same quantity"

            let Good2Quantity = supermarket.GetGoodsQuantity good2.Id
            Expect.isOk Good2Quantity "should be ok"
            Expect.equal Good2Quantity.OkValue 9 "should be the same quantity"

        multipleTestCase "add multiple good to a cart, exceeding quantity by one so can't add it. Nothing changes - Error" marketInstances <| fun (supermarket, _, setup, _) ->
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

            let _ = supermarket.AddQuantity(good1.Id, 10)
            let _ = supermarket.AddQuantity(good2.Id, 10)

            let addedToCart1 = supermarket.AddGoodsToCart(cartId, [(good1.Id, 11); (good2.Id, 1)])
            
            Expect.isError addedToCart1 "should be an error"

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

        multipleTestCase "add and a good and it's quantity will be zero - Ok" marketInstances <| fun (supermarket, _, setup, _) ->
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

        multipleTestCase "can't add twice a good with the same name - Error" marketInstances <| fun (supermarket, _ , setup, _) ->
            setup()

            let good = Good(Guid.NewGuid(), "Good", 10.0m, [])
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"

            let good2 = Good(Guid.NewGuid(), "Good", 10.0m, [])
            let addedTwice = supermarket.AddGood good2
            Expect.isError addedTwice "should be an error"

        multipleTestCase "add a good and remove it - Ok" marketInstances <| fun (supermarket,_ , setup, _) ->
            setup ()

            let good = Good(Guid.NewGuid(), "Good", 10.0m, [])
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"
            let removed = supermarket.RemoveGood good.Id
            Expect.isOk removed "should be ok"

            let retrieved = supermarket.GetGood good.Id
            Expect.isError retrieved "should be an error"

        multipleTestCase  "when remove a good then can gets its quantity - Error" marketInstances <| fun (supermarket, _, setup, _) ->
            setup ()
            let good = Good(Guid.NewGuid(), "Good", 10.0m, [])
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"
            let removed = supermarket.RemoveGood good.Id
            Expect.isOk removed "should be ok"

            let quantity = supermarket.GetGoodsQuantity good.Id
            Expect.isError quantity "should be an error"

        multipleTestCase "Initial state. Add many goods and add quantity to them many times. Verify multiple events and multiple aggregate updates - Ok" marketInstances <| fun (supermarket, _, setup, _) ->
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

            let quantityAdded = supermarket.AddQuantity (good1Id, 10)
            let quantityAdded21 = supermarket.AddQuantity (good2Id, 2)
            let quantityAdded22 = supermarket.AddQuantity (good3Id, 99)

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

            let supermarketGoodState = supermarket.GetGood good1Id |> Result.get
            Expect.equal supermarketGoodState.Quantity 20 "should be the same state"

        multipleTestCase "retrieve the undoer of a command, apply the command, then retrieve the events from the undoer and check that they will be the events that works as the anticommand - Ok" marketInstances <| fun (supermarket, _, setup, _ ) ->
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

        multipleTestCase "can't apply the undoer of a command before the related command has actually been applied - Error" marketInstances <| fun (supermarket, eventStore, setup, _) ->
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
