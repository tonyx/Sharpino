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
open Sharpino.TransportTycoon.Truck
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
    eventStore.Reset Truck.Version Truck.StorageName
    StateCache2<Network>.Instance.Invalidate ()
    AggregateCache<Site, string>.Instance.Clear ()
    AggregateCache<Truck, string>.Instance.Clear ()
           
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

let truckViewerMemory = getAggregateStorageFreshStateViewer<Truck, TruckEvents, string> eventStoreMemory
let truckViewerPg = getAggregateStorageFreshStateViewer<Truck, TruckEvents, string> eventStorePg

let memoryTransportTycoon = TransportTycoon (eventStoreMemory, doNothingBroker, networkViewerMemory, siteViewerMemory, truckViewerMemory)
let pgTransportTycoon = TransportTycoon (eventStorePg, doNothingBroker, networkViewerPg, siteViewerPg, truckViewerPg)

let transportTycoons =
    [
        memoryTransportTycoon, fun () ->  setUp eventStoreMemory
        pgTransportTycoon, fun () -> setUp eventStorePg
    ]

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
            let truck = Truck.Truck.MkTruck (Guid.NewGuid(), "A")
            // when
            let truckAdded = transportTycoon.AddTruck truck
            // then
            Expect.isOk truckAdded "should be ok"
            let trucks = transportTycoon.TrucksReferences ()
            Expect.isOk trucks "should be ok"
            let trucksVal = trucks.OkValue
            Expect.equal 1 trucksVal.Length "should be 1"
           
        multipleTestCase "when added a tadd and retrieve a truck - Ok" transportTycoons <| fun (transportTycoon, setUp) ->      
            // given
            setUp ()
            let truck = Truck.Truck.MkTruck (Guid.NewGuid(), "A")
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
        
        multipleTestCase "add a truck and then place it on a site, Then retrieve the truck and verify that its current position is that site - Ok" transportTycoons <| fun (transportTycoon, setUp) ->
            // given
            setUp ()
            let truckId = Guid.NewGuid()
            let truck = Truck.Truck.MkTruck (truckId, "A")
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
            
        multipleTestCase "add a truck and then place it on a site, Then check that the site contains that truck  - Ok" transportTycoons <| fun (transportTycoon, setUp) ->
            // given
            setUp ()
            let truck = Truck.Truck.MkTruck (Guid.NewGuid(), "A")
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
            let truck1 = Truck.Truck.MkTruck (Guid.NewGuid(), "truck1")
            let truck2 = Truck.Truck.MkTruck (Guid.NewGuid(), "truck2")
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
            
    ]
    |> testSequenced

