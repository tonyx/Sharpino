namespace Sharpino.TransportTycoon

open System
open Sharpino
open Sharpino.Commons
open Sharpino.Core
open Sharpino.TransportTycoon.Definitions
open FSharpPlus
open FSharpPlus.Operators
open FsToolkit.ErrorHandling

module Site =
    type Site =
        {
            Id: Guid
            SiteType: SiteType
            Trucks: List<Guid>
            SiteConnections: List<Connection>
        }
        static member MkSite (id: Guid, siteType: SiteType) =
            { Id = id; SiteType = siteType; Trucks = [] ; SiteConnections = [] }
        member this.PlaceTruck (truckRef: Guid) =
            result {
                do! 
                    this.Trucks |> List.contains truckRef |> not
                    |> Result.ofBool "Truck already placed"
                return { this with Trucks = truckRef :: this.Trucks } 
            }
        member this.AddConnection (connection:  Connection) =
            result
                {
                    let endNodesOfCurrentConnections =
                        this.SiteConnections
                        |>> _.EndInterval
                    
                    do! 
                        endNodesOfCurrentConnections
                        |> List.contains connection.EndInterval
                        |> not
                        |> Result.ofBool "Connection already exists"    
                    
                    return
                        {
                            this with
                                SiteConnections = connection :: this.SiteConnections
                        }
                } 
        static member StorageName = "_site"
        static member Version = "_01"
        static member SnapshotsInterval = 15
        static member Deserialize x = 
            jsonPSerializer.Deserialize<Site> x
        member this.Serialize = 
            jsonPSerializer.Serialize this
