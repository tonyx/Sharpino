module Sharpino.TransportTycoon.Tests

open System
open Sharpino.Storage
open Sharpino.TransportTycoon
open Sharpino.TransportTycoon.Definitions
open Sharpino.TransportTycoon.Site
open Sharpino.TransportTycoon.SiteEvents
open Sharpino.TransportTycoon.SiteCommands
open Sharpino.TransportTycoon.Network
open Sharpino.TransportTycoon.NetworkEvents
open Sharpino.TransportTycoon.NetworkCommands
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
open Sharpino.TransportTycoon.TruckEvents

let setUp (eventStore: IEventStore<'F>) =
    eventStore.Reset Network.Version Network.StorageName
    eventStore.Reset Site.Version Site.StorageName
    eventStore.Reset Transporter.Version Transporter.StorageName
    StateCache2<Network>.Instance.Invalidate ()
    AggregateCache<Site, string>.Instance.Clear ()
    AggregateCache<Transporter, string>.Instance.Clear ()
           
let connection = 
        Env.Load() |> ignore
        let password = Environment.GetEnvironmentVariable("password")

        "Server=127.0.0.1;" +
        "Database=transport_tycoon;" +
        "User Id=safe;"+
        $"Password={password};"

let eventStoreMemory: IEventStore<string> = MemoryStorage ()
let eventStorePg: IEventStore<string> = PgEventStore connection

let networkViewerMemory = getStorageFreshStateViewer<Network, NetworkEvents, string> eventStoreMemory
let networkViewerPg = getStorageFreshStateViewer<Network, NetworkEvents, string> eventStorePg

let siteViewerMemory = getAggregateStorageFreshStateViewer<Site, SiteEvents, string> eventStoreMemory
let siteViewerPg = getAggregateStorageFreshStateViewer<Site, SiteEvents, string> eventStorePg

let truckViewerMemory = getAggregateStorageFreshStateViewer<Transporter, TruckEvents, string> eventStoreMemory
let truckViewerPg = getAggregateStorageFreshStateViewer<Transporter, TruckEvents, string> eventStorePg

let memoryTransportTycoon = TransportTycoon (eventStoreMemory, doNothingBroker, networkViewerMemory, siteViewerMemory, truckViewerMemory)
let pgTransportTycoon = TransportTycoon (eventStorePg, doNothingBroker, networkViewerPg, siteViewerPg, truckViewerPg)

let transportTycoons =
    [
        memoryTransportTycoon, fun () ->  setUp eventStoreMemory
        pgTransportTycoon, fun () -> setUp eventStorePg
    ]

let testDataIds = 
    {|
        PortId = Guid.NewGuid()
        FactoryId = Guid.NewGuid()
        NodeAId = Guid.NewGuid()
        NodeBId = Guid.NewGuid()
        Truck1Id = Guid.NewGuid()
        Truck2Id = Guid.NewGuid()
    |}

let seedDefaultNetwork (transportTycoon: TransportTycoon) = 
    result {
        let factory = Site.MkSite (testDataIds.FactoryId, SiteType.Factory)
        let port = Site.MkSite (testDataIds.PortId, SiteType.Port)
        let nodeA = Site.MkSite (testDataIds.NodeAId, SiteType.Destination "A")
        let nodeB = Site.MkSite (testDataIds.NodeBId, SiteType.Destination "B")
        let truck1 = Transporter.Transporter.MkTruck (testDataIds.Truck1Id, "A")
        let truck2 = Transporter.Transporter.MkTruck (testDataIds.Truck2Id, "B")

        do! transportTycoon.AddSite factory
        do! transportTycoon.AddSite port
        do! transportTycoon.AddSite nodeA
        do! transportTycoon.AddSite nodeB

        do! transportTycoon.AddTruck truck1
        do! transportTycoon.AddTruck truck2
        do! transportTycoon.PlaceTruckOnSite (truck1.Id, factory.Id)
        do! transportTycoon.PlaceTruckOnSite (truck2.Id, factory.Id)
        do! transportTycoon.ConnectSitesByRoad factory.Id port.Id 1
        do! transportTycoon.ConnectSitesBySea port.Id nodeA.Id 1
        do! transportTycoon.ConnectSitesByRoad factory.Id nodeB.Id 5

        return ()
    }
    
    

[<Tests>]
let tests =
    testList "samples" [
        multipleTestCase "initial state of transportTycoon: the list of sites is empty - Ok" transportTycoons <| fun (transportTycoon, setUp) ->
            // given
            setUp ()
            let currentSites = transportTycoon.SitesReferences ()
            Expect.isOk currentSites "should be ok"
            
            // then
            let sitesValue = currentSites.OkValue
            Expect.equal 0 sitesValue.Length "should be 0"
            
        multipleTestCase "add a site to the network - Ok" transportTycoons <| fun (transportTycoon, setUp) ->
            // given
            setUp ()
            let site = Site.MkSite (Guid.NewGuid(), SiteType.Factory)
            // when
            let siteAdded = transportTycoon.AddSite site
            Expect.isOk siteAdded "should be ok"
            // then
            let currentSites = transportTycoon.SitesReferences ()
            Expect.isOk currentSites "should be ok"
            let currentSitesVal = currentSites.OkValue
            Expect.equal 1 currentSitesVal.Length "should be 1"
            
            let retrieveSite = transportTycoon.GetSite site.Id
            Expect.isOk retrieveSite "should be ok"
            let retrieveSiteVal = retrieveSite.OkValue
            Expect.equal site retrieveSiteVal "should be equal"
            
        multipleTestCase "cannot retrieve an unexisting site - Error"  transportTycoons <| fun (transportTycoon, setUp) ->
            // given
            setUp ()
            let siteRef = Guid.NewGuid()
            // when
            let retrieveSite = transportTycoon.GetSite siteRef
            // then
            Expect.isError retrieveSite "should be an error"
            let (Error e) = retrieveSite
            Expect.equal "Site not found" e "should be equal"
        
        multipleTestCase "there are not trucks in the network - Ok" transportTycoons <| fun (transportTycoon, setUp) ->
            // given
            setUp ()
            // when
            let trucks = transportTycoon.TrucksReferences ()
            // then
            Expect.isOk trucks "should be ok"
            let trucksVal = trucks.OkValue
            Expect.equal 0 trucksVal.Length "should be 0"
        
        multipleTestCase "when added a truck on an empty network then the number of truck will be one - Ok " transportTycoons <| fun (transportTycoon, setUp) ->
            // given
            setUp ()
            let truck = Transporter.Transporter.MkTruck (Guid.NewGuid(), "A")
            // when
            let truckAdded = transportTycoon.AddTruck truck
            // then
            Expect.isOk truckAdded "should be ok"
            let trucks = transportTycoon.TrucksReferences ()
            Expect.isOk trucks "should be ok"
            let trucksVal = trucks.OkValue
            Expect.equal 1 trucksVal.Length "should be 1"
           
        multipleTestCase "add and retrieve a truck - Ok" transportTycoons <| fun (transportTycoon, setUp) ->      
            // given
            setUp ()
            let truck = Transporter.Transporter.MkTruck (Guid.NewGuid(), "A")
            // when
            let truckAdded = transportTycoon.AddTruck truck
            
            // then
            Expect.isOk truckAdded "should be ok"
            
            let retrieveTruck = transportTycoon.GetTruck truck.Id
            Expect.isOk retrieveTruck "should be ok"
            let trackVal = retrieveTruck.OkValue
            Expect.equal truck trackVal "should be equal"
            
        multipleTestCase "cannot retrieve an unexisting truck - Error" transportTycoons <| fun (transportTycoon, setUp) ->
            // given
            setUp ()
            let truckRef = Guid.NewGuid()
            // when
            let retrieveTruck = transportTycoon.GetTruck truckRef
            // then
            Expect.isError retrieveTruck "should be an error"
            let (Error e) = retrieveTruck
            Expect.equal "Truck not found" e "should be equal"
        
        multipleTestCase "add a truck and then place it on a site, then retrieve the truck and verify that its current position is that site - Ok" transportTycoons <| fun (transportTycoon, setUp) ->
            // given
            setUp ()
            let truckId = Guid.NewGuid()
            let truck = Transporter.Transporter.MkTruck (truckId, "A")
            let site = Site.MkSite (Guid.NewGuid(), SiteType.Factory)
            let truckAdded = transportTycoon.AddTruck truck
            Expect.isOk truckAdded "should be ok"
            let siteAdded = transportTycoon.AddSite site
            Expect.isOk siteAdded "should be ok"
            
            // when
            let truckPlaced = transportTycoon.PlaceTruckOnSite (truck.Id, site.Id)
            Expect.isOk truckPlaced "should be ok"
           
            // then
            let retrieveTruck = transportTycoon.GetTruck truck.Id
            Expect.isOk retrieveTruck "should be ok"
            let retrieveTruckVal = retrieveTruck.OkValue
            let expectedTruck = { truck with CurrentLocation = Some site.Id }
            Expect.equal expectedTruck retrieveTruckVal "should be equal"
            
        multipleTestCase "add a truck and then place it on a site, then check that the site contains that truck  - Ok" transportTycoons <| fun (transportTycoon, setUp) ->
            // given
            setUp ()
            let truck = Transporter.Transporter.MkTruck (Guid.NewGuid(), "A")
            let site = Site.MkSite (Guid.NewGuid(), SiteType.Factory)
            let truckAdded = transportTycoon.AddTruck truck
            Expect.isOk truckAdded "should be ok"
            let siteAdded = transportTycoon.AddSite site
            Expect.isOk siteAdded "should be ok"
            
            // when
            let truckPlaced = transportTycoon.PlaceTruckOnSite (truck.Id, site.Id)
            Expect.isOk truckPlaced "should be ok"
            
            // then
            let retrieveSite = transportTycoon.GetSite site.Id
            Expect.isOk retrieveSite "should be ok"
            let retrievedSiteVal = retrieveSite.OkValue
            let expectedSite = { site with Trucks = [truck.Id] }
            Expect.equal expectedSite retrievedSiteVal "should be equal"
            
        multipleTestCase "add two trucks, one site and then place both the trucks on that place, in two different operations - Ok" transportTycoons <| fun (transportTycoon, setUp) ->
            // given
            setUp ()
            let truck1 = Transporter.Transporter.MkTruck (Guid.NewGuid(), "A")
            let truck2 = Transporter.Transporter.MkTruck (Guid.NewGuid(), "B")
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
            let retrieveSite = transportTycoon.GetSite site.Id
            Expect.isOk retrieveSite "should be ok"
            let retrievedSiteVal = retrieveSite.OkValue
            Expect.isTrue (retrievedSiteVal.Trucks |> List.contains truck1.Id) "should be true"
            Expect.isTrue (retrievedSiteVal.Trucks |> List.contains truck2.Id) "should be true"
            
        multipleTestCase "add a factory site and a port site and a connection between them. Then in retrieving those sites, each of them will contain the newly added connection - Ok" transportTycoons <| fun (transportTycoon, setUp) ->
            // given
            setUp ()
            let factory = Site.MkSite (Guid.NewGuid(), SiteType.Factory)
            let port = Site.MkSite (Guid.NewGuid(), SiteType.Port)
            
            let factoryAdded = transportTycoon.AddSite factory
            Expect.isOk factoryAdded "should be ok"
            let portAdded = transportTycoon.AddSite port
            Expect.isOk portAdded "should be ok"
            
            // when
            let connectFactoryToPort = transportTycoon.ConnectSitesByRoad factory.Id port.Id 1
            Expect.isOk connectFactoryToPort "should be ok"
            
            // then
            let retrieveFactory = transportTycoon.GetSite factory.Id
            Expect.isOk retrieveFactory "should be ok"
            let retrieveFactoryValue = retrieveFactory.OkValue
            Expect.equal retrieveFactoryValue.SiteConnections.Length 1 "should be equal"
            let actualConnection = retrieveFactoryValue.SiteConnections |> List.head
            let expectedConnection =
                {
                    EndNode = port.Id
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
                    EndNode = factory.Id
                    ConnectionType = ConnectionType.Road
                    TimeToTravel = 1
                }
            Expect.equal actualConnection expectedConnection "should be equal"
            
        multipleTestCase "setup factory, truck1, truck2, Factory, Port, Node A" transportTycoons  <| fun (transportTycoon, setUp) ->
            // given
            setUp ()
            let factory = Site.MkSite (Guid.NewGuid(), SiteType.Factory)
            let port = Site.MkSite (Guid.NewGuid(), SiteType.Port)
            let truck1 = Transporter.Transporter.MkTruck (Guid.NewGuid(), "A")
            let truck2 = Transporter.Transporter.MkTruck (Guid.NewGuid(), "B")
            let nodeA = Site.MkSite (Guid.NewGuid(), SiteType.Destination "A")
          
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
            let connectFactoryToPort = transportTycoon.ConnectSitesByRoad factory.Id port.Id 1
            Expect.isOk connectFactoryToPort "should be ok"
            
            let connectPortToNodeA = transportTycoon.ConnectSitesByRoad port.Id nodeA.Id 1
            Expect.isOk connectPortToNodeA "should be ok"

            // then
            let retrieveFactory = transportTycoon.GetSite factory.Id
            Expect.isOk retrieveFactory "should be ok"
            let retrieveFactoryValue = retrieveFactory.OkValue
            Expect.equal retrieveFactoryValue.SiteConnections.Length 1 "should be equal"
            let actualConnection = retrieveFactoryValue.SiteConnections |> List.head
            let expectedConnection =
                {
                    EndNode = port.Id
                    ConnectionType = ConnectionType.Road
                    TimeToTravel = 1
                }
            Expect.equal actualConnection expectedConnection "should be equal"  

            let retrievePort = transportTycoon.GetSite port.Id
            Expect.isOk retrievePort "should be ok"
            let retrievePortValue = retrievePort.OkValue
            Expect.equal retrievePortValue.SiteConnections.Length 2 "should be equal"
            let actualConnection = retrievePortValue.SiteConnections |> List.head
            let expectedConnection =
                {
                    EndNode = nodeA.Id
                    ConnectionType = ConnectionType.Road
                    TimeToTravel = 1
                }
            Expect.equal actualConnection expectedConnection "should be equal"

        multipleTestCase "add a port connected to the factory and to the A node" transportTycoons <| fun (transportTycoon, setUp) ->
            // given
            setUp ()
            let factory = Site.MkSite (Guid.NewGuid(), SiteType.Factory)
            let addFactory = transportTycoon.AddSite factory
            Expect.isOk addFactory "should be ok"
            let nodeA = Site.MkSite (Guid.NewGuid(), SiteType.Destination "A")
            let addNodeA = transportTycoon.AddSite nodeA
            Expect.isOk addNodeA "should be ok"
            let port = Site.MkSite (Guid.NewGuid(), SiteType.Port)
            let addPort = transportTycoon.AddSite port
            Expect.isOk addPort "should be ok"

            // when
            let connectFactoryToPort = transportTycoon.ConnectSitesByRoad factory.Id port.Id 1
            Expect.isOk connectFactoryToPort "should be ok"
            let connectPortToNodeA = transportTycoon.ConnectSitesBySea port.Id nodeA.Id 1
            Expect.isOk connectPortToNodeA "should be ok"

            // then
            let retrievePort = transportTycoon.GetSite port.Id
            Expect.isOk retrievePort "should be ok"
            let retrievePortValue = retrievePort.OkValue
            Expect.equal retrievePortValue.SiteConnections.Length 2 "should be equal"
            let actualConnections = retrievePortValue.SiteConnections |> List.sortBy (fun c -> c.EndNode)
            let expectedConnections = 
                [
                    { EndNode = factory.Id; ConnectionType = ConnectionType.Road; TimeToTravel = 1 }
                    { EndNode = nodeA.Id; ConnectionType = ConnectionType.Sea; TimeToTravel = 1 }
                ]
                |> List.sortBy (fun c -> c.EndNode)
            Expect.equal actualConnections expectedConnections "should be equal"

            let retrieveFactory = transportTycoon.GetSite factory.Id
            Expect.isOk retrieveFactory "should be ok"
            let retrieveFactoryValue = retrieveFactory.OkValue
            Expect.equal retrieveFactoryValue.SiteConnections.Length 1 "should be equal"
            let actualConnection = retrieveFactoryValue.SiteConnections |> List.head
            let expectedConnection =
                {
                    EndNode = port.Id
                    ConnectionType = ConnectionType.Road
                    TimeToTravel = 1
                }
            Expect.equal actualConnection expectedConnection "should be equal"

            let retrieveNodeA = transportTycoon.GetSite nodeA.Id
            Expect.isOk retrieveNodeA "should be ok"
            let retrieveNodeAValue = retrieveNodeA.OkValue
            Expect.equal retrieveNodeAValue.SiteConnections.Length 1 "should be equal"
            let actualConnection = retrieveNodeAValue.SiteConnections |> List.head
            let expectedConnection =
                {
                    EndNode = port.Id
                    ConnectionType = ConnectionType.Sea
                    TimeToTravel = 1
                }
            Expect.equal actualConnection expectedConnection "should be equal"

            let retrieveFactory = transportTycoon.GetSite factory.Id
            Expect.isOk retrieveFactory "should be ok"
            let retrieveFactoryValue = retrieveFactory.OkValue
            Expect.equal retrieveFactoryValue.SiteConnections.Length 1 "should be equal"
            let actualConnection = retrieveFactoryValue.SiteConnections |> List.head
            let expectedConnection =
                {
                    EndNode = port.Id
                    ConnectionType = ConnectionType.Road
                    TimeToTravel = 1
                }
            Expect.equal actualConnection expectedConnection "should be equal"

        multipleTestCase "add port connected to factory and to node A, factory is also connected to node B " transportTycoons <| fun (transportTycoon, setUp) ->
            // given
            setUp ()
            let factory = Site.MkSite (Guid.NewGuid(), SiteType.Factory)
            let port = Site.MkSite (Guid.NewGuid(), SiteType.Port)
            let nodeA = Site.MkSite (Guid.NewGuid(), SiteType.Destination "A")
            let addFactory = transportTycoon.AddSite factory
            Expect.isOk addFactory "should be ok"
            let addPort = transportTycoon.AddSite port
            Expect.isOk addPort "should be ok"
            let addNodeA = transportTycoon.AddSite nodeA
            Expect.isOk addNodeA "should be ok"
            let nodeB = Site.MkSite (Guid.NewGuid(), SiteType.Destination "B")
            let addNodeB = transportTycoon.AddSite nodeB
            Expect.isOk addNodeB "should be ok"

            // when
            let connectFactoryToPort = transportTycoon.ConnectSitesByRoad factory.Id port.Id 1
            Expect.isOk connectFactoryToPort "should be ok"
            let connectPortToNodeA = transportTycoon.ConnectSitesBySea port.Id nodeA.Id 1
            Expect.isOk connectPortToNodeA "should be ok"
            let connectFactoryToNodeB = transportTycoon.ConnectSitesByRoad factory.Id nodeB.Id 5
            Expect.isOk connectFactoryToNodeB "should be ok"

            // then
            let retrievePort = transportTycoon.GetSite port.Id
            Expect.isOk retrievePort "should be ok"
            let retrievePortValue = retrievePort.OkValue
            Expect.equal retrievePortValue.SiteConnections.Length 2 "should be equal"
            let actualConnections = retrievePortValue.SiteConnections |> List.sortBy (fun c -> c.EndNode)
            let expectedConnections = 
                [
                    { EndNode = factory.Id; ConnectionType = ConnectionType.Road; TimeToTravel = 1 }
                    { EndNode = nodeA.Id; ConnectionType = ConnectionType.Sea; TimeToTravel = 1 }
                ]
                |> List.sortBy (fun c -> c.EndNode)
            Expect.equal actualConnections expectedConnections "should be equal"

            let retrieveFactory = transportTycoon.GetSite factory.Id
            Expect.isOk retrieveFactory "should be ok"
            let retrieveFactoryValue = retrieveFactory.OkValue
            Expect.equal retrieveFactoryValue.SiteConnections.Length 2 "should be equal"
            let connections = retrieveFactoryValue.SiteConnections |> List.sortBy (fun c -> c.EndNode)
            let expectedConnections = 
                [
                    { EndNode = port.Id; ConnectionType = ConnectionType.Road; TimeToTravel = 1 }
                    { EndNode = nodeB.Id; ConnectionType = ConnectionType.Road; TimeToTravel = 5 }
                ]
                |> List.sortBy (fun c -> c.EndNode)
            Expect.equal connections expectedConnections "should be equal"

        multipleTestCase "add port connected to factory and to node A. Factory is also connected to node B. Add two trucks and put them on the factory - Ok" transportTycoons <| fun (transportTycoon, setUp) ->
            // given
            setUp ()
            let factory = Site.MkSite (Guid.NewGuid(), SiteType.Factory)
            let port = Site.MkSite (Guid.NewGuid(), SiteType.Port)
            let nodeA = Site.MkSite (Guid.NewGuid(), SiteType.Destination "A")
            let addFactory = transportTycoon.AddSite factory
            Expect.isOk addFactory "should be ok"
            let addPort = transportTycoon.AddSite port
            Expect.isOk addPort "should be ok"
            let addNodeA = transportTycoon.AddSite nodeA
            Expect.isOk addNodeA "should be ok"
            let nodeB = Site.MkSite (Guid.NewGuid(), SiteType.Destination "B")
            let addNodeB = transportTycoon.AddSite nodeB
            Expect.isOk addNodeB "should be ok"
            let truck1 = Transporter.Transporter.MkTruck (Guid.NewGuid(), "A")
            let truck2 = Transporter.Transporter.MkTruck (Guid.NewGuid(), "B")
            let addTruck1 = transportTycoon.AddTruck truck1
            Expect.isOk addTruck1 "should be ok"
            let addTruck2 = transportTycoon.AddTruck truck2
            Expect.isOk addTruck2 "should be ok"
            let retrieveTruck1 = transportTycoon.GetTruck truck1.Id
            Expect.isOk retrieveTruck1 "should be ok"
            let retrieveTruck1Value = retrieveTruck1.OkValue
            Expect.equal retrieveTruck1Value.CurrentLocation None "should be equal"

            // when
            let connectFactoryToPort = transportTycoon.ConnectSitesByRoad factory.Id port.Id 1
            Expect.isOk connectFactoryToPort "should be ok"
            let connectPortToNodeA = transportTycoon.ConnectSitesBySea port.Id nodeA.Id 1
            Expect.isOk connectPortToNodeA "should be ok"
            let connectFactoryToNodeB = transportTycoon.ConnectSitesByRoad factory.Id nodeB.Id 5
            Expect.isOk connectFactoryToNodeB "should be ok"
            let placeTruck1OnFactory = transportTycoon.PlaceTruckOnSite (truck1.Id, factory.Id)
            Expect.isOk placeTruck1OnFactory "should be ok"
            let placeTruck2OnFactory = transportTycoon.PlaceTruckOnSite (truck2.Id, factory.Id)
            Expect.isOk placeTruck2OnFactory "should be ok"

            // then
            let retrievePort = transportTycoon.GetSite port.Id
            Expect.isOk retrievePort "should be ok"
            let retrievePortValue = retrievePort.OkValue
            Expect.equal retrievePortValue.SiteConnections.Length 2 "should be equal"
            let actualConnections = retrievePortValue.SiteConnections |> List.sortBy (fun c -> c.EndNode)
            let expectedConnections = 
                [
                    { EndNode = factory.Id; ConnectionType = ConnectionType.Road; TimeToTravel = 1 }
                    { EndNode = nodeA.Id; ConnectionType = ConnectionType.Sea; TimeToTravel = 1 }
                ]
                |> List.sortBy (fun c -> c.EndNode)
            Expect.equal actualConnections expectedConnections "should be equal"

            let retrieveFactory = transportTycoon.GetSite factory.Id
            Expect.isOk retrieveFactory "should be ok"
            let retrieveFactoryValue = retrieveFactory.OkValue
            Expect.equal retrieveFactoryValue.SiteConnections.Length 2 "should be equal"
            let connections = retrieveFactoryValue.SiteConnections |> List.sortBy (fun c -> c.EndNode)
            let expectedConnections = 
                [
                    { EndNode = port.Id; ConnectionType = ConnectionType.Road; TimeToTravel = 1 }
                    { EndNode = nodeB.Id; ConnectionType = ConnectionType.Road; TimeToTravel = 5 }
                ]
                |> List.sortBy (fun c -> c.EndNode)
            Expect.equal connections expectedConnections "should be equal"

            let retrieveTruck1 = transportTycoon.GetTruck truck1.Id
            Expect.isOk retrieveTruck1 "should be ok"
            let retrieveTruck1Value = retrieveTruck1.OkValue
            Expect.equal retrieveTruck1Value.CurrentLocation (Some factory.Id) "should be equal"

            let retrieveTruck2 = transportTycoon.GetTruck truck2.Id
            Expect.isOk retrieveTruck2 "should be ok"
            let retrieveTruck2Value = retrieveTruck2.OkValue
            Expect.equal retrieveTruck2Value.CurrentLocation (Some factory.Id) "should be equal"

        multipleTestCase "verify that the network is seeded correctly - Ok" transportTycoons <| fun (transportTycoon, setUp) ->
            // given
            setUp ()
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

    ]
    |> testSequenced

