
namespace Sharpino.TransportTycoon

open System
open Sharpino
open Sharpino.Core
open Sharpino.TransportTycoon.Network
open Sharpino.Commons
open FsToolkit.ErrorHandling

module NetworkEvents =
    type NetworkEvents =
        | SiteReferenceAdded of Guid
        | TruckReferenceAdded of Guid
            interface Event<Network> with
                member this.Process (network: Network) =
                    match this with
                    | SiteReferenceAdded siteRef -> network.AddSiteReference siteRef
                    | TruckReferenceAdded truckRef -> network.AddTruckReference truckRef
        static member Deserialize x =
            jsonPSerializer.Deserialize<NetworkEvents> x
        member this.Serialize =
            jsonPSerializer.Serialize this    
        