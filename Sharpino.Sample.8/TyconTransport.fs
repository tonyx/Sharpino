namespace Sharpino.TransportTycoon
open Sharpino.Storage
open Sharpino.TransportTycoon.Definitions
open Sharpino.TransportTycoon.Site
open Sharpino.TransportTycoon.SiteEvents
open Sharpino.TransportTycoon.SiteCommands
open Sharpino.TransportTycoon.Network
open Sharpino.TransportTycoon.NetworkEvents
open Sharpino.TransportTycoon.NetworkCommands
open Sharpino.TransportTycoon.NetworkCommands

open Sharpino
open Sharpino.Commons
open Sharpino.Core
open Sharpino.CommandHandler

open System

open FSharpPlus
open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Sharpino.TransportTycoon.Transporter
open Sharpino.TransportTycoon.TruckEvents

module TransportTycoon =
    
    let doNothingBroker: IEventBroker<_> =
        {  notify = None
           notifyAggregate = None }

    type TransportTycoon (eventStore: IEventStore<string>, eventBroker: IEventBroker<_>, networkViewer:StateViewer<Network>, siteViewer:AggregateViewer<Site>, truckViewer: AggregateViewer<Transporter> ) =
        member this.SitesReferences () = 
            result {
                let! (_, state) = networkViewer ()
                return state.SiteIds
            }
        member this.TrucksReferences () = 
            result {
                let! (_, state) = networkViewer ()
                return state.TruckRefs
            }     
        member this.AddSite (site: Site) =
            result {
                return!
                    site.Id
                    |> NetworkCommands.AddSiteReference
                    |> runInitAndCommand<Network, NetworkEvents, Site, string> eventStore eventBroker site
            }
        member this.AddTruck (truck: Transporter) =
            result {
                return!
                    truck.Id 
                    |> NetworkCommands.AddTruckReference
                    |> runInitAndCommand<Network, NetworkEvents, Transporter, string> eventStore eventBroker truck
            }     
        member this.GetSite (siteRef: Guid) =
            result {
                let! (_, state) = networkViewer ()
                do!
                    state.SiteIds
                    |> List.contains siteRef
                    |> Result.ofBool "Site not found"
                let! (_, site) = siteViewer siteRef
                return site
            }
            
        member this.GetTruck (truckRef: Guid) =
            result {
                let! (_, state) = networkViewer ()
                do!
                    state.TruckRefs
                    |> List.contains truckRef
                    |> Result.ofBool "Truck not found"
                let! (_, truck) = truckViewer truckRef
                return truck
            }
        member this.PlaceTruckOnSite (truckId: Guid, siteId: Guid) =
            result {
                let! (_, network) = networkViewer ()
                do! 
                    network.TruckRefs
                    |> List.contains truckId
                    |> Result.ofBool (sprintf "Truck %A not found" truckId)
                do!
                    network.SiteIds
                    |> List.contains siteId
                    |> Result.ofBool (sprintf "Site %A not found" siteId)
                let setSite = TruckCommands.SetSite siteId
                let placeTruck = SiteCommands.PlaceTruck truckId
                 
                let! result =
                    runTwoAggregateCommands truckId siteId eventStore eventBroker setSite placeTruck
                return result
            }

        member private this.ConnectSites (startConnection: Guid) (endConnection: Guid) (startPath: Guid) (endPath: Guid) (timeToTravel: int) (connectionType: ConnectionType) =
            result {
                let! (_, network) = networkViewer ()
                do! 
                    network.SiteIds
                    |> List.contains startConnection
                    |> Result.ofBool (sprintf "Start connection %A site not found" startConnection)
                do! 
                    network.SiteIds
                    |> List.contains endConnection
                    |> Result.ofBool (sprintf "End connection %A not found" endConnection)

                do!
                    network.SiteIds
                    |> List.contains startPath
                    |> Result.ofBool (sprintf "Start of path %A not found" startPath)
                do!
                    network.SiteIds
                    |> List.contains endPath
                    |> Result.ofBool (sprintf "End of path %A not found" endPath)   

                let startToEndConnection =
                    Connection.MkConnection endConnection startPath endPath connectionType timeToTravel
                let endToStartConnection =
                    Connection.MkConnection startConnection startPath endPath connectionType timeToTravel
               
                let addConnectionToFirstNode = SiteCommands.AddConnection startToEndConnection
                let addConnectionToSecondNode = SiteCommands.AddConnection endToStartConnection
               
                return!
                    runTwoAggregateCommands startConnection endConnection eventStore eventBroker addConnectionToFirstNode addConnectionToSecondNode
            }
            
        member this.ConnectSitesByRoad  (siteId1: Guid) (siteId2: Guid) (startPath: Guid) (endPath: Guid) (timeToTravel: int) =
            this.ConnectSites siteId1 siteId2 startPath endPath timeToTravel ConnectionType.Road
        member this.ConnectSitesBySea (siteId1: Guid) (siteId2: Guid) (startPath: Guid) (endPath: Guid) (timeToTravel: int) =
            this.ConnectSites siteId1 siteId2 startPath endPath timeToTravel ConnectionType.Sea 
            