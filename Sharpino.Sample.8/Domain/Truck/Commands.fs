namespace Sharpino.TransportTycoon

open System
open Sharpino
open Sharpino.Commons
open Sharpino.Core
open Sharpino.TransportTycoon.Definitions
open FSharpPlus
open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Sharpino.TransportTycoon.Transporter
open Sharpino.TransportTycoon.TruckEvents

module TruckCommands =
    type TruckCommands =
        SetSite of Guid
            interface AggregateCommand<Transporter, TruckEvents> with
                member this.Execute (x: Transporter) =
                    match this with
                    | SetSite siteId ->
                        x.SetSite siteId
                        |> Result.map (fun x -> (x, [SiteSet siteId]))
                member this.Undoer = None
                