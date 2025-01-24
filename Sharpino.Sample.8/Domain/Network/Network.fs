namespace Sharpino.TransportTycoon

open System
open Sharpino
open Sharpino.Commons
open FsToolkit.ErrorHandling

module Network =
    type Network =
        {
            SiteRefs: List<Guid>
            TruckRefs: List<Guid>
        }
        static member MkNetwork () =
            { SiteRefs = []; TruckRefs = [] }
        member this.AddSiteReference (siteRef: Guid) =
            result {
                do!
                    this.SiteRefs |> List.contains siteRef |> not
                    |> Result.ofBool "Site already added"
                return { this with SiteRefs = siteRef :: this.SiteRefs }    
            }
        member this.AddTruckReference (truckRef: Guid) =
            result {
                do!
                    this.TruckRefs |> List.contains truckRef |> not
                    |> Result.ofBool "Truck already added"
                return { this with TruckRefs = truckRef :: this.TruckRefs }    
            }
        static member Zero = { SiteRefs = []; TruckRefs = [] }
        static member StorageName = "_network"
        static member Version = "_01"
        static member SnapshotsInterval = 15
        
        static member Deserialize x =
            jsonPSerializer.Deserialize<Network> x
       
        member this.Serialize =
            this |> jsonPSerializer.Serialize