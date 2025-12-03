module Tests

open System
open System.Threading
open System.Threading.Tasks
open DotNetEnv
open Expecto
open Sharpino
open Sharpino.Cache
open Sharpino.CommandHandler
open Sharpino.Core
open Sharpino.EventBroker
open Sharpino.PgBinaryStore
open Sharpino.RabbitMq
open Sharpino.Storage
open Sharpino.TestUtils
open ShoppingCart.CartConsumer
open ShoppingCart.GoodConsumer
open ShoppingCartBinary.Good
open ShoppingCartBinary.GoodsContainer
open ShoppingCartBinary.Cart
open ShoppingCartBinary.GoodEvents
open ShoppingCartBinary.GoodCommands
open ShoppingCartBinary.CartEvents
open ShoppingCartBinary.GoodsContainerEvents
open ShoppingCartBinary.Supermarket

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting

let setUp (eventStore: IEventStore<'F>) =

    eventStore.Reset GoodsContainer.Version GoodsContainer.StorageName
    eventStore.Reset Good.Version Good.StorageName
    eventStore.ResetAggregateStream Good.Version Good.StorageName

    eventStore.Reset Cart.Version Cart.StorageName
    eventStore.ResetAggregateStream Cart.Version Cart.StorageName

    StateCache2<GoodsContainer>.Instance.Invalidate()
    AggregateCache3.Instance.Clear()
    
let connection =
    Env.Load() |> ignore
    let password = Environment.GetEnvironmentVariable("password")
    "Server=127.0.0.1;" +
    "Database=es_shopping_cart_bin;" +
    "User Id=safe;"+
    $"Password={password};"

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

let cartConsumer =
    host.Services.GetServices<IHostedService>()
    |> Seq.find (fun s -> s.GetType() = typeof<CartConsumer>)
    :?> CartConsumer

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

let messageSenders =
    MessageSenders.MessageSender
        (fun queueName ->
            let sender = aggregateMessageSenders.TryGetValue(queueName)
            match sender with
            | true, sender -> sender |> Ok
            | _ -> (sprintf "not found %s" queueName) |> Error
        )
#endif
let eventStore = PgBinaryStore(connection)

let setupPgEventStore () =
    setUp eventStore
    ()
        
let emptyMessageSenders =
    fun queueName ->
        fun message ->
            ValueTask.CompletedTask
let goodsViewer = getAggregateStorageFreshStateViewer<Good, GoodEvents, byte[]> eventStore
let cartViewer = getAggregateStorageFreshStateViewer<Cart, CartEvents, byte[]> eventStore
let goodsContainerViewer = getStorageFreshStateViewer<GoodsContainer, GoodsContainerEvents, byte[]> eventStore

let doNothingBroker: IEventBroker<byte[]> = 
    {
        notify = None
        notifyAggregate = None
    }
    
let marketInstances =
    [
          #if RABBITMQ 
            Supermarket(eventStore, messageSenders, goodsContainerViewer, rabbitMqGoodsStateViewer, rabbitMqCartStateViewer ), "eventStorePostgres", setupPgEventStore, rabbitMqGoodsStateViewer, eventStore:> IEventStore<byte[]>, messageSenders, 200
          #else   
            Supermarket(eventStore, MessageSenders.NoSender, goodsContainerViewer, goodsViewer, cartViewer ), "eventStorePostgres", setupPgEventStore, goodsViewer  , eventStore:> IEventStore<byte[]>,
                                                                                                                                                                  MessageSenders.NoSender, 0
          #endif  
    ]
   
[<Tests>]
let tests =

    testList "samples" [
        multipleTestCase "Initial state: there are no good in a Supermarket - Ok" marketInstances <| fun (supermarket, _, setup, _, _, messageSender, delay) ->
            setup()

            let goods = supermarket.Goods

            Expect.isOk goods "should be ok"
            Expect.equal goods.OkValue [] "There are no goods in the supermarket."
        
        multipleTestCase "after added a good to the supermarket then can retrieve it - Ok" marketInstances <| fun (supermarket, _, setup, _, _, messageSender, delay) ->
            setup ()

            // given
            let good = Good.MkGood (Guid.NewGuid(), "Good", 10.0m)
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"
             
            Thread.Sleep(delay)
            // when
            let retrieved = supermarket.RetrieveGoodBypassingContainer good.Id
            Expect.isOk retrieved "should be ok"
            let retrieved' = retrieved.OkValue
            
            Thread.Sleep(delay)
            // then
            Expect.equal  retrieved'.Id good.Id "should be the same good"

        multipleTestCase "create a good and put it in the supermarket" marketInstances <| fun (supermarket, _, setup, _, _, messageSender, delay) ->
            setup ()

            let good = Good.MkGood (Guid.NewGuid(), "Good", 10.0m)
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"
            
        multipleTestCase "create apple,  put it in the supermarket and add a specific new quantity again - Ok" marketInstances <| fun (supermarket, _, setup, _, _, messageSender, delay) ->
            setup ()
            let goodId = Guid.NewGuid()
            let good = Good.MkGood (goodId, "Apple", 10.0m)
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"
            
            Thread.Sleep(delay)
            let addQuantity = supermarket.AddQuantity(goodId, 10)
            Expect.isOk addQuantity "should be ok"
            let retrievedQuantity = supermarket.GetGoodsQuantity goodId
            Expect.isOk retrievedQuantity "should be ok"

        multipleTestCase "after added a good, its quantity is zero" marketInstances <| fun (supermarket, _, setup, _, _, messageSender, delay) ->
            setup ()
            let id = Guid.NewGuid()

            // given
            let good = Good.MkGood (id, "Good", 10.0m)
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"
            
            Thread.Sleep(delay)
            // when
            let retrievedQuantity = supermarket.GetGoodsQuantity id
            Expect.isOk retrievedQuantity "should be ok"
            
            // then
            let result = retrievedQuantity.OkValue
            Expect.equal result 0 "should be the same quantity"

        multipleTestCase "Add a good. Increase its quantity, retrieve checking the quantity - Ok" marketInstances <| fun (supermarket, _, setup, _, _, messageSender, delay) ->
            setup ()
            let id = Guid.NewGuid()
            // given
            let good = Good.MkGood (id, "Good", 10.0m)
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"
            
            // when
            Thread.Sleep(delay)
            let addQuantity = supermarket.AddQuantity(id, 10)
            Expect.isOk addQuantity "should be ok"
            
            // then
            Thread.Sleep(delay)
            let retrievedQuantity = supermarket.GetGoodsQuantity id
            Expect.isOk retrievedQuantity "should be ok"
            let result = retrievedQuantity.OkValue
            Expect.equal result 10 "should be the same quantity"

        multipleTestCase "create a cart" marketInstances <| fun (supermarket, _, setup, _, _, messageSender, delay) ->
            setup ()
            // given
            let cartId = Guid.NewGuid()
            let cart = Cart.MkCart (Guid.NewGuid())
            // when
            Thread.Sleep(delay)
            let addCart = supermarket.AddCart cart
            
            // then
            Expect.isOk addCart "should be ok"

        multipleTestCase "add a good, increase its quantity and then put some of that good in a cart. The total quantity in the supermarket will be decreased - Ok" marketInstances <| fun (supermarket, _, setup, _, _, messageSender, delay) ->
            setup ()
            
            // given
            let cartId = Guid.NewGuid()
            let cart = Cart.MkCart (cartId)
            
            let cartAdded = supermarket.AddCart cart
            Expect.isOk cartAdded "should be ok"
            let good = Good.MkGood (Guid.NewGuid(), "Good", 10.0m)
            
            Thread.Sleep(delay)
            let retrievedX = supermarket.GetCart cartId
            Expect.isOk retrievedX "should be ok"
            
            // when
            let GoodAdded = supermarket.AddGood good
            Expect.isOk GoodAdded "should be ok"

            Thread.Sleep(delay)
            // then
            let addQuantity = supermarket.AddQuantity(good.Id, 10)
            Expect.isOk addQuantity "should be ok" 

            Thread.Sleep(delay)
            let addedToCart = supermarket.AddGoodToCart(cartId, good.Id, 1)
            Expect.isOk addedToCart "should be ok"
            
            Thread.Sleep(delay)
            let retrieved = supermarket.GetCart cartId
            Expect.isOk retrieved "should be ok"
            let result = retrieved.OkValue.Goods
            Expect.equal result.Count 1 "should be the same quantity"

        multipleTestCase "try adding more items than available. - Error" marketInstances <| fun (supermarket, _, setup, _, _, messageSender, delay) ->
            setup ()

            // given
            let cartId = Guid.NewGuid()
            let cart = Cart.MkCart cartId
            let cartAdded = supermarket.AddCart cart

            Expect.isOk cartAdded "should be ok"

            let good = Good.MkGood (Guid.NewGuid(), "Good", 10.0m)
            let GoodAdded = supermarket.AddGood good
            Expect.isOk GoodAdded "should be ok"

            Thread.Sleep(delay)
            let addQuantity = supermarket.AddQuantity(good.Id, 10)
            Expect.isOk addQuantity "should be ok"

            // when
            let addedToCart = supermarket.AddGoodToCart(cartId, good.Id, 11)
            
            // then
            Expect.isError addedToCart "should be an error"
            let (Error e) = addedToCart
            Expect.equal e "Quantity not available" "should be the same error"

        multipleTestCase "when adding a good into an unexisting cart will get an Error" marketInstances <| fun (supermarket, _, setup, _, _, messageSender, delay) ->
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

        multipleTestCase "try adding an unexisting good to a cart - Error" marketInstances <| fun (supermarket, _, setup, _, _, messageSender, delay) ->
            setup ()

            // given
            let cartId = Guid.NewGuid()
            let cart = Cart.MkCart (Guid.NewGuid())
            let cartAdded = supermarket.AddCart cart
            Expect.isOk cartAdded "should be ok"

            // when
            let addedToCart = supermarket.AddGoodToCart (cartId, Guid.NewGuid(), 1)
            
            // then
            Expect.isError addedToCart "should be an error" 

        multipleTestCase "add multiple goods to a cart, the goods in the supermarket will decrease by the quantity added to the cart - Ok" marketInstances <| fun (supermarket, _, setup, _, _, messageSender, delay) ->
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

            Thread.Sleep(delay)
            let addedToCart1 = supermarket.AddGoodsToCart (cartId, [(apple.Id, 1); (lemon.Id, 1)])
            Expect.isOk addedToCart1 "should be ok"

            Thread.Sleep(delay)
            let cart = supermarket.GetCart cartId
            Expect.isOk cart "should be ok"

            Thread.Sleep(delay)
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

        multipleTestCase "add multiple good to a cart, exceeding quantity by one so can't add it. Nothing changes" marketInstances <| fun (supermarket, _, setup, _, _, messageSender, delay) ->
            setup ()

            // given
            let cartId = Guid.NewGuid()
            let cart = Cart.MkCart cartId
            let cartAdded = supermarket.AddCart cart
            Expect.isOk cartAdded "should be ok"

            Thread.Sleep(delay)
            let good1 = Good.MkGood (Guid.NewGuid(), "Good1", 10.0m)
            let GoodAdded1 = supermarket.AddGood good1
            Expect.isOk GoodAdded1 "should be ok"

            Thread.Sleep(delay)
            let good2 = Good.MkGood (Guid.NewGuid(), "Good2", 10.0m)
            let GoodAdded2 = supermarket.AddGood good2
            Expect.isOk GoodAdded2 "should be ok"

            // when
            Thread.Sleep(delay)
            let _ = supermarket.AddQuantity (good1.Id, 10)
            Thread.Sleep(delay)
            let _ = supermarket.AddQuantity (good2.Id, 10)

            let addedToCart1 = supermarket.AddGoodsToCart (cartId, [(good1.Id, 11); (good2.Id, 1)])
            
            Expect.isError addedToCart1 "should be an error"

            let cart = supermarket.GetCart cartId
            Expect.isOk cart "should be ok"
            Expect.equal cart.OkValue.Goods.Count 0 "should be the same quantity"   

            Thread.Sleep(delay)
            let retrievedGood1 = supermarket.GetGoodsQuantity good1.Id
            Expect.isOk retrievedGood1 "should be ok"
            
            // then

            let result1 = retrievedGood1.OkValue
            Expect.equal result1 10 "should be the same quantity"

            Thread.Sleep(delay)
            let result2 = supermarket.GetGoodsQuantity good2.Id
            Expect.isOk result2 "should be ok"
            Expect.equal result2.OkValue 10 "should be the same quantity"

        multipleTestCase "add and a good and it's quantity will be zero - Ok" marketInstances <| fun (supermarket, _, setup, _, _, messageSender, delay) ->
            setup ()

            let cartId = Guid.NewGuid()
            // let cart = Cart(cartId, Map.empty)
            let cart = Cart.MkCart (Guid.NewGuid())
            let cartAdded = supermarket.AddCart cart
            Expect.isOk cartAdded "should be ok"

            let good = Good.MkGood (Guid.NewGuid(), "Good", 10.0m)
            Thread.Sleep(delay)
            let GoodAdded = supermarket.AddGood good
            Expect.isOk GoodAdded "should be ok"
            Thread.Sleep(delay)
            let quantity = supermarket.GetGoodsQuantity good.Id
            Expect.isOk quantity "should be ok"

            let result = quantity.OkValue
            Expect.equal result 0 "should be the same quantity"

        multipleTestCase "can't add twice a good with the same name - Error" marketInstances <| fun (supermarket, _ , setup, _, _, messageSender, delay) ->
            setup()

            let good = Good.MkGood (Guid.NewGuid(), "Good", 10.0m)
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"

            let good2 = Good.MkGood (Guid.NewGuid(), "Good", 10.0m)
            Thread.Sleep(delay)
            let addedTwice = supermarket.AddGood good2
            Expect.isError addedTwice "should be an error"

        multipleTestCase "add a good and remove it - Ok" marketInstances <| fun (supermarket,_ , setup, _, _, messageSender, delay) ->
            setup ()

            let good = Good.MkGood (Guid.NewGuid(), "Good", 10.0m)
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"
            Thread.Sleep(delay)
            let removed = supermarket.RemoveGood good.Id
            Expect.isOk removed "should be ok"

            Thread.Sleep(delay)
            let retrieved = supermarket.GetGood good.Id
            Expect.isError retrieved "should be an error"

        multipleTestCase  "when remove a good then can't gets its quantity - Error" marketInstances <| fun (supermarket, _, setup, _, _, messageSender, delay) ->
            setup ()
            let good = Good.MkGood (Guid.NewGuid(), "Good", 10.0m)
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"
            Thread.Sleep(delay)
            let removed = supermarket.RemoveGood good.Id
            Expect.isOk removed "should be ok"

            Thread.Sleep(delay)
            let quantity = supermarket.GetGoodsQuantity good.Id
            Expect.isError quantity "should be an error"

        multipleTestCase "Initial state. Add many goods and add quantity to them many times. Verify multiple events and multiple aggregate updates - Ok" marketInstances <| fun (supermarket, _, setup, _, _, messageSender, delay) ->
            setup ()
            let good1Id = Guid.NewGuid()
            let good1 = Good.MkGood (good1Id, "Good1", 10.0m)
            let added = supermarket.AddGood good1

            System.Threading.Thread.Sleep(delay)
            let quantityAdded = supermarket.AddQuantity (good1Id, 10)
            
            System.Threading.Thread.Sleep(delay)
            let quantityAdded = supermarket.AddQuantity (good1Id, 3)
            
            System.Threading.Thread.Sleep(delay)
            let quantityAdded21 = supermarket.AddQuantity (good1Id, 2)

            System.Threading.Thread.Sleep(500)
            let supermarketGoodState = supermarket.GetGood good1Id |> Result.get
            Expect.equal supermarketGoodState.Quantity 15 "should be the same state"

        multipleTestCase "retrieve the undoer of a command, apply the command, then retrieve the events from the undoer and check that they will be the events that works as the anticommand - Ok" marketInstances <| fun (supermarket, _, setup, goodsViewer, eventStore, messageSender, delay ) ->
            setup ()

            let good = Good.MkGood (Guid.NewGuid(), "Good", 10.0m)
            let goodAdded = supermarket.AddGood good

            Expect.isOk goodAdded "should be ok"

            let addQuantityCommand:AggregateCommand<Good,GoodEvents> = GoodCommands.AddQuantity 1

            Thread.Sleep(delay)
            let undoer = addQuantityCommand.Undoer
            let firstShotUndoer = undoer |> Option.get
            let undoerEvents = firstShotUndoer good goodsViewer
            Expect.isOk undoerEvents "should be ok"

            System.Threading.Thread.Sleep(delay)
            let addQuantity = runAggregateCommand<Good, GoodEvents, byte[]> good.Id eventStore messageSender addQuantityCommand
            Expect.isOk addQuantity "should be ok"
            Thread.Sleep(delay)
            let goodRetrieved = supermarket.RetrieveGoodBypassingContainer good.Id |> Result.get
            Expect.equal goodRetrieved.Quantity 1 "should be the same quantity"

            let undoerEvents' = undoerEvents |> Result.get

            let undoerEventsResult = undoerEvents' () 
            Expect.isOk undoerEventsResult "should be ok"
            let result = undoerEventsResult |> Result.get
            Expect.equal (result |> snd).Length 1 "should be the same quantity"

        multipleTestCase "can't apply the undoer of a command before the related command has actually been applied - Error" marketInstances <| fun (supermarket, _, setup, goodsViewer, _, messageSender, delay) ->
            setup ()
            let good = Good.MkGood (Guid.NewGuid(), "Good", 10.0m)
            let goodAdded = supermarket.AddGood good

            Expect.isOk goodAdded "should be ok"

            let addQuantityCommand:AggregateCommand<Good,GoodEvents> = GoodCommands.AddQuantity 1

            let undoer = addQuantityCommand.Undoer
            let firstShotUndoer = undoer |> Option.get
            System.Threading.Thread.Sleep(delay)
            let undoerEvents = firstShotUndoer good goodsViewer
            Expect.isOk undoerEvents "should be ok"

            let undoerEvents' = undoerEvents |> Result.get

            let undoerEventsResult = undoerEvents' () 
            Expect.isError undoerEventsResult "should be an error"
            
        multipleTestCase "add and retrieve a good bypassing the container - Ok" marketInstances <| fun (supermarket, _, setup, _, _, messageSender, delay) ->
            setup ()
            let good = Good.MkGood (Guid.NewGuid(), "Good", 10.0m)
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"
          
            Thread.Sleep(delay)  
            let goodRetrieved = supermarket.RetrieveGoodBypassingContainer good.Id
            Expect.isOk goodRetrieved "should be ok"                    
                                
            Expect.equal goodRetrieved.OkValue good "should be the same good"
        
        multipleTestCase "cannot add a good that already exists. Bypass the container" marketInstances <| fun (supermarket, _, setup, _, _, messageSender, delay) ->
            setup ()
            let good = Good.MkGood (Guid.NewGuid(), "Good", 10.0m)
            let added = supermarket.AddGood good
            Expect.isOk added "should be ok"
            Thread.Sleep(delay)
            let newPrice = 20.0m
            let priceChanged = supermarket.SetPrice (good.Id, newPrice)
            Expect.isOk priceChanged "should be ok"
            
            Thread.Sleep(delay)
            let retrieved = supermarket.RetrieveGoodBypassingContainer good.Id
            Expect.isOk retrieved "should be ok"
            let retrieved' = retrieved.OkValue
            Expect.equal retrieved'.Price newPrice "should be the same price"
    ]
    |> testSequenced
  
   