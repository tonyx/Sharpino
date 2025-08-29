namespace Sharpino.TransportTycoon
open Sharpino.EventBroker
open Sharpino.Storage
open Sharpino.TransportTycoon.Definitions
open Sharpino.TransportTycoon.Site
open Sharpino.TransportTycoon.SiteEvents
open Sharpino.TransportTycoon.SiteCommands

// open Sharpino.TransportTycoon.Network
// open Sharpino.TransportTycoon.NetworkEvents
// open Sharpino.TransportTycoon.NetworkCommands
// open Sharpino.TransportTycoon.NetworkCommands

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

    type TransportTycoon (eventStore: IEventStore<string>, messageSenders: MessageSenders, siteViewer:AggregateViewer<Site>, truckViewer: AggregateViewer<Transporter> ) =
            
        member this.Sites () = 
            result {
                let! sites =
                    StateView.getAllAggregateStates<Site, SiteEvents, string> eventStore
                return sites |>> snd
            }
            
        member this.Trucks () =
            result {
                let! trucks =
                    StateView.getAllAggregateStates<Transporter, TruckEvents, string> eventStore
                return trucks |>> snd
            }
            
        member this.AddSite (site: Site) =
            result {
                return!
                    runInit<Site, SiteEvents, string> eventStore messageSenders site
            }
            
        member this.AddTruck (truck: Transporter) =
            result {
                return!
                    runInit<Transporter, TruckEvents, string> eventStore messageSenders truck
            }
            
        member this.GetSite (siteRef: Guid) =
            result {
                let! (_, site) = siteViewer siteRef
                return site
            }
            
        member this.GetTruck (truckRef: Guid) =
            result {
                let! (_, truck) = truckViewer truckRef
                return truck
            }
            
        member this.PlaceTruckOnSite (truckId: Guid, siteId: Guid) =
            result {
                let! (_, site) = siteViewer siteId 
                let setSite = TruckCommands.SetSite siteId
                let placeTruck = SiteCommands.PlaceTruck truckId
                 
                let! result =
                    runTwoAggregateCommands truckId siteId eventStore messageSenders setSite placeTruck
                return result
            }

        member private this.ConnectSites (startConnection: Guid) (endConnection: Guid) (startPath: Guid) (endPath: Guid) (timeToTravel: int) (connectionType: ConnectionType) =
            result {
                let! (_, startSite) = siteViewer startConnection
                let! (_, endSite) = siteViewer endConnection

                let startToEndConnection =
                    Connection.MkConnection endConnection startPath endPath connectionType timeToTravel
                let endToStartConnection =
                    Connection.MkConnection startConnection startPath endPath connectionType timeToTravel
               
                let addConnectionToFirstNode = SiteCommands.AddConnection startToEndConnection
                let addConnectionToSecondNode = SiteCommands.AddConnection endToStartConnection
               
                return!
                    runTwoAggregateCommands startConnection endConnection eventStore messageSenders addConnectionToFirstNode addConnectionToSecondNode
            }
            
        member this.ConnectSitesByRoad (siteId1: Guid) (siteId2: Guid) (startPath: Guid) (endPath: Guid) (timeToTravel: int) =
            this.ConnectSites siteId1 siteId2 startPath endPath timeToTravel ConnectionType.Road
            
        member this.ConnectSitesBySea (siteId1: Guid) (siteId2: Guid) (startPath: Guid) (endPath: Guid) (timeToTravel: int) =
            this.ConnectSites siteId1 siteId2 startPath endPath timeToTravel ConnectionType.Sea 
            
        member this.Tick () =
            result {
                
                let! transporters =
                    StateView.getAllAggregateStates<Transporter, TruckEvents, string> eventStore
                let transporters' =
                    transporters |>> snd
                    
                let transportersOnSite =
                    transporters'
                    |> List.filter (fun truck -> truck.CurrentLocation.IsSome)
                return ()
            }
            
            