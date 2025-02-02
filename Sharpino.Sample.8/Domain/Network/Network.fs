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
            TruckRefs: List<TruckId>
        }
        static member MkNetwork () =
            { SiteIds = []; TruckRefs = [] }
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
                    this.TruckRefs |> List.contains truckRef |> not
                    |> Result.ofBool "Truck already added"
                return { this with TruckRefs = truckRef :: this.TruckRefs }    
            }
        static member Zero = { SiteIds = []; TruckRefs = [] }
        static member StorageName = "_network"
        static member Version = "_01"
        static member SnapshotsInterval = 15
        
        static member Deserialize x =
            jsonPSerializer.Deserialize<Network> x
       
        member this.Serialize =
            this |> jsonPSerializer.Serialize