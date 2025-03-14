namespace Sharpino.TransportTycoon

open System
open Sharpino
open Sharpino.Commons
open Sharpino.TransportTycoon.Definitions
open FsToolkit.ErrorHandling

module Network =
    type Network =
        {
            SiteIds: List<SiteId>
            TransporterIds: List<TransporterId>
        }
        static member MkNetwork () =
            { SiteIds = []; TransporterIds = [] }
        member this.AddSiteReference (siteRef: Guid) =
            result {
                do!
                    this.SiteIds |> List.contains siteRef |> not
                    |> Result.ofBool (sprintf "Site  %A already added" siteRef)
                return { this with SiteIds = siteRef :: this.SiteIds }    
            }
        member this.AddTruckReference (truckRef: Guid) =
            result {
                do!
                    this.TransporterIds |> List.contains truckRef |> not
                    |> Result.ofBool "Truck already added"
                return { this with TransporterIds = truckRef :: this.TransporterIds }    
            }
        static member Zero = { SiteIds = []; TransporterIds = [] }
        static member StorageName = "_network"
        static member Version = "_01"
        static member SnapshotsInterval = 15
        
        static member Deserialize x =
            jsonPSerializer.Deserialize<Network> x
       
        member this.Serialize =
            this |> jsonPSerializer.Serialize