namespace Sharpino.TransportTycoon

open System
open Sharpino
open Sharpino.Commons
open Sharpino.Core
open Sharpino.TransportTycoon.Definitions
open FSharpPlus
open FSharpPlus.Operators
open FsToolkit.ErrorHandling

module Transporter =
    type Transporter =
        {
            Id: TransporterId
            DestinationCode: string
            DestinationSiteId: SiteId
            CurrentLocation: Option<SiteId>
            TransporterType: TransporterType
            DistanceTraveled: int
            ConnectionChosen: Option<ConnectionId>
        }
        static member MkTransporter (id: TransporterId, destination: string, destinationSiteId: SiteId) =
            {
                Id = id
                DestinationCode = destination
                DestinationSiteId = destinationSiteId
                CurrentLocation = None
                TransporterType = TransporterType.TruckType
                DistanceTraveled = 0
                ConnectionChosen = None
            }
        member this.SetSite (siteId: SiteId) =
            { this with CurrentLocation = Some siteId } |> Ok
            
        static member StorageName = "_truck"
        static member Version = "_01"
        static member SnapshotsInterval = 15
        static member Deserialize x = 
            jsonPSerializer.Deserialize<Transporter> x
        member this.Serialize =
            jsonPSerializer.Serialize this
        
        
        
            