namespace  Sharpino.TransportTycoon
open Sharpino.TransportTycoon
open Sharpino.TransportTycoon.Site
open System
open Sharpino
open Sharpino.Commons
open Sharpino.Core
open Sharpino.TransportTycoon.Definitions
open FSharpPlus
open FSharpPlus.Operators
open FsToolkit.ErrorHandling

module SiteEvents =
    type SiteEvents =
        | TruckPlaced of Guid
        | ConnectionAdded of Connection
            interface Event<Site.Site> with
                member this.Process (x: Site.Site) =
                    match this with
                    | TruckPlaced truckRef ->
                        x.PlaceTruck truckRef
                    | ConnectionAdded connection->
                        x.AddConnection connection
           static member Deserialize x =
                jsonPSerializer.Deserialize<SiteEvents> x
           member this.Serialize =
                jsonPSerializer.Serialize this 
        
       