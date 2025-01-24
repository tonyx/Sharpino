namespace Sharpino.TransportTycoon

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
open Sharpino.TransportTycoon.Truck

module TruckEvents =
    type TruckEvents =
        SiteSet of Guid
            interface Event<Truck> with
                member this.Process (x: Truck) =
                    match this with
                    | SiteSet siteId ->
                        x.SetSite siteId
           static member Deserialize x =
                jsonPSerializer.Deserialize<TruckEvents> x
           member this.Serialize =
                jsonPSerializer.Serialize this