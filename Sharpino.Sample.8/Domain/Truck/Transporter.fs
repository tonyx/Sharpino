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
            Id: TransportId
            DestinationCode: string
            CurrentLocation: Option<Guid>
            TransporterType: TransporterType
        }
        static member MkTruck (id: TruckId, destination: string) =
            {
                Id = id
                DestinationCode = destination
                CurrentLocation = None
                TransporterType = TransporterType.TruckType
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
        
        interface Aggregate<string> with
            member this.Id =
                this.Id
                
            member this.Serialize =
                this.Serialize
        
        
            