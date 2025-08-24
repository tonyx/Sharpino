module Tests

open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open FSharpPlus.Math
open FsToolkit.ErrorHandling
open Sharpino
open Sharpino.Commons
open Sharpino.EventBroker
open Sharpino.PgBinaryStore
open Sharpino.RabbitMq
open ShoppingCart.CartConsumer
open ShoppingCart.Good
open ShoppingCart.GoodConsumer
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

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting

let setUp (eventStore: IEventStore<'F>) =

    eventStore.Reset GoodsContainer.Version GoodsContainer.StorageName
    eventStore.Reset Good.Version Good.StorageName
    eventStore.ResetAggregateStream Good.Version Good.StorageName

    eventStore.Reset Cart.Version Cart.StorageName
    eventStore.ResetAggregateStream Cart.Version Cart.StorageName

    StateCache2<GoodsContainer>.Instance.Invalidate()
    AggregateCache2.Instance.Clear()

let connection = 
        Env.Load() |> ignore
        let password = Environment.GetEnvironmentVariable("password")

        "Server=127.0.0.1;" +
        "Database=es_shopping_cart;" +
        "User Id=safe;"+
        $"Password={password};"

let eventStoreMemory = MemoryStorage() 
let eventStorePostgres = PgEventStore(connection)

#if RABBITMQ
let hostBuilder = 
    Host.CreateDefaultBuilder()
        .ConfigureServices(fun (services: IServiceCollection) ->
            services.AddSingleton<RabbitMqReceiver>() |> ignore
            services.AddHostedService<GoodConsumer>() |> ignore
            services.AddHostedService<CartConsumer>() |> ignore
            ()
        )
        
let host = hostBuilder.Build()

let hostTask = host.StartAsync()

let services = host.Services

let goodConsumer =
    host.Services.GetServices<IHostedService>()
    |> Seq.find (fun s -> s.GetType() = typeof<GoodConsumer>)
    :?> GoodConsumer

goodConsumer.SetFallbackAggregateStateRetriever (getAggregateStorageFreshStateViewer<Good, GoodEvents, string> eventStorePostgres)

let cartConsumer =
    host.Services.GetServices<IHostedService>()
    |> Seq.find (fun s -> s.GetType() = typeof<CartConsumer>)
    :?> CartConsumer

cartConsumer.SetFallbackAggregateStateRetriever (getAggregateStorageFreshStateViewer<Cart, CartEvents, string> eventStorePostgres)    

let rabbitMqGoodsStateViewer = goodConsumer.GetAggregateState
let rabbitMqCartStateViewer = cartConsumer.GetAggregateState

let aggregateMessageSenders = System.Collections.Generic.Dictionary<string, MessageSender>()

let cartMessageSender =
    mkMessageSender "127.0.0.1" "_01_cart"
    |> Result.get

let goodMessageSender =
    mkMessageSender "127.0.0.1" "_01_good"
    |> Result.get

aggregateMessageSenders.Add("_01_cart", cartMessageSender)
aggregateMessageSenders.Add("_01_good", goodMessageSender)

let messageSender =
    fun queueName ->
        let sender = aggregateMessageSenders.TryGetValue(queueName)
        match sender with
        | true, sender -> sender
        | _ -> failwith "not found XX"
#endif


let jsonDbGoodsViewer = getAggregateStorageFreshStateViewer<Good, GoodEvents, string> eventStorePostgres
let jsonDbCartViewer = getAggregateStorageFreshStateViewer<Cart, CartEvents, string> eventStorePostgres
let jsonDbGoodsContainerViewer = getStorageFreshStateViewer<GoodsContainer, GoodsContainerEvents, string> eventStorePostgres

let jsonMemoryGoodsViewer = getAggregateStorageFreshStateViewer<Good, GoodEvents, string> eventStoreMemory
let jsonMemoryCartViewer = getAggregateStorageFreshStateViewer<Cart, CartEvents, string> eventStoreMemory
let jsonMemoryGoodsContainerViewer = getStorageFreshStateViewer<GoodsContainer, GoodsContainerEvents, string> eventStoreMemory

let setupPgEventStore () =
    setUp eventStorePostgres
    ()
let setupMemoryEventStore () =
    setUp eventStoreMemory
    ()

let emptyMessageSender =
    fun queueName ->
        fun message ->
            ValueTask.CompletedTask
                
let marketInstances =
    [
#if RABBITMQ
        Supermarket(eventStorePostgres, messageSender, jsonDbGoodsContainerViewer, rabbitMqGoodsStateViewer,  rabbitMqCartStateViewer ), "eventStorePostgres", setupPgEventStore, jsonDbGoodsViewer, eventStorePostgres:> IEventStore<string>, messageSender, 100
#else
        Supermarket(eventStorePostgres, emptyMessageSender, jsonDbGoodsContainerViewer, jsonDbGoodsViewer, jsonDbCartViewer ), "eventStorePostgres", setupPgEventStore, jsonDbGoodsViewer, eventStorePostgres:> IEventStore<string>, emptyMessageSender, 0 ;
#endif
    ]
    
[<Tests>]
let tests =

    testList "samples" [
        multipleTestCase "Initial state: there are no good in a Supermarket - Ok" marketInstances <| fun (supermarket, _, setup, _, _, _, _) ->
            setup()

            let goods = supermarket.Goods

            Expect.isOk goods "should be ok"
            Expect.equal goods.OkValue [] "There are no goods in the supermarket."
        
        multipleTestCase "after added a good to the supermarket then can retrieve it - Ok" marketInstances <| fun (supermarket, _, setup, _, _,_, timeToWait) ->
            setup ()

            // given

            Thread.Sleep(timeToWait)
            let good = Good.MkGood (Guid.NewGuid(), "Good", 10.0m)
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"
             
            Thread.Sleep(timeToWait)
            // when
            let retrieved = supermarket.RetrieveGoodBypassingContainer good.Id
            Expect.isOk retrieved "should be ok"
            let retrieved' = retrieved.OkValue
            
            Thread.Sleep(timeToWait)
            // then
            Expect.equal  retrieved'.Id good.Id "should be the same good"

        multipleTestCase "create a good and put it in the supermarket" marketInstances <| fun (supermarket, _, setup, _, _, _, timeToWait) ->
            setup ()

            let good = Good.MkGood (Guid.NewGuid(), "Good", 10.0m)
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"
            
        multipleTestCase "create apple,  put it in the supermarket and add a specific new quantity again - Ok" marketInstances <| fun (supermarket, _, setup, _, _,_, timeToWait) ->
            setup ()
            let goodId = Guid.NewGuid()
            let good = Good.MkGood (goodId, "Apple", 10.0m)
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"
            
            Thread.Sleep(timeToWait)
            let addQuantity = supermarket.AddQuantity(goodId, 10)
            Expect.isOk addQuantity "should be ok"
            let retrievedQuantity = supermarket.GetGoodsQuantity goodId
            Expect.isOk retrievedQuantity "should be ok"

        multipleTestCase "after added a good, its quantity is zero" marketInstances <| fun (supermarket, _, setup, _, _, _, timeToWait) ->
            setup ()
            let id = Guid.NewGuid()

            // given
            let good = Good.MkGood (id, "Good", 10.0m)
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"
            
            Thread.Sleep(timeToWait)
            // when
            let retrievedQuantity = supermarket.GetGoodsQuantity id
            Expect.isOk retrievedQuantity "should be ok"
            
            // then
            let result = retrievedQuantity.OkValue
            Expect.equal result 0 "should be the same quantity"

        multipleTestCase "Add a good. Increase its quantity, retrieve checking the quantity - Ok" marketInstances <| fun (supermarket, _, setup, _, _, _, timeToWait) ->
            setup ()
            let id = Guid.NewGuid()
            // given
            let good = Good.MkGood (id, "Good", 10.0m)
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"
            
            // when
            Thread.Sleep(timeToWait)
            let addQuantity = supermarket.AddQuantity(id, 10)
            Expect.isOk addQuantity "should be ok"
            
            // then
            let retrievedQuantity = supermarket.GetGoodsQuantity id
            Expect.isOk retrievedQuantity "should be ok"
            let result = retrievedQuantity.OkValue
            Expect.equal result 10 "should be the same quantity"

        multipleTestCase "create a cart" marketInstances <| fun (supermarket, _, setup, _, _, _, timeToWait) ->
            setup ()
            // given
            let cartId = Guid.NewGuid()
            let cart = Cart.MkCart (Guid.NewGuid())
            // when
            Thread.Sleep(timeToWait)
            let addCart = supermarket.AddCart cart
            
            // then
            Expect.isOk addCart "should be ok"

        multipleTestCase "add a good, increase its quantity and then put some of that good in a cart. The total quantity in the supermarket will be decreased - Ok" marketInstances <| fun (supermarket, _, setup, _, _, _, timeToWait) ->
            setup ()
            
            // given

            let cartId = Guid.NewGuid()
            let cart = Cart.MkCart (cartId)
            
            let cartAdded = supermarket.AddCart cart
            Expect.isOk cartAdded "should be ok"
            let good = Good.MkGood (Guid.NewGuid(), "Good", 10.0m)
            
            Thread.Sleep(timeToWait)
            let retrievedX = supermarket.GetCart cartId
            Expect.isOk retrievedX "should be ok"
            
            // when
            let GoodAdded = supermarket.AddGood good
            Expect.isOk GoodAdded "should be ok"

            Thread.Sleep(timeToWait)
            // then
            let addQuantity = supermarket.AddQuantity(good.Id, 10)
            Expect.isOk addQuantity "should be ok" 

            Thread.Sleep(timeToWait)
            let addedToCart = supermarket.AddGoodToCart(cartId, good.Id, 1)
            Expect.isOk addedToCart "should be ok"
            
            Thread.Sleep(timeToWait)
            let retrieved = supermarket.GetCart cartId
            Expect.isOk retrieved "should be ok"
            let result = retrieved.OkValue.Goods
            Expect.equal result.Count 1 "should be the same quantity"

        multipleTestCase "try adding more items than available. - Error" marketInstances <| fun (supermarket, _, setup, _, _, _, timeToWait) ->
            setup ()

            // given
            let cartId = Guid.NewGuid()
            let cart = Cart.MkCart cartId
            let cartAdded = supermarket.AddCart cart

            Expect.isOk cartAdded "should be ok"

            let good = Good.MkGood (Guid.NewGuid(), "Good", 10.0m)
            let GoodAdded = supermarket.AddGood good
            Expect.isOk GoodAdded "should be ok"

            Thread.Sleep(timeToWait)
            let addQuantity = supermarket.AddQuantity(good.Id, 10)
            Expect.isOk addQuantity "should be ok"

            // when
            let addedToCart = supermarket.AddGoodToCart(cartId, good.Id, 11)
            
            // then
            Expect.isError addedToCart "should be an error"
            let (Error e) = addedToCart
            Expect.equal e "Quantity not available" "should be the same error"

        multipleTestCase "when adding a good into an unexisting cart will get an Error" marketInstances <| fun (supermarket, _, setup, _, _, _, timeToWait) ->
            setup ()
            
            // given
            let good = Good.MkGood (Guid.NewGuid(), "Good", 10.0m)
            let GoodAdded = supermarket.AddGood good
            Expect.isOk GoodAdded "should be ok"
            
            // when
            let unexistingCartGuid = Guid.NewGuid()
            let addedToCart = supermarket.AddGoodToCart (unexistingCartGuid, good.Id, 1)
            
            // then
            Expect.isError addedToCart "should be an error"

        multipleTestCase "try adding an unexisting good to a cart - Error" marketInstances <| fun (supermarket, _, setup, _, _, _, timeToWait) ->
            setup ()

            // given
            let cartId = Guid.NewGuid()
            // let cart = Cart(cartId, Map.empty)
            let cart = Cart.MkCart (Guid.NewGuid())
            let cartAdded = supermarket.AddCart cart
            Expect.isOk cartAdded "should be ok"

            // when
            let addedToCart = supermarket.AddGoodToCart (cartId, Guid.NewGuid(), 1)
            
            // then
            Expect.isError addedToCart "should be an error" 

        multipleTestCase "add multiple goods to a cart, the goods in the supermarket will decrease by the quantity added to the cart - Ok" marketInstances <| fun (supermarket, _, setup, _, _, _, timeToWait) ->
            setup ()

            // given
            let cartId = Guid.NewGuid()
            let cart = Cart.MkCart cartId
            let cartAdded = supermarket.AddCart cart
            Expect.isOk cartAdded "should be ok"

            let apple = Good.MkGood (Guid.NewGuid(), "apple", 10.0m)
            
            let GoodAdded1 = supermarket.AddGood apple
            Expect.isOk GoodAdded1 "should be ok"

            let lemon = Good.MkGood (Guid.NewGuid(), "lemon", 10.0m)
            let GoodAdded2 = supermarket.AddGood lemon
            Expect.isOk GoodAdded2 "should be ok"

            // when
            let _ = supermarket.AddQuantity (apple.Id, 8)
            let _ = supermarket.AddQuantity (lemon.Id, 10)

            Thread.Sleep(timeToWait)
            let addedToCart1 = supermarket.AddGoodsToCart (cartId, [(apple.Id, 1); (lemon.Id, 1)])
            Expect.isOk addedToCart1 "should be ok"

            Thread.Sleep(timeToWait)
            let cart = supermarket.GetCart cartId
            Expect.isOk cart "should be ok"

            Thread.Sleep(timeToWait)
            let result = cart.OkValue.Goods
            Expect.equal result.Count 2 "should be the same quantity"  

            Expect.equal result.[apple.Id] 1 "should be the same quantity"
            Expect.equal result.[lemon.Id] 1 "should be the same quantity"

            // then
            let applesQuantity = supermarket.GetGoodsQuantity apple.Id
            Expect.isOk applesQuantity "should be ok"
            Expect.equal applesQuantity.OkValue 7 "should be the same quantity"

            let lemonsQuantity = supermarket.GetGoodsQuantity lemon.Id
            Expect.isOk lemonsQuantity "should be ok"
            Expect.equal lemonsQuantity.OkValue 9 "should be the same quantity"


        multipleTestCase "add multiple good to a cart, exceeding quantity by one so can't add it. Nothing changes" marketInstances <| fun (supermarket, _, setup, _, _, _, timeToWait) ->
            setup ()

            // given
            let cartId = Guid.NewGuid()
            let cart = Cart.MkCart cartId
            let cartAdded = supermarket.AddCart cart
            Expect.isOk cartAdded "should be ok"

            Thread.Sleep(timeToWait)
            let good1 = Good.MkGood (Guid.NewGuid(), "Good1", 10.0m)
            let GoodAdded1 = supermarket.AddGood good1
            Expect.isOk GoodAdded1 "should be ok"

            Thread.Sleep(timeToWait)
            let good2 = Good.MkGood (Guid.NewGuid(), "Good2", 10.0m)
            let GoodAdded2 = supermarket.AddGood good2
            Expect.isOk GoodAdded2 "should be ok"

            // when
            Thread.Sleep(timeToWait)
            let _ = supermarket.AddQuantity (good1.Id, 10)
            Thread.Sleep(timeToWait)
            let _ = supermarket.AddQuantity (good2.Id, 10)

            let addedToCart1 = supermarket.AddGoodsToCart (cartId, [(good1.Id, 11); (good2.Id, 1)])
            
            Expect.isError addedToCart1 "should be an error"

            let cart = supermarket.GetCart cartId
            Expect.isOk cart "should be ok"
            Expect.equal cart.OkValue.Goods.Count 0 "should be the same quantity"   

            Thread.Sleep(timeToWait)
            let retrievedGood1 = supermarket.GetGoodsQuantity good1.Id
            Expect.isOk retrievedGood1 "should be ok"
            
            // then

            let result1 = retrievedGood1.OkValue
            Expect.equal result1 10 "should be the same quantity"

            Thread.Sleep(timeToWait)
            let result2 = supermarket.GetGoodsQuantity good2.Id
            Expect.isOk result2 "should be ok"
            Expect.equal result2.OkValue 10 "should be the same quantity"

        multipleTestCase "add and a good and it's quantity will be zero - Ok" marketInstances <| fun (supermarket, _, setup, _, _, _, timeToWait) ->
            setup ()

            let cartId = Guid.NewGuid()
            // let cart = Cart(cartId, Map.empty)
            let cart = Cart.MkCart (Guid.NewGuid())
            let cartAdded = supermarket.AddCart cart
            Expect.isOk cartAdded "should be ok"

            let good = Good.MkGood (Guid.NewGuid(), "Good", 10.0m)
            Thread.Sleep(timeToWait)
            let GoodAdded = supermarket.AddGood good
            Expect.isOk GoodAdded "should be ok"
            Thread.Sleep(timeToWait)
            let quantity = supermarket.GetGoodsQuantity good.Id
            Expect.isOk quantity "should be ok"

            let result = quantity.OkValue
            Expect.equal result 0 "should be the same quantity"

        multipleTestCase "can't add twice a good with the same name - Error" marketInstances <| fun (supermarket, _ , setup, _, _, _, timeToWait) ->
            setup()

            let good = Good.MkGood (Guid.NewGuid(), "Good", 10.0m)
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"

            let good2 = Good.MkGood (Guid.NewGuid(), "Good", 10.0m)
            Thread.Sleep(timeToWait)
            let addedTwice = supermarket.AddGood good2
            Expect.isError addedTwice "should be an error"

        multipleTestCase "add a good and remove it - Ok" marketInstances <| fun (supermarket,_ , setup, _, _, _, timeToWait) ->
            setup ()

            let good = Good.MkGood (Guid.NewGuid(), "Good", 10.0m)
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"
            Thread.Sleep(timeToWait)
            let removed = supermarket.RemoveGood good.Id
            Expect.isOk removed "should be ok"

            Thread.Sleep(timeToWait)
            let retrieved = supermarket.RetrieveGoodBypassingContainer good.Id
            Expect.isError retrieved "should be an error"

        multipleTestCase  "when remove a good then can't gets its quantity - Error" marketInstances <| fun (supermarket, _, setup, _, _, _, timeToWait) ->
            setup ()
            let good = Good.MkGood (Guid.NewGuid(), "Good", 10.0m)
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"
            Thread.Sleep(timeToWait)
            let removed = supermarket.RemoveGood good.Id
            Expect.isOk removed "should be ok"

            Thread.Sleep(timeToWait)
            let quantity = supermarket.GetGoodsQuantity good.Id
            Expect.isError quantity "should be an error"

        multipleTestCase "Initial state. Add many goods and add quantity to them many times. Verify multiple events and multiple aggregate updates - Ok" marketInstances <| fun (supermarket, _, setup, _, _, _, timeToWait) ->
            setup ()
            let good1Id = Guid.NewGuid()
            let good1 = Good.MkGood (good1Id, "Good1", 10.0m)
            let added = supermarket.AddGood good1

            System.Threading.Thread.Sleep(timeToWait)
            let quantityAdded = supermarket.AddQuantity (good1Id, 10)
            
            System.Threading.Thread.Sleep(timeToWait)
            let quantityAdded = supermarket.AddQuantity (good1Id, 3)
            
            System.Threading.Thread.Sleep(timeToWait)
            let quantityAdded21 = supermarket.AddQuantity (good1Id, 2)

            System.Threading.Thread.Sleep(timeToWait)
            let supermarketGoodState = supermarket.RetrieveGoodBypassingContainer good1Id |> Result.get
            Expect.equal supermarketGoodState.Quantity 15 "should be the same state"

        multipleTestCase "retrieve the undoer of a command, apply the command, then retrieve the events from the undoer and check that they will be the events that works as the anticommand - Ok" marketInstances <| fun (supermarket, _, setup, goodsViewer, eventStore, messageSender, timeToWait ) ->
            setup ()

            let good = Good.MkGood (Guid.NewGuid(), "Good", 10.0m)
            let goodAdded = supermarket.AddGood good

            Expect.isOk goodAdded "should be ok"

            let addQuantityCommand:AggregateCommand<Good,GoodEvents> = GoodCommands.AddQuantity 1

            let undoer = addQuantityCommand.Undoer
            let firstShotUndoer = undoer |> Option.get
            let undoerEvents = firstShotUndoer good goodsViewer
            Expect.isOk undoerEvents "should be ok"

            System.Threading.Thread.Sleep(timeToWait)
            let addQuantity = runAggregateCommand<Good, GoodEvents, string> good.Id eventStore messageSender addQuantityCommand
            Expect.isOk addQuantity "should be ok"
            Thread.Sleep(timeToWait)
            let goodRetrieved = supermarket.RetrieveGoodBypassingContainer good.Id |> Result.get
            Expect.equal goodRetrieved.Quantity 1 "should be the same quantity"

            let undoerEvents' = undoerEvents |> Result.get

            let undoerEventsResult = undoerEvents' () 
            Expect.isOk undoerEventsResult "should be ok"
            let result = undoerEventsResult |> Result.get
            Expect.equal result.Length 1 "should be the same quantity"

        multipleTestCase "can't apply the undoer of a command before the related command has actually been applied - Error" marketInstances <| fun (supermarket, _, setup, goodsViewer, _, _, timeToWait) ->
            setup ()
            let good = Good.MkGood (Guid.NewGuid(), "Good", 10.0m)
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
            
        multipleTestCase "add and retrieve a good bypassing the container - Ok" marketInstances <| fun (supermarket, _, setup, _, _, _, timeToWait) ->
            setup ()
            let good = Good.MkGood (Guid.NewGuid(), "Good", 10.0m)
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"
          
            Thread.Sleep(timeToWait)  
            let goodRetrieved = supermarket.RetrieveGoodBypassingContainer good.Id
            Expect.isOk goodRetrieved "should be ok"                    
                                
            Expect.equal goodRetrieved.OkValue good "should be the same good"
        
        multipleTestCase "cannot add a good that already exists. Bypass the container" marketInstances <| fun (supermarket, _, setup, _, _, _, timeToWait) ->
            setup ()
            let good = Good.MkGood (Guid.NewGuid(), "Good", 10.0m)
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"
            Thread.Sleep(timeToWait)
            let newPrice = 20.0m
            let priceChanged = supermarket.SetPrice (good.Id, newPrice)
            Expect.isOk priceChanged "should be ok"
            
            let retrieved = supermarket.RetrieveGoodBypassingContainer good.Id
            Expect.isOk retrieved "should be ok"
            let retrieved' = retrieved.OkValue
            Expect.equal retrieved'.Price newPrice "should be the same price"
       
        testCase "deserializarion test"  <| fun _ ->
            let sut =
                """
                    {"AggregateId":"9cb9ce29-fcb0-4a41-82fb-9d6132d0492d","Message":{"Case":"Events","Item":{"InitEventId":740,"EndEventId":741,"Events":[{"Case":"QuantityRemoved","Item":1}]}}}

                """
            let deserializedMessage = jsonPSerializer.Deserialize<AggregateMessage<Good, GoodEvents>> sut
            Expect.isOk deserializedMessage "should be ok"
        
    ]
    |> testSequenced
