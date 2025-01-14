
module KafkaTests

// open ShoppingCart.Good
// open Confluent.Kafka.Admin
// open ShoppingCart.Commons
// open ShoppingCart.GoodsContainer
// open ShoppingCart.Supermarket
// open ShoppingCart.Cart
// open System
// open Sharpino.Storage
// open Sharpino.Core
// open Sharpino.KafkaReceiver
// open Sharpino.PgStorage
// open Sharpino.KafkaBroker
// open Sharpino.TestUtils
// open Sharpino.PgBinaryStore
// open Sharpino.MemoryStorage

open Expecto

// open Confluent.Kafka
// // open FsKafka
// open Tests
// open ShoppingCart.CartEvents
// open ShoppingCart.GoodEvents
// open FsToolkit.ErrorHandling
// open FsKafka
// open Sharpino.CommandHandler


// let getFromMessage<'E> value =
//     result {
//         let! okBinaryDecoded = getStrAggregateMessage value
//         let message = okBinaryDecoded.BrokerEvent
//         let actual = 
//             match message with
//                 | StrEvent x -> jsonPicklerSerializer.Deserialize<'E> x |> Result.get
//                 | BinaryEvent x -> binPicklerSerializer.Deserialize<'E> x |> Result.get
//         return actual
//     }

// let tryDeleteTopic (cliAdmin: IAdminClient) topicName =
//     try
//         cliAdmin.DeleteTopicsAsync([topicName]) |> Async.AwaitTask  |> Async.RunSynchronously
//     with
//     | _ -> 
//         printf "not deleted because does not exist\n"
//         ()

// let topicSetup () =
//     let config = new AdminClientConfig()
//     config.BootstrapServers <- "localhost:9092"
//     let adminClient = new AdminClientBuilder(config)
//     let cliAdmin = adminClient.Build()
//     let delete = tryDeleteTopic cliAdmin "good-01"
//     // printf "topics deleted %A\n" delete

//     let delete2 =  tryDeleteTopic cliAdmin "cart-01"
//     // printf "topics deleted %A\n" delete2

//     let delete3 = tryDeleteTopic cliAdmin "goodsContainer-01"
//     // printf "topics deleted %A\n" delete3

//     let log = Serilog.LoggerConfiguration().CreateLogger()
//     let batching = Batching.Linger (System.TimeSpan.FromMilliseconds 10.)
//     let producerConfig = KafkaProducerConfig.Create("MyClientIdX", "localhost:9092", Acks.All, batching)
//     let createFirstTopic = KafkaProducer.Create(log, producerConfig, "good-01")
//     let createSecondTopic = KafkaProducer.Create(log, producerConfig, "cart-01")
//     let createThirdTopic = KafkaProducer.Create(log, producerConfig, "goodsContainer-01")
//     ()

// let kafkaBroker = getKafkaBroker "localhost:9092"
// let kafkaGoodsConsumer = ConsumerX<Good, GoodEvents>(goodsTopic, "MyClientIdX", "localhost:9092", "MyGroupIdX", 4000, goodsViewer)
// let kafkaCartConsumer = ConsumerX<Cart, CartEvents>(cartTopic, "MyClientIdX", "localhost:9092", "MyGroupIdX", 4000, cartViewer)

// let kafkaMarketInstances =
//     [
//         // Supermarket(eventStorePostgres, doNothingBroker, goodsViewer, cartViewer), "eventStorePostgres", (fun () -> setUp eventStorePostgres), (fun () -> ())  ;
//         Supermarket(eventStorePostgres, kafkaBroker, goodsViewer, cartViewer, goodsContainerViewer), "eventStorePostgres", (fun () -> setUp eventStorePostgres), (fun () -> ())  ;
//         // Supermarket(eventStorePostgres, doNothingBroker, goodsViewer, cartViewer), "eventStorePostgres", (fun () -> setUp eventStorePostgres), (fun () -> ())  ;
//     ]


// [<Tests>]
let kafkaTests =
    () 
    // testList "Supermarket" [
    //     multipleTestCase "add a good to a cart, and verify events are published on the cart and on the good side - Ok" kafkaMarketInstances <| fun (supermarket, eventStore, setup, _) ->
    //         setup ()
    //         topicSetup ()
    //         let storageGoodStateViewer = getAggregateStorageFreshStateViewer<Good, GoodEvents, string> eventStorePostgres
    //         let storageCartStateViewer = getAggregateStorageFreshStateViewer<Cart, CartEvents, string> eventStorePostgres

    //         let cartId = Guid.NewGuid()
    //         printf "cart id1: %A \n" cartId
    //         let iniCart = Cart(cartId, Map.empty)
    //         let cartAdded = supermarket.AddCart iniCart
    //         Expect.isOk cartAdded "should be ok"

    //         let good1 = Good(Guid.NewGuid(), "Good1", 10.0m, [])
    //         printf "good1 id: %A \n" good1.Id
    //         let GoodAdded1 = supermarket.AddGood good1
    //         Expect.isOk GoodAdded1 "should be ok"

    //         let _ = supermarket.AddQuantity(good1.Id, 8)

    //         let addedToCart1 = supermarket.AddGoodsToCart(cartId, [(good1.Id, 1)])

    //         let cart = supermarket.GetCart cartId
    //         Expect.isOk cart "should be ok"

    //         let result = cart.OkValue.Goods
    //         Expect.equal result.Count 1 "should be the same quantity"  

    //         Expect.equal result.[good1.Id] 1 "should be the same quantity"

    //         let good1Quantity = supermarket.GetGoodsQuantity good1.Id
    //         Expect.isOk good1Quantity "should be ok"
    //         Expect.equal good1Quantity.OkValue 7 "should be the same quantity"

    //         let topic = (Good.StorageName + "-" + Good.Version).Replace("_", "")
    //         printf "topic %s\n" topic

    //         let goodConsumer = ConsumerX<Good, GoodEvents>(topic, "MyClientIdX", "localhost:9092", "MyGroupIdX", 4000, storageGoodStateViewer)
            
    //         goodConsumer.Consuming()

    //         let binaryDecoded = getStrAggregateMessage goodConsumer.GMessages.[0].Message.Value

    //         Expect.isOk binaryDecoded "should be ok"
    //         let okBinaryDecoded = binaryDecoded.OkValue
    //         Expect.equal okBinaryDecoded.AggregateId good1.Id "should be the same id"

    //         let binaryDecodeds = goodConsumer.GetMessages |> Result.get
    //         Expect.equal binaryDecodeds.[0].AggregateId good1.Id "should be the same id"

    //         let cartTopic = (Cart.StorageName + "-" + Cart.Version).Replace("_", "")
    //         let cartConsumer = ConsumerX<Cart, CartEvents>(cartTopic, "MyClientIdX", "localhost:9092", "MyGroupIdX", 4000, storageCartStateViewer)

    //         cartConsumer.Consuming()

    //         let binaryDecodedCartMessage = getStrAggregateMessage cartConsumer.GMessages.[0].Message.Value
    //         Expect.isOk binaryDecodedCartMessage "should be ok"
    //         let okBinaryDecodedCartMessage = binaryDecodedCartMessage.OkValue
    //         Expect.equal okBinaryDecodedCartMessage.AggregateId cartId "should be the same id"

    //     // FOCUS
    //     // multipleTestCase "add a good to a cart and  decript the events - Ok"  kafkaMarketInstances <| fun (supermarket, eventStore, setup, _) ->
    //     //     setup ()
    //     //     topicSetup ()
    //     //     let storageGoodStateViewer = getAggregateStorageFreshStateViewer<Good, GoodEvents, string> eventStorePostgres
    //     //     let storageCartStateViewer = getAggregateStorageFreshStateViewer<Cart, CartEvents, string> eventStorePostgres
            
    //     //     // prepare the good adding it to the cart
    //     //     let cartId = Guid.NewGuid ()
    //     //     let cart = Cart (cartId, Map.empty)
    //     //     let cartAdded = supermarket.AddCart cart
    //     //     Expect.isOk cartAdded "should be ok"

    //     //     let good1 = Good (Guid.NewGuid(), "Good1", 10.0m, [])
    //     //     let GoodAdded1 = supermarket.AddGood good1
    //     //     Expect.isOk GoodAdded1 "should be ok"

    //     //     let addToSupermarket = supermarket.AddQuantity (good1.Id, 8)
    //     //     Expect.isOk addToSupermarket "should be ok"

    //     //     let addedToCart1 = supermarket.AddGoodToCart (cartId, good1.Id, 1)
    //     //     Expect.isOk addedToCart1 "should be ok"

    //     //     // now verify that the events are published on the good and cart side
    //     //     let topic = (Good.StorageName + "-" + Good.Version).Replace("_", "")
    //     //     let goodConsumer = ConsumerX<Good, GoodEvents>(topic, "MyClientIdX", "localhost:9092", "MyGroupIdX", 4000, storageGoodStateViewer)

    //     //     goodConsumer.Consuming()

    //     //     let actualGoodEvents = goodConsumer.GetEvents |> Result.get |> Set.ofList

    //     //     let expected = GoodEvents.QuantityAdded 8
    //     //     let expected2 = GoodEvents.QuantityRemoved 1

    //     //     let expected = Set.ofList [expected; expected2]

    //     //     Expect.equal actualGoodEvents expected "should be the same event"

    //     //     let cartTopic = (Cart.StorageName + "-" + Cart.Version).Replace("_", "")
    //     //     let cartConsumer = ConsumerX<Cart, CartEvents> (cartTopic, "MyClientIdX", "localhost:9092", "MyGroupIdX", 4000, storageCartStateViewer)

    //     //     cartConsumer.Consuming()

    //     //     let expected = CartEvents.GoodAdded (good1.Id, 1)

    //     //     let actual = cartConsumer.GetEvents |> Result.get |> List.head

    //     //     Expect.equal actual expected "should be the same event"

    //     // multipleTestCase "add two goods into a card - Ok" kafkaMarketInstances <| fun (supermarket, eventStore, setup, _) ->

    //     //     setup ()
    //     //     topicSetup ()
    //     //     let storageGoodStateViewer = getAggregateStorageFreshStateViewer<Good, GoodEvents, string> eventStorePostgres
    //     //     let storageCartStateViewer = getAggregateStorageFreshStateViewer<Cart, CartEvents, string> eventStorePostgres

    //     //     let cartId = Guid.NewGuid ()
    //     //     let cart = Cart (cartId, Map.empty)
    //     //     let cartAdded = supermarket.AddCart cart
    //     //     Expect.isOk cartAdded "should be ok"

    //     //     let good1 = Good (Guid.NewGuid(), "Good1", 10.0m, [])
    //     //     let GoodAdded1 = supermarket.AddGood good1
    //     //     Expect.isOk GoodAdded1 "should be ok"

    //     //     let good2 = Good (Guid.NewGuid(), "Good2", 20.0m, [])
    //     //     let GoodAdded2 = supermarket.AddGood good2
    //     //     Expect.isOk GoodAdded2 "should be ok"

    //     //     let addToSupermarket1 = supermarket.AddQuantity (good1.Id, 8)
    //     //     Expect.isOk addToSupermarket1 "should be ok"

    //     //     let addToSupermarket2 = supermarket.AddQuantity (good2.Id, 5)
    //     //     Expect.isOk addToSupermarket2 "should be ok"

    //     //     let addedToCart1 = supermarket.AddGoodsToCart (cartId, [(good1.Id, 2); (good2.Id, 1)])
    //     //     Expect.isOk addedToCart1 "should be ok"

    //     //     let topic = (Good.StorageName + "-" + Good.Version).Replace("_", "")

    //     //     let consumer = ConsumerX<Good, GoodEvents> (topic, "MyClientIdX", "localhost:9092", "MyGroupIdX", 4000, storageGoodStateViewer)
    //     //     consumer.Consuming()

    //     //     let actuals = consumer.GetEvents |> Result.get |> Set.ofList
    //     //     let expected1 = GoodEvents.QuantityAdded 8
    //     //     let expected2 = GoodEvents.QuantityAdded 5
    //     //     let expected3 = GoodEvents.QuantityRemoved 2
    //     //     let expected4 = GoodEvents.QuantityRemoved 1

    //     //     let expected = Set.ofList [expected1; expected2; expected3; expected4]
    //     //     Expect.equal actuals expected "should be the same events"

    //     //     let cartTopic = (Cart.StorageName + "-" + Cart.Version).Replace("_", "")
    //     //     let cartConsumer = ConsumerX<Cart, CartEvents> (cartTopic, "MyClientIdX", "localhost:9092", "MyGroupIdX", 4000, storageCartStateViewer)
    //     //     cartConsumer.Consuming()

    //     //     let expected1 = CartEvents.GoodAdded (good1.Id, 2)
    //     //     let expected2 = CartEvents.GoodAdded (good2.Id, 1)
    //     //     let expecteds = [expected1; expected2] |> Set.ofList

    //     //     let actuals = cartConsumer.GetEvents |> Result.get |> Set.ofList
    //     //     Expect.equal expecteds actuals "should be the same events"

    //     multipleTestCase "add one good twice and another good once, get related events by the consumer - Ok" kafkaMarketInstances <| fun (supermarket, eventStore, setup, _) ->

    //         setup ()
    //         topicSetup ()
    //         let storageGoodStateViewer = getAggregateStorageFreshStateViewer<Good, GoodEvents, string> eventStorePostgres
    //         let storageCartStateViewer = getAggregateStorageFreshStateViewer<Cart, CartEvents, string> eventStorePostgres

    //         let cartId = Guid.NewGuid ()
    //         let cart = Cart (cartId, Map.empty)
    //         let cartAdded = supermarket.AddCart cart
    //         Expect.isOk cartAdded "should be ok"

    //         let good1Id = Guid.NewGuid()
    //         let good1 = Good (good1Id, "Good1", 10.0m, [])
    //         let GoodAdded1 = supermarket.AddGood good1
    //         Expect.isOk GoodAdded1 "should be ok"

    //         let good2Id = Guid.NewGuid()
    //         let good2 = Good (good2Id, "Good2", 20.0m, [])
    //         let GoodAdded2 = supermarket.AddGood good2
    //         Expect.isOk GoodAdded2 "should be ok"

    //         let addToSupermarket1 = supermarket.AddQuantity (good1.Id, 8)
    //         Expect.isOk addToSupermarket1 "should be ok"

    //         let addToSupermarket12 = supermarket.AddQuantity (good1.Id, 13)
    //         Expect.isOk addToSupermarket1 "should be ok"

    //         let addToSupermarket2 = supermarket.AddQuantity (good2.Id, 5)
    //         Expect.isOk addToSupermarket2 "should be ok"

    //         let expected1 = [GoodEvents.QuantityAdded 8; GoodEvents.QuantityAdded 13]
    //         let expected2 = [GoodEvents.QuantityAdded 5]

    //         let topic = (Good.StorageName + "-" + Good.Version).Replace("_", "")
    //         let consumer = ConsumerX<Good, GoodEvents> (topic, "MyClientIdX", "localhost:9092", "MyGroupIdX", 7000, storageGoodStateViewer)

    //         consumer.Consuming()

    //         let actual1 = consumer.GetEventsByAggregate good1Id |> Result.get 
    //         Expect.equal actual1 expected1 "should be the same events"
    //         let actual2 = consumer.GetEventsByAggregate good2Id |> Result.get
    //         Expect.equal actual2 expected2 "should be the same events"

    //     multipleTestCase "add one good twice and another good once, get related events by the consumer. Compute the evolve - Ok" kafkaMarketInstances <| fun (supermarket, eventStore, setup, _) ->

    //         setup ()
    //         topicSetup ()
    //         let storageGoodStateViewer = getAggregateStorageFreshStateViewer<Good, GoodEvents, string> eventStorePostgres
    //         let storageCartStateViewer = getAggregateStorageFreshStateViewer<Cart, CartEvents, string> eventStorePostgres

    //         let cartId = Guid.NewGuid ()
    //         let cart = Cart (cartId, Map.empty)
    //         let cartAdded = supermarket.AddCart cart
    //         Expect.isOk cartAdded "should be ok"

    //         let good1Id = Guid.NewGuid()
    //         let good1 = Good (good1Id, "Good1", 10.0m, [])
    //         let GoodAdded1 = supermarket.AddGood good1
    //         Expect.isOk GoodAdded1 "should be ok"

    //         let good2Id = Guid.NewGuid()
    //         let good2 = Good (good2Id, "Good2", 20.0m, [])
    //         let GoodAdded2 = supermarket.AddGood good2
    //         Expect.isOk GoodAdded2 "should be ok"

    //         let addToSupermarket1 = supermarket.AddQuantity (good1.Id, 9)
    //         Expect.isOk addToSupermarket1 "should be ok"

    //         let addToSupermarket12 = supermarket.AddQuantity (good1.Id, 11)
    //         Expect.isOk addToSupermarket12 "should be ok"

    //         let addToSupermarket13 = supermarket.AddQuantity (good1.Id, 17)
    //         Expect.isOk addToSupermarket13 "should be ok"

    //         let addToSupermarket21 = supermarket.AddQuantity (good2.Id, 5)
    //         let addToSupermarket22 = supermarket.AddQuantity (good2.Id, 10)

    //         let topic = (Good.StorageName + "-" + Good.Version).Replace("_", "")
    //         let consumer = ConsumerX<Good, GoodEvents> (topic, "MyClientIdQ", "localhost:9092", "MyGroupIdQ", 7000, storageGoodStateViewer)

    //         let expected1 = [GoodEvents.QuantityAdded 9; GoodEvents.QuantityAdded 11; GoodEvents.QuantityAdded 17]
    //         let expected2 = [GoodEvents.QuantityAdded 5; GoodEvents.QuantityAdded 10]

    //         consumer.Consuming()

    //         let actual1 = consumer.GetEventsByAggregate good1Id |> Result.get 
    //         Expect.equal actual1 expected1 "should be the same events"

    //         let good1State = actual1 |> evolve good1 |> Result.get
    //         let actualGood1State = supermarket.GetGood good1Id |> Result.get
    //         Expect.equal good1State.Quantity actualGood1State.Quantity "should be the same state"

    //         let actual2 = consumer.GetEventsByAggregate good2Id |> Result.get
    //         Expect.equal actual2 expected2 "should be the same events"
    //         let good2State = actual2 |> evolve good2 |> Result.get
    //         let actualGood2State = supermarket.GetGood good2Id |> Result.get
    //         Expect.equal good2State.Quantity actualGood2State.Quantity "should be the same state"

    //     multipleTestCase "initial state when no events are issued is the one provided by the backup state viewer - Ok" kafkaMarketInstances <| fun (supermarket, eventStore, setup, _) ->

    //         setup ()
    //         topicSetup ()
    //         let storageGoodStateViewer = getAggregateStorageFreshStateViewer<Good, GoodEvents, string> eventStorePostgres

    //         let good1Id = Guid.NewGuid()
    //         let good1 = Good (good1Id, "Good1", 10.0m, [])

    //         let GoodAdded = supermarket.AddGood good1

    //         let topic = (Good.StorageName + "-" + Good.Version).Replace("_", "")
    //         let consumer = ConsumerX<Good, GoodEvents> (topic, "MyClientIdQ", "localhost:9092", "MyGroupIdQ", 7000, storageGoodStateViewer)

    //         let result = consumer.GetState good1Id |> Result.get |> snd
    //         Expect.equal result good1 "should be the same state"

    //     // WORK IN PROGRESS
    //     multipleTestCase "initial state. the number of items is zero - Ok" marketInstances <| fun (supermarket, eventStore, setup, _) ->

    //         setup ()
    //         topicSetup ()
    //         let storageGoodStateViewer = getAggregateStorageFreshStateViewer<Good, GoodEvents, string> eventStorePostgres

    //         let good1Id = Guid.NewGuid()
    //         let good1 = Good (good1Id, "Good1", 10.0m, [])

    //         let GoodAdded = supermarket.AddGood good1

    //         let topic = (Good.StorageName + "-" + Good.Version).Replace("_", "")
    //         let consumer = ConsumerX<Good, GoodEvents> (topic, "MyClientIdQ", "localhost:9092", "MyGroupIdQ", 7000, storageGoodStateViewer)

    //         let result = consumer.GetState good1Id |> Result.get |> snd
    //         Expect.equal result.Quantity 0 "should be the same state"

    //     multipleTestCase "Initial state.  Add a quanty. Verify that the quantity retrieved by consumer changed accordingly - Ok" kafkaMarketInstances <| fun (supermarket, eventStore, setup, _) ->

    //         setup ()
    //         topicSetup ()
    //         let storageGoodStateViewer = getAggregateStorageFreshStateViewer<Good, GoodEvents, string> eventStorePostgres

    //         let good1Id = Guid.NewGuid()
    //         let good1 = Good (good1Id, "Good1", 10.0m, [])
    //         let topic = (Good.StorageName + "-" + Good.Version).Replace("_", "")

    //         let consumer = ConsumerX<Good, GoodEvents> (topic, "MyClientIdQ", "localhost:9092", "MyGroupIdQ", 7000, storageGoodStateViewer)
    //         let GoodAdded = supermarket.AddGood good1
    //         let result = consumer.GetState good1Id |> Result.get |> snd
    //         Expect.equal result.Quantity 0 "should be the same state"

    //         consumer.Update()

    //         let quantityAdded = supermarket.AddQuantity (good1Id, 10)

    //         let result2 = consumer.GetState good1Id |> Result.get |> snd
    //         Expect.equal result2.Quantity 10 "should be the same state"

    //     multipleTestCase "Initial state.  Add a quanty. Verify that the quantity retrieved by consumer changed accordingly, then add quantities twice and verify - Ok" kafkaMarketInstances <| fun (supermarket, eventStore, setup, _)  ->

    //         setup ()
    //         topicSetup ()
    //         let storageGoodStateViewer = getAggregateStorageFreshStateViewer<Good, GoodEvents, string> eventStorePostgres

    //         let good1Id = Guid.NewGuid()
    //         let good1 = Good (good1Id, "Good1", 10.0m, [])
    //         let topic = (Good.StorageName + "-" + Good.Version).Replace("_", "")

    //         let consumer = ConsumerX<Good, GoodEvents> (topic, "MyClientIdQ", "localhost:9092", "MyGroupIdQ", 15000, storageGoodStateViewer)
    //         let GoodAdded = supermarket.AddGood good1
    //         let result = consumer.GetState good1Id |> Result.get |> snd
    //         Expect.equal result.Quantity 0 "should be the same state"

    //         consumer.Update()

    //         let quantityAdded = supermarket.AddQuantity (good1Id, 10)

    //         Async.Sleep 1000 |> Async.RunSynchronously

    //         let result2 = consumer.GetState good1Id |> Result.get |> snd
    //         Expect.equal result2.Quantity 10 "should be the same state"

    //         let quantityAdded2 = supermarket.AddQuantity (good1Id, 5)
    //         let quantityAdded3 = supermarket.AddQuantity (good1Id, 7)
    //         consumer.Update()

    //         let quantityAdded4 = supermarket.AddQuantity (good1Id, 10)
    //         consumer.Update()

    //         let result3 = consumer.GetState good1Id |> Result.get |> snd
    //         Expect.equal result3.Quantity 32 "should be the same state"

    //     multipleTestCase "Initial state. no events - Ok" kafkaMarketInstances <| fun (supermarket, eventStore, setup, _) ->
    //         setup ()
    //         topicSetup ()
    //         let storageGoodStateViewer = getAggregateStorageFreshStateViewer<Good, GoodEvents, string> eventStorePostgres

    //         let good1Id = Guid.NewGuid()
    //         let good1 = Good (good1Id, "Good1", 10.0m, [])
    //         let added = supermarket.AddGood good1
    //         let topic = (Good.StorageName + "-" + Good.Version).Replace("_", "")
    //         let consumer = ConsumerX<Good, GoodEvents> (topic, "MyClientIdQ", "localhost:9092", "MyGroupIdQ", 15000, storageGoodStateViewer)
    //         let state = consumer.GetState good1Id |> Result.get |> snd
    //         Expect.equal state good1 "should be the same state"

    //     multipleTestCase "Initial state. no explicit update with one event, state is stale - Error " kafkaMarketInstances <| fun (supermarket, eventStore, setup, _) ->
    //         setup ()
    //         topicSetup ()
    //         let storageGoodStateViewer = getAggregateStorageFreshStateViewer<Good, GoodEvents, string> eventStorePostgres

    //         let good1Id = Guid.NewGuid()
    //         let good1 = Good (good1Id, "Good1", 10.0m, [])
    //         let added = supermarket.AddGood good1
    //         let topic = (Good.StorageName + "-" + Good.Version).Replace("_", "")
    //         let consumer = ConsumerX<Good, GoodEvents> (topic, "MyClientIdQ", "localhost:9092", "MyGroupIdQ", 15000, storageGoodStateViewer)
    //         let stateFirst = consumer.GetState good1Id |> Result.get |> snd
    //         let quantityAdded = supermarket.AddQuantity (good1Id, 10)
    //         let supermarketState = supermarket.GetGood good1Id |> Result.get
    //         let state = consumer.GetState good1Id |> Result.get |> snd

    //         printf "----- bla bla bla state %A\n" state.Quantity

    //         Expect.notEqual state supermarketState "should be the same state"

    //     multipleTestCase "Initial state. There is an explicit update with one event, state is fresh - Ok " kafkaMarketInstances <| fun (supermarket, eventStore, setup, _) ->
    //         setup ()
    //         topicSetup ()
    //         let storageGoodStateViewer = getAggregateStorageFreshStateViewer<Good, GoodEvents, string> eventStorePostgres

    //         let good1Id = Guid.NewGuid()
    //         let good1 = Good (good1Id, "Good1", 10.0m, [])
    //         let added = supermarket.AddGood good1
    //         let topic = (Good.StorageName + "-" + Good.Version).Replace("_", "")
    //         let consumer = ConsumerX<Good, GoodEvents> (topic, "MyClientIdQ", "localhost:9092", "MyGroupIdQ", 15000, storageGoodStateViewer)
    //         let stateFirst = consumer.GetState good1Id |> Result.get |> snd
    //         let quantityAdded = supermarket.AddQuantity (good1Id, 10)
    //         let supermarketState = supermarket.GetGood good1Id |> Result.get
    //         printf "1000. before update\n"
    //         consumer.Update()
    //         printf "2000. after update\n"
    //         let state = consumer.GetState good1Id |> Result.get |> snd
    //         Expect.equal state supermarketState "should be the same state"

    //     multipleTestCase "Initial state. There is an explicit update with one event, then another event. State is computed by evolve  - Ok" kafkaMarketInstances <| fun (supermarket, eventStore, setup, _) ->
    //         setup ()
    //         topicSetup ()
    //         let storageGoodStateViewer = getAggregateStorageFreshStateViewer<Good, GoodEvents, string> eventStorePostgres

    //         let good1Id = Guid.NewGuid()
    //         let good1 = Good (good1Id, "Good1", 10.0m, [])
    //         let added = supermarket.AddGood good1
    //         let topic = (Good.StorageName + "-" + Good.Version).Replace("_", "")
    //         let consumer = ConsumerX<Good, GoodEvents> (topic, "MyClientIdQ", "localhost:9092", "MyGroupIdQ", 15000, storageGoodStateViewer)

    //         let stateFirst = consumer.GetState good1Id |> Result.get |> snd
    //         let quantityAdded = supermarket.AddQuantity (good1Id, 10)
    //         consumer.Update() // will create the first state

    //         let quantityAdded2 = supermarket.AddQuantity (good1Id, 5)

    //         let supermarketState = supermarket.GetGood good1Id |> Result.get
    //         printf "1000. before update\n"

    //         consumer.Update() // wil compute the event

    //         printf "2000. after update\n"
    //         let state = consumer.GetState good1Id |> Result.get |> snd
    //         Expect.equal state supermarketState "should be the same state"

    //     multipleTestCase "Initial state. There is an explicit update with one event, then another event. State is computed by evolve  of a list of two - Ok" kafkaMarketInstances <| fun (supermarket, eventStore, setup, _) ->
    //         setup ()
    //         topicSetup ()
    //         let storageGoodStateViewer = getAggregateStorageFreshStateViewer<Good, GoodEvents, string> eventStorePostgres

    //         let good1Id = Guid.NewGuid()
    //         let good1 = Good (good1Id, "Good1", 10.0m, [])
    //         let added = supermarket.AddGood good1
    //         let topic = (Good.StorageName + "-" + Good.Version).Replace("_", "")
    //         let consumer = ConsumerX<Good, GoodEvents> (topic, "MyClientIdQ", "localhost:9092", "MyGroupIdQ", 15000, storageGoodStateViewer)

    //         let stateFirst = consumer.GetState good1Id |> Result.get |> snd
    //         let quantityAdded = supermarket.AddQuantity (good1Id, 10)
    //         consumer.Update() // will create the first state

    //         let quantityAdded2 = supermarket.AddQuantity (good1Id, 5)
    //         let quantityAdded3 = supermarket.AddQuantity (good1Id, 3)
    //         let quantityAdded4 = supermarket.AddQuantity (good1Id, 2)

    //         let supermarketState = supermarket.GetGood good1Id |> Result.get
    //         Expect.equal supermarketState.Quantity 20 "should be the same state"
    //         printf "1000. before update\n"
    //         // expect to see two events in the log
    //         consumer.Update() // wil compute the event
    //         printf "2000. after update\n"
    //         let state = consumer.GetState good1Id |> Result.get |> snd
    //         Expect.equal state supermarketState "should be the same state"

    //     multipleTestCase "Initial state. Add many goods and add quantity to them many times. Verify multiple events and multiple aggregate updates - Ok" kafkaMarketInstances <| fun (supermarket, eventStore, setup, _) ->
    //         setup ()
    //         topicSetup ()
    //         let storageGoodStateViewer = getAggregateStorageFreshStateViewer<Good, GoodEvents, string> eventStorePostgres

    //         let good1Id = Guid.NewGuid()
    //         printf "good1 id: %A\n\n" good1Id
    //         let good1 = Good (good1Id, "Good1", 10.0m, [])
    //         let added = supermarket.AddGood good1

    //         let good2Id = Guid.NewGuid()
    //         printf "good2 id: %A\n\n" good2Id
    //         let good2 = Good (good2Id, "Good2", 20.0m, [])
    //         let added2 = supermarket.AddGood good2
    //         let good3Id = Guid.NewGuid()
    //         printf "good3 id: %A\n\n" good3Id
    //         let good3 = Good (good3Id, "Good3", 30.0m, [])
    //         let added3 = supermarket.AddGood good3

    //         let topic = (Good.StorageName + "-" + Good.Version).Replace("_", "")
    //         let consumer = ConsumerX<Good, GoodEvents> (topic, "MyClientIdQ", "localhost:9092", "MyGroupIdQ", 15000, storageGoodStateViewer)

    //         let stateFirst = consumer.GetState good1Id |> Result.get |> snd
    //         let quantityAdded = supermarket.AddQuantity (good1Id, 10)
    //         let quantityAdded21 = supermarket.AddQuantity (good2Id, 2)
    //         let quantityAdded22 = supermarket.AddQuantity (good3Id, 99)
    //         consumer.Update()

    //         // to populate the initial hash
    //         let _ = consumer.GetState good1Id 
    //         let _ = consumer.GetState good2Id 
    //         let _ = consumer.GetState good3Id


    //         let quantityAdded3 = supermarket.AddQuantity (good1Id, 3)
    //         let quantityAdded4 = supermarket.AddQuantity (good1Id, 2)
    //         let quantityAdded2 = supermarket.AddQuantity (good1Id, 5)

    //         let quantityAdded21 = supermarket.AddQuantity (good2Id, 2)
    //         let quantityAdded31 = supermarket.AddQuantity (good2Id, 5)
    //         let quantityAdded41 = supermarket.AddQuantity (good2Id, 6)
    //         let quantityAdded51 = supermarket.AddQuantity (good2Id, 7)

    //         let quantityAdded32 = supermarket.AddQuantity (good3Id, 51)
    //         let quantityAdded42 = supermarket.AddQuantity (good3Id, 69)
    //         let quantityAdded52 = supermarket.AddQuantity (good3Id, 73)
    //         let quantityAdded62 = supermarket.AddQuantity (good3Id, 99)

    //         let supermarketGoodState = supermarket.GetGood good1Id |> Result.get
    //         printf "before second update\n"
    //         consumer.Update() // wil compute the event
    //         printf "after second update\n"

    //         Expect.equal supermarketGoodState.Quantity 20 "should be the same state"

    //         // let supermarketGoodState2 = supermarket.GetGood good2Id |> Result.get
    //         // let supermarketGoodState3 = supermarket.GetGood good3Id |> Result.get

    //         // printf "1000. before update\n"
    //         // // expect to see two events in the log
    //         // consumer.Update() // wil compute the event

    //         // printf "2000. after update\n"
    //         // let state = consumer.GetState good1Id |> Result.get |> snd
    //         // Expect.equal state supermarketGoodState "should be the same state"

    //         // let state2 = consumer.GetState good2Id |> Result.get |> snd
    //         // Expect.equal state2 supermarketGoodState2 "should be the same state"

    //         // let state3 = consumer.GetState good3Id |> Result.get |> snd
    //         // Expect.equal state3 supermarketGoodState3 "should be the same state"
            



    // ]
    // |> testSequenced