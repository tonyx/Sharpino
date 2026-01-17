namespace Sharpino.Template.Models

open Sharpino
open Sharpino.Template
open Sharpino.Template.Commons
open Sharpino.Core
open System.Text.Json
open FsToolkit.ErrorHandling

    type Product =
        {
            Id: ProductId
            Name: string
            Materials: List<MaterialId * Quantity>
        }
        static member New (name: string) (materialsWithQuantities: List<MaterialId * Quantity>) =
            { Id = ProductId.New; Name = name; Materials = materialsWithQuantities }

        // ---
        static member SnapshotsInterval = 50
        static member StorageName = "_Products"
        static member Version = "_01"
        
        member this.Serialize =
            JsonUtils.serializeJson this
        
        static member Deserialize (data: string) =
            JsonUtils.DeserializeJson<Product> data
                
        interface Aggregate<string> with
            member this.Id = this.Id.Value
            member this.Serialize = this.Serialize          
        