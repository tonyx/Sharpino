namespace Sharpino.TransportTycoon

open System
open Sharpino
open Sharpino.Commons
open Sharpino.Core
open Sharpino.TransportTycoon.Definitions
open FSharpPlus
open FSharpPlus.Operators
open FsToolkit.ErrorHandling

module Truck =
    type Truck =
        {
            Id: Guid
            DestinationCode: string
            CurrentLocation: Option<Guid>
        }
        static member MkTruck (id: Guid, destination: string) =
            { Id = id; DestinationCode = destination; CurrentLocation = None }
        member this.SetSite (siteRef: Guid) =
            { this with CurrentLocation = Some siteRef } |> Ok
            
        static member StorageName = "_truck"
        static member Version = "_01"
        static member SnapshotsInterval = 15
        static member Deserialize x = 
            jsonPSerializer.Deserialize<Truck> x
        member this.Serialize =
            jsonPSerializer.Serialize this
        
        interface Aggregate<string> with
            member this.Id = this.Id
            member this.Serialize =
                this.Serialize
        
        
            