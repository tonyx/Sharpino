namespace ShoppingCartBinary
open System
open Sharpino.Core
open Sharpino
open Sharpino.Commons
open FsToolkit.ErrorHandling

module Good =
    type Good =
        {
            Id: Guid
            Name: string
            Price: decimal
            Quantity: int
        }
        static member MkGood (id: Guid, name: string) =
            { Id = id; Name = name; Price = 0M; Quantity = 0 }
        static member MkGood (id: Guid, name: string, price: decimal) =
            { Id = id; Name = name; Price = price; Quantity = 0 }
        member
            this.SetPrice (price: Decimal) =
                result
                    {
                        do! 
                            price > 0M
                            |> Result.ofBool "Price must be greater than 0"
                        return { this with Price = price }        
                    }
        member this.AddQuantity (quantity: int) =
            result {
                do!
                    quantity > 0
                    |> Result.ofBool "Quantity must be greater than 0"
                return { this with Quantity = this.Quantity + quantity }
            }
        member this.RemoveQuantity (quantity: int) =
            result {
                do!
                    this.Quantity - quantity >= 0
                    |> Result.ofBool "Quantity not available"
                return { this with Quantity = this.Quantity - quantity }
            }

        static member StorageName = "_good"
        static member Version = "_01"
        static member SnapshotsInterval = 15 

        static member Deserialize x = 
            binarySerializer.Deserialize x
        member this.Serialize  =
            binarySerializer.Serialize this
        interface Aggregate<byte[]> with
            member this.Id = this.Id
            member this.Serialize  =
                this.Serialize 


    
