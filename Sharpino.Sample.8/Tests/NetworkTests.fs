module Sharpino.TransportTycoon.Tests

open System
open System.Threading
open System.Threading.Tasks
open Sharpino.EventBroker
open Sharpino.RabbitMq
open Sharpino.Storage
open Sharpino.TransportTycoon
open Sharpino.TransportTycoon.Definitions
open Sharpino.TransportTycoon.Site
open Sharpino.TransportTycoon.SiteEvents
open Sharpino.TransportTycoon.SiteCommands

// open Sharpino.TransportTycoon.Network
// open Sharpino.TransportTycoon.NetworkEvents
// open Sharpino.TransportTycoon.NetworkCommands

open Sharpino.TransportTycoon.Transporter
open Sharpino.Core
open Sharpino.PgStorage
open Sharpino.TestUtils
open Expecto
open Sharpino.MemoryStorage
open Sharpino.CommandHandler
open Sharpino.Cache
open DotNetEnv
open Sharpino.TransportTycoon.Definitions
open DotNetEnv
open Sharpino.TransportTycoon.TransportTycoon
open Sharpino.TransportTycoon.Truck.Consumer
open Sharpino.TransportTycoon.TruckEvents
open Sharpino.TransportTycoon.SiteConsumer

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting

let setUp (eventStore: IEventStore<'F>) =
    eventStore.Reset Site.Version Site.StorageName
    eventStore.Reset Transporter.Version Transporter.StorageName
    AggregateCache2.Instance.Clear ()
    AggregateCache2.Instance.Clear ()
          
let connection = 
        Env.Load() |> ignore
        let password = Environment.GetEnvironmentVariable("password")

        "Server=127.0.0.1;" +
        "Database=transport_tycoon;" +
        "User Id=safe;"+
        $"Password={password};"

let emptyMessageSender =
    fun queueName ->
        fun message ->
            ValueTask.CompletedTask
            
let eventStoreMemory: IEventStore<string> = MemoryStorage ()
let eventStorePg: IEventStore<string> = PgEventStore connection

let siteViewerMemory = getAggregateStorageFreshStateViewer<Site, SiteEvents, string> eventStoreMemory
let siteViewerPg = getAggregateStorageFreshStateViewer<Site, SiteEvents, string> eventStorePg

let truckViewerMemory = getAggregateStorageFreshStateViewer<Transporter, TruckEvents, string> eventStoreMemory
let truckViewerPg = getAggregateStorageFreshStateViewer<Transporter, TruckEvents, string> eventStorePg

let memoryTransportTycoon = TransportTycoon (eventStoreMemory, emptyMessageSender, siteViewerMemory, truckViewerMemory)
let pgTransportTycoon = TransportTycoon (eventStorePg, emptyMessageSender, siteViewerPg, truckViewerPg)

#if RABBITMQ
 
let hostBuilder = 
    Host.CreateDefaultBuilder()
        .ConfigureServices(fun (services: IServiceCollection) ->
            services.AddSingleton<RabbitMqReceiver>() |> ignore
            services.AddHostedService<TransporterConsumer>() |> ignore
            services.AddHostedService<SiteConsumer>() |> ignore
            ()
        )
        
let host = hostBuilder.Build()

// Start the host in the background
let hostTask = host.StartAsync()

let services = host.Services


let goodConsumer =
    host.Services.GetServices<IHostedService>()
    |> Seq.find (fun s -> s.GetType() = typeof<SiteConsumer>)
    :?> SiteConsumer

let cartConsumer =
    host.Services.GetServices<IHostedService>()
    |> Seq.find (fun s -> s.GetType() = typeof<TransporterConsumer>)
    :?> TransporterConsumer

let rabbitMqTransportStateViewer = goodConsumer.GetAggregateState
let rabbitMqTruckStateViewer = cartConsumer.GetAggregateState

let aggregateMessageSenders = System.Collections.Generic.Dictionary<string, MessageSender>()

let siteMessageSender =
    mkMessageSender "127.0.0.1" "_01_site" |> Result.get

let truckMessageSender =
    mkMessageSender "127.0.0.1" "_01_truck" |> Result.get

aggregateMessageSenders.Add("_01_site", siteMessageSender)
aggregateMessageSenders.Add("_01_truck", truckMessageSender)

let messageSender =
    fun queueName ->
        let sender = aggregateMessageSenders.TryGetValue(queueName)
        match sender with
        | true, sender -> sender
        | _ -> failwith "not found XX"

let pgRabbitMqTransportTycoon = TransportTycoon (eventStorePg, messageSender, rabbitMqTransportStateViewer, rabbitMqTruckStateViewer)

#endif

let transportTycoons =
    [
        #if RABBITMQ
            pgRabbitMqTransportTycoon, (fun () -> setUp eventStorePg), 100
        #else
            pgTransportTycoon, (fun () -> setUp eventStorePg), 0
        #endif
    ]

let testDataIds = 
    {|
        PortId = Guid.Parse "eece581d-f873-4fe0-885e-c832f3c7d453"
        FactoryId = Guid.Parse "a5897d66-c3be-47e3-8c15-3ee603cd3a0b"
        NodeAId = Guid.Parse "c4bc3d10-5deb-46ff-ac77-df4c070abbfc"
        NodeBId = Guid.Parse "9d177ee8-eb33-4d39-be2e-62eac6de5cd1"
        Truck1Id = Guid.Parse "ff21dc22-95b4-4b05-8eaf-6ae074d754f2"
        Truck2Id = Guid.Parse "55077d7a-26ae-44af-854d-c15ce95219df"
    |}

let seedDefaultNetwork (transportTycoon: TransportTycoon) = 
    result {
        let factory = Site.MkSite (testDataIds.FactoryId, SiteType.Factory)
        let port = Site.MkSite (testDataIds.PortId, SiteType.Port)
        let nodeA = Site.MkSite (testDataIds.NodeAId, SiteType.Destination "A")
        let nodeB = Site.MkSite (testDataIds.NodeBId, SiteType.Destination "B")
        let truck1 = Transporter.Transporter.MkTransporter (testDataIds.Truck1Id, "A", nodeA.Id)
        let truck2 = Transporter.Transporter.MkTransporter (testDataIds.Truck2Id, "B", nodeB.Id)

        do! transportTycoon.AddSite factory
        do! transportTycoon.AddSite port
        do! transportTycoon.AddSite nodeA
        do! transportTycoon.AddSite nodeB

        do! transportTycoon.AddTruck truck1
        do! transportTycoon.AddTruck truck2
        do! transportTycoon.PlaceTruckOnSite (truck1.Id, factory.Id)
        do! transportTycoon.PlaceTruckOnSite (truck2.Id, factory.Id)
        do! transportTycoon.ConnectSitesByRoad factory.Id port.Id factory.Id nodeA.Id 1
        do! transportTycoon.ConnectSitesBySea port.Id nodeA.Id factory.Id nodeA.Id 1
        do! transportTycoon.ConnectSitesByRoad factory.Id nodeB.Id factory.Id nodeB.Id 5
        return ()
    }

[<Tests>]
let tests =
    testList "samples" [
        multipleTestCase "initial state of transportTycoon: the list of sites is empty - Ok" transportTycoons <| fun (transportTycoon, setUp, delay) ->
            // given
            setUp ()
            let currentSites = transportTycoon.Sites ()
            Expect.isOk currentSites "should be ok"
            
            // then
            let sitesValue = currentSites.OkValue
            Expect.equal 0 sitesValue.Length "should be 0"
            
        multipleTestCase "add a site to the network - Ok" transportTycoons <| fun (transportTycoon, setUp, delay) ->
            // given
            // setUp ()
            let site = Site.MkSite (Guid.NewGuid(), SiteType.Factory)
            // when
            let siteAdded = transportTycoon.AddSite site
            Expect.isOk siteAdded "should be ok"
            
            Thread.Sleep (delay)
            let retrieveSite = transportTycoon.GetSite site.Id
            Expect.isOk retrieveSite "should be ok"
            Thread.Sleep (delay)
            let retrieveSiteVal = retrieveSite.OkValue
            Expect.equal site retrieveSiteVal "should be equal"
            
        multipleTestCase "cannot retrieve an unexisting site - Error"  transportTycoons <| fun (transportTycoon, setUp, delay) ->
            // given
            setUp ()
            let siteRef = Guid.NewGuid()
            // when
            Thread.Sleep (delay)
            let retrieveSite = transportTycoon.GetSite siteRef
            // then
            Expect.isError retrieveSite "should be an error"
           
        multipleTestCase "add and retrieve a truck - Ok" transportTycoons <| fun (transportTycoon, setUp, delay) ->      
            // given
            setUp ()
            let truck = Transporter.Transporter.MkTransporter (Guid.NewGuid(), "A", Guid.NewGuid())
            // when
            let truckAdded = transportTycoon.AddTruck truck
            
            // then
            Expect.isOk truckAdded "should be ok"
            
            Thread.Sleep (delay)
            let retrieveTruck = transportTycoon.GetTruck truck.Id
            Expect.isOk retrieveTruck "should be ok"
            let trackVal = retrieveTruck.OkValue
            Expect.equal truck trackVal "should be equal"
            
        multipleTestCase "cannot retrieve an unexisting truck - Error" transportTycoons <| fun (transportTycoon, setUp,delay) ->
            // given
            setUp ()
            let truckRef = Guid.NewGuid()
            // when
            let retrieveTruck = transportTycoon.GetTruck truckRef
            // then
            Expect.isError retrieveTruck "should be an error"
        
        multipleTestCase "add a truck and then place it on a site, then retrieve the truck and verify that its current position is that site - Ok" transportTycoons <| fun (transportTycoon, setUp, delay) ->
            // given
            setUp ()
            let truckId = Guid.NewGuid()
            let truck = Transporter.Transporter.MkTransporter (truckId, "A", Guid.NewGuid())
            let site = Site.MkSite (Guid.NewGuid(), SiteType.Factory)
            Thread.Sleep (delay)
            let truckAdded = transportTycoon.AddTruck truck
            Expect.isOk truckAdded "should be ok"
            Thread.Sleep (delay)
            let siteAdded = transportTycoon.AddSite site
            Expect.isOk siteAdded "should be ok"
            
            // when
            Thread.Sleep (delay)
            let truckPlaced = transportTycoon.PlaceTruckOnSite (truck.Id, site.Id)
            Expect.isOk truckPlaced "should be ok"
           
            // then
            Thread.Sleep (delay)
            let retrieveTruck = transportTycoon.GetTruck truck.Id
            Expect.isOk retrieveTruck "should be ok"
            let retrieveTruckVal = retrieveTruck.OkValue
            let expectedTruck = { truck with CurrentLocation = Some site.Id }
            Expect.equal expectedTruck retrieveTruckVal "should be equal"
            
        multipleTestCase "add a truck and then place it on a site, then check that the site contains that truck  - Ok" transportTycoons <| fun (transportTycoon, setUp, delay) ->
            // given
            setUp ()
            let truck = Transporter.Transporter.MkTransporter (Guid.NewGuid(), "A", Guid.NewGuid())
            let site = Site.MkSite (Guid.NewGuid(), SiteType.Factory)
            let truckAdded = transportTycoon.AddTruck truck
            Expect.isOk truckAdded "should be ok"
            Thread.Sleep (delay)
            let siteAdded = transportTycoon.AddSite site
            Expect.isOk siteAdded "should be ok"
            
            // when
            Thread.Sleep (delay)
            let truckPlaced = transportTycoon.PlaceTruckOnSite (truck.Id, site.Id)
            Expect.isOk truckPlaced "should be ok"
            
            // then
            Thread.Sleep (delay)
            let retrieveSite = transportTycoon.GetSite site.Id
            Expect.isOk retrieveSite "should be ok"
            let retrievedSiteVal = retrieveSite.OkValue
            let expectedSite = { site with Trucks = [truck.Id] }
            Expect.equal expectedSite retrievedSiteVal "should be equal"
            
        multipleTestCase "add two trucks, one site and then place both the trucks on that place, in two different operations - Ok" transportTycoons <| fun (transportTycoon, setUp, delay) ->
            // given
            setUp ()
            let truck1 = Transporter.Transporter.MkTransporter (Guid.NewGuid(), "A", Guid.NewGuid())
            let truck2 = Transporter.Transporter.MkTransporter (Guid.NewGuid(), "B", Guid.NewGuid())
            let site = Site.MkSite (Guid.NewGuid(), SiteType.Factory)
            
            let siteAdded = transportTycoon.AddSite site
            Expect.isOk siteAdded "should be ok"
            let truck1Added = transportTycoon.AddTruck truck1
            Expect.isOk truck1Added "should be ok"
            
            let truck2Added = transportTycoon.AddTruck truck2
            Expect.isOk truck2Added "should be ok"
            
            // when
            let truck1Placed = transportTycoon.PlaceTruckOnSite (truck1.Id, site.Id)
            Expect.isOk truck1Placed "should be ok"
            let truck2Placed = transportTycoon.PlaceTruckOnSite (truck2.Id, site.Id)
            Expect.isOk truck2Placed "should be ok"
            
            // then
            Thread.Sleep (delay)
            let retrieveSite = transportTycoon.GetSite site.Id
            Expect.isOk retrieveSite "should be ok"
            let retrievedSiteVal = retrieveSite.OkValue
            Expect.isTrue (retrievedSiteVal.Trucks |> List.contains truck1.Id) "should be true"
            Expect.isTrue (retrievedSiteVal.Trucks |> List.contains truck2.Id) "should be true"
            
        multipleTestCase "add a factory site and a port site and a connection between them. Then in retrieving those sites, each of them will contain the newly added connection - Ok" transportTycoons <| fun (transportTycoon, setUp, delay) ->
            // given
            setUp ()
            let factory = Site.MkSite (Guid.NewGuid(), SiteType.Factory)
            let port = Site.MkSite (Guid.NewGuid(), SiteType.Port)
            
            let factoryAdded = transportTycoon.AddSite factory

            Expect.isOk factoryAdded "should be ok"
            let portAdded = transportTycoon.AddSite port
            Expect.isOk portAdded "should be ok"
            
            // when
            let connectFactoryToPort = transportTycoon.ConnectSitesByRoad factory.Id port.Id factory.Id port.Id 1
            Expect.isOk connectFactoryToPort "should be ok"
            
            // then
            Thread.Sleep (delay)
            let retrieveFactory = transportTycoon.GetSite factory.Id
            Expect.isOk retrieveFactory "should be ok"
            let retrieveFactoryValue = retrieveFactory.OkValue
            Expect.equal retrieveFactoryValue.SiteConnections.Length 1 "should be equal"
            let actualConnection = retrieveFactoryValue.SiteConnections |> List.head
            let expectedConnection =
                {
                    Id = actualConnection.Id
                    InitialSitePath = factory.Id // todo: set the initial site path
                    EndInterval = port.Id
                    DestinationSitePath = port.Id
                    ConnectionType = ConnectionType.Road
                    TimeToTravel = 1
                }
            Expect.equal actualConnection expectedConnection "should be equal"
            
            // and also        
            let retrievePort = transportTycoon.GetSite port.Id
            Expect.isOk retrievePort "should be ok"
            let retrievePortValue = retrievePort.OkValue
            Expect.equal retrievePortValue.SiteConnections.Length 1 "should be equal"
            let actualConnection = retrievePortValue.SiteConnections |> List.head
            let expectedConnection =
                {
                    Id = actualConnection.Id
                    InitialSitePath = factory.Id // todo: set the initial site path
                    EndInterval = factory.Id
                    ConnectionType = ConnectionType.Road
                    TimeToTravel = 1
                    DestinationSitePath = port.Id
                }
            Expect.equal actualConnection expectedConnection "should be equal"
            
        multipleTestCase "setup factory, truck1, truck2, Factory, Port, Node A" transportTycoons  <| fun (transportTycoon, setUp, delay) ->
            // given
            setUp ()
            let factory = Site.MkSite (Guid.NewGuid(), SiteType.Factory)
            let port = Site.MkSite (Guid.NewGuid(), SiteType.Port)
            let truck1 = Transporter.MkTransporter (Guid.NewGuid(), "A", Guid.NewGuid())
            let truck2 = Transporter.MkTransporter (Guid.NewGuid(), "B", Guid.NewGuid())
            let nodeAId = Guid.NewGuid()
            let nodeA = Site.MkSite (nodeAId, SiteType.Destination "A")
          
            let factoryAdded = transportTycoon.AddSite factory
            Expect.isOk factoryAdded "should be ok"
            let portAdded = transportTycoon.AddSite port
            Expect.isOk portAdded "should be ok"
            let truck1Added = transportTycoon.AddTruck truck1
            Expect.isOk truck1Added "should be ok"
            let truck2Added = transportTycoon.AddTruck truck2
            Expect.isOk truck2Added "should be ok"
            let nodeAAdded = transportTycoon.AddSite nodeA
            Expect.isOk nodeAAdded "should be ok"
            
            // when
            let connectFactoryToPort = transportTycoon.ConnectSitesByRoad factory.Id port.Id factory.Id nodeA.Id 1
            Expect.isOk connectFactoryToPort "should be ok"
            
            let connectPortToNodeA = transportTycoon.ConnectSitesByRoad port.Id nodeA.Id factory.Id nodeA.Id 1
            Expect.isOk connectPortToNodeA "should be ok"

            // then
            Thread.Sleep (delay)
            let retrieveFactory = transportTycoon.GetSite factory.Id
            Expect.isOk retrieveFactory "should be ok"
            let retrieveFactoryValue = retrieveFactory.OkValue
            Expect.equal retrieveFactoryValue.SiteConnections.Length 1 "should be equal"
            let actualConnection = retrieveFactoryValue.SiteConnections |> List.head
            let expectedConnection =
                {
                    Id = actualConnection.Id
                    InitialSitePath = factory.Id
                    EndInterval = port.Id
                    ConnectionType = ConnectionType.Road
                    TimeToTravel = 1
                    DestinationSitePath = nodeA.Id
                }
            Expect.equal actualConnection expectedConnection "should be equal"  

            let retrievePort = transportTycoon.GetSite port.Id
            Expect.isOk retrievePort "should be ok"
            let retrievePortValue = retrievePort.OkValue
            Expect.equal retrievePortValue.SiteConnections.Length 2 "should be equal"
            let actualConnection = retrievePortValue.SiteConnections |> List.head
            let expectedConnection =
                {
                    Id = actualConnection.Id
                    InitialSitePath = factory.Id
                    EndInterval = nodeA.Id
                    ConnectionType = ConnectionType.Road
                    TimeToTravel = 1
                    DestinationSitePath = nodeA.Id
                }
            Expect.equal actualConnection expectedConnection "should be equal"

        multipleTestCase "add a port connected to the factory and to the A node" transportTycoons <| fun (transportTycoon, setUp, delay) ->
            // given
            setUp ()
            let factory = Site.MkSite (Guid.NewGuid(), SiteType.Factory)
            let addFactory = transportTycoon.AddSite factory
            Expect.isOk addFactory "should be ok"
            let nodeA = Site.MkSite (Guid.NewGuid(), SiteType.Destination "A")
            Thread.Sleep (delay)
            let addNodeA = transportTycoon.AddSite nodeA
            Expect.isOk addNodeA "should be ok"
            let port = Site.MkSite (Guid.NewGuid(), SiteType.Port)
            Thread.Sleep (delay)
            let addPort = transportTycoon.AddSite port
            Expect.isOk addPort "should be ok"

            // when
            Thread.Sleep (delay)
            let connectFactoryToPort = transportTycoon.ConnectSitesByRoad factory.Id port.Id factory.Id nodeA.Id 1
            Expect.isOk connectFactoryToPort "should be ok"
            
            Thread.Sleep (delay)
            let connectPortToNodeA = transportTycoon.ConnectSitesBySea port.Id nodeA.Id factory.Id nodeA.Id 1
            Expect.isOk connectPortToNodeA "should be ok"

            // then
            Thread.Sleep (delay)
            let retrieveFactory = transportTycoon.GetSite factory.Id
            Expect.isOk retrieveFactory "should be ok"
            let retrievePortValue = retrieveFactory.OkValue
            
            Thread.Sleep (delay)
            Expect.equal retrievePortValue.SiteConnections.Length 1 "should be equal"
            let firstConnection = retrievePortValue.SiteConnections |> List.head
            let expectedFirstConnection = 
                { 
                    Id = firstConnection.Id
                    InitialSitePath = factory.Id; 
                    EndInterval = port.Id; 
                    ConnectionType = ConnectionType.Road; 
                    TimeToTravel = 1; 
                    DestinationSitePath = nodeA.Id 
                }
            Expect.equal firstConnection expectedFirstConnection "should be equal"

        multipleTestCase "verify that the network is seeded correctly - Ok" transportTycoons <| fun (transportTycoon, setUp, delay) ->
            // given
            setUp ()
            Thread.Sleep (delay)
            let seedNetwork = seedDefaultNetwork transportTycoon
            let retrieveFactory = transportTycoon.GetSite testDataIds.FactoryId
            Expect.isOk retrieveFactory "should be ok"
            let retrieveNodeA = transportTycoon.GetSite testDataIds.NodeAId
            Expect.isOk retrieveNodeA "should be ok"
            let retrieveNodeB = transportTycoon.GetSite testDataIds.NodeBId
            Expect.isOk retrieveNodeB "should be ok"
            let retrievePort = transportTycoon.GetSite testDataIds.PortId
            Expect.isOk retrievePort "should be ok"
            let retrieveTruck1 = transportTycoon.GetTruck testDataIds.Truck1Id
            Expect.isOk retrieveTruck1 "should be ok"
            let retrieveTruck2 = transportTycoon.GetTruck testDataIds.Truck2Id
            Expect.isOk retrieveTruck2 "should be ok"

            Expect.isOk seedNetwork "should be ok"

        multipleTestCase "a truck that is placed on a site has zero distance traveled on the connection- Ok" transportTycoons <| fun (transportTycoon, setUp, delay) ->
            setUp ()
            let seedNetwork = seedDefaultNetwork transportTycoon
            Expect.isOk seedNetwork "should be ok"
            let retrieveTruck1 = transportTycoon.GetTruck testDataIds.Truck1Id
            Expect.isOk retrieveTruck1 "should be ok"
            let retrieveTruck1Value = retrieveTruck1.OkValue
            Expect.equal retrieveTruck1Value.DistanceTraveled 0 "should be equal"

        multipleTestCase "Initial state of a truck: no connection chosen - Ok" transportTycoons <| fun (transportTycoon, setUp, delay) ->
            // given
            setUp ()
            let seedNetwork = seedDefaultNetwork transportTycoon
            Expect.isOk seedNetwork "should be ok"
            let retrieveTruck1 = transportTycoon.GetTruck testDataIds.Truck1Id
            Expect.isOk retrieveTruck1 "should be ok"
            let retrieveTruckValue = retrieveTruck1.OkValue
            Expect.isNone retrieveTruckValue.ConnectionChosen "should be some"

        multipleTestCase "When the tick starts, if the truck is on a site, then the truck should choose the first connection that has the same destination site as the truck's destination - Ok" transportTycoons <| fun (transportTycoon, setUp, delay) ->
            setUp ()
            // given
            let factory = Site.MkSite (Guid.NewGuid(), SiteType.Factory)
            let factoryAdded = transportTycoon.AddSite factory
            Expect.isOk factoryAdded "should be ok"

            let port = Site.MkSite (Guid.NewGuid(), SiteType.Port)
            let portAdded = transportTycoon.AddSite port
            Expect.isOk portAdded "should be ok"

            let truck = Transporter.MkTransporter (Guid.NewGuid(), "A", Guid.NewGuid())
            let truckAdded = transportTycoon.AddTruck truck
            Expect.isOk truckAdded "should be ok"

            let connectFactoryToPort = transportTycoon.ConnectSitesByRoad factory.Id port.Id factory.Id port.Id 1
            Expect.isOk connectFactoryToPort "should be ok"

            // when
            let truckPlaced = transportTycoon.PlaceTruckOnSite (truck.Id, factory.Id)
            Expect.isOk truckPlaced "should be ok"

            Thread.Sleep (delay)
            let retrieveTruck = transportTycoon.GetTruck truck.Id
            Expect.isOk retrieveTruck "should be ok"
            let retrieveTruckValue = retrieveTruck.OkValue
            Expect.equal retrieveTruckValue.ConnectionChosen None "should be none"

            // then

            let tick = transportTycoon.Tick ()
            Expect.isOk tick "should be ok"

            // Expect.isOk tick "should be ok"
            // let retrieveTruck2 = transportTycoon.GetTruck truck.Id
            // Expect.isOk retrieveTruck2 "should be ok"
            // let retrieveTruckValue2 = retrieveTruck2.OkValue
            // Expect.isSome retrieveTruckValue2.ConnectionChosen "should be none"

    ]
    |> testSequenced

