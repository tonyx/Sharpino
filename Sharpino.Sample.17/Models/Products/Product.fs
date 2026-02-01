namespace Sharpino.Template.Models

open Sharpino
open Sharpino.Template
open Sharpino.Template.Commons
open Sharpino.Core
open System.Text.Json
open FsToolkit.ErrorHandling

    type Product =
        {
            ProductId: ProductId
            Name: string
            Materials: List<MaterialId * Quantity>
        }
        static member New (name: string) (materialsWithQuantities: List<MaterialId * Quantity>) =
            { ProductId = ProductId.New; Name = name; Materials = materialsWithQuantities }

        
        // ---
        member this.Id = this.ProductId.Value
        static member SnapshotsInterval = 50
        static member StorageName = "_Products"
        static member Version = "_01"
        
        member this.Serialize =
            JsonUtils.serializeJson this
        
        static member Deserialize (data: string) =
            JsonUtils.DeserializeJson<Product> data
                
        