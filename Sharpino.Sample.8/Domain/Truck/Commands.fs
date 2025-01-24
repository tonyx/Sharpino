namespace Sharpino.TransportTycoon

open System
open Sharpino
open Sharpino.Commons
open Sharpino.Core
open Sharpino.TransportTycoon.Definitions
open FSharpPlus
open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Sharpino.TransportTycoon.Truck
open Sharpino.TransportTycoon.TruckEvents

module TruckCommands =
    type TruckCommands =
        SetSite of Guid
            interface AggregateCommand<Truck, TruckEvents> with
                member this.Execute (x: Truck) =
                    match this with
                    | SetSite siteId ->
                        x.SetSite siteId
                        |> Result.map (fun x -> (x, [SiteSet siteId]))
                member this.Undoer = None
                