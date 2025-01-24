namespace Sharpino.TransportTycoon

open System
open Sharpino
open Sharpino.Commons
open Sharpino.Core
open Sharpino.TransportTycoon.Network
open Sharpino.TransportTycoon.NetworkEvents
open FsToolkit.ErrorHandling

module NetworkCommands =
    type NetworkCommands =
        | AddSiteReference of Guid
        | AddTruckReference of Guid
            interface Command<Network, NetworkEvents> with
                member this.Execute (network: Network) =
                    match this with
                    | AddSiteReference siteRef ->
                        network.AddSiteReference siteRef
                        |> Result.map (fun s -> (s, [SiteReferenceAdded siteRef]))
                    | AddTruckReference truckRef ->
                        network.AddTruckReference truckRef
                        |> Result.map (fun s -> (s, [TruckReferenceAdded truckRef]))
                member this.Undoer = None
                