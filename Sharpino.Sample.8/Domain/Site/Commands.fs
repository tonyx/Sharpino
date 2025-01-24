namespace  Sharpino.TransportTycoon
open Sharpino.TransportTycoon
open Sharpino.TransportTycoon.Site
open Sharpino.TransportTycoon.SiteEvents
open System
open Sharpino
open Sharpino.Commons
open Sharpino.Core
open Sharpino.TransportTycoon.Definitions
open FSharpPlus
open FSharpPlus.Operators
open FsToolkit.ErrorHandling

module SiteCommands =
    type SiteCommands =
        | PlaceTruck of Guid
        | AddConnection of Connection
            interface AggregateCommand<Site.Site, SiteEvents> with
                member this.Execute (site: Site) =
                    match this with
                    | PlaceTruck truckRef -> 
                        site.PlaceTruck truckRef
                        |> Result.map (fun x -> (x, [TruckPlaced truckRef]))
                    | AddConnection connection ->
                        site.AddConnection connection
                        |> Result.map (fun x -> (x, [ConnectionAdded connection]))
                member this.Undoer = None
            
                

