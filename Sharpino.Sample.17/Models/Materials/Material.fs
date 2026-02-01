namespace Sharpino.Template.Models

open Sharpino.Template
open Sharpino.Template.Commons
open Sharpino.Core
open System.Text.Json
open FsToolkit.ErrorHandling
open System

    type Material =
        {
            MaterialId: MaterialId
            Name: string
            Availability: Quantity
        }
        static member New (name: string) (quantity: Quantity) =
            {
                MaterialId = MaterialId.New
                Name = name
                Availability = quantity
            }
            
        member this.Consume (quantity: Quantity) =
            result
                {
                    let! newQuantity = this.Availability.Subtract quantity
                    return { this with Availability = newQuantity }
                }
                
        member this.Add (quantity: Quantity) =
            result
                {
                    return { this with Availability = this.Availability.Add quantity }
                }
                
        // ----
        
        member this.Id = this.MaterialId.Value
        static member SnapshotsInterval = 50
        static member StorageName = "_Materials"
        static member Version = "_01"
        
        member this.Serialize =
            JsonUtils.serializeJson this
        
        static member Deserialize (data: string) =
            JsonUtils.DeserializeJson<Material> data
        