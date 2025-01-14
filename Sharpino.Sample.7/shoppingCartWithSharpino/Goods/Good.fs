namespace ShoppingCart
open System
open ShoppingCart.Commons
open Sharpino
open Sharpino.Core
open Sharpino.Lib.Core.Commons
open MBrace.FsPickler.Json
open FsToolkit.ErrorHandling

module Good =
    type Discount =
        { ItemNumber: int
          Price: decimal }

    type Discounts = List<Discount>

    type Good private (id: Guid, name: string, price: decimal, discounts: Discounts, quantity: int) =
        member this.Id = id
        member this.Name = name
        member this.Price = price
        member this.Discounts = discounts
        member this.Quantity = quantity

        new (id: Guid, name: string, price: decimal, discounts: Discounts) =
            Good (id, name, price, discounts, 0 )

        member this.SetPrice (price: decimal) =
            result {
                do! 
                    price > 0M
                    |> Result.ofBool "Price must be greater than 0"
                let result = Good (this.Id, this.Name, price, this.Discounts, quantity) 
                return result
            }

        member this.ChangeDiscounts(discounts: Discounts) =
            Good (this.Id, this.Name, this.Price, discounts, quantity ) |> Ok

        member this.AddQuantity(quantity: int) =
            Good (this.Id, this.Name, this.Price, this.Discounts, this.Quantity + quantity ) |> Ok

        member this.RemoveQuantity(quantity: int) =
            result {
                do! 
                    this.Quantity - quantity >= 0
                    |> Result.ofBool "Quantity not available"
                return Good (this.Id, this.Name, this.Price, this.Discounts, this.Quantity - quantity )
            }

        override this.GetHashCode() =
            hash (this.Id.GetHashCode(), this.Name.GetHashCode(), this.Price.GetHashCode(), this.Discounts.GetHashCode(), this.Quantity.GetHashCode())

        override this.Equals(obj) = 
            match obj with
            | :? Good as g -> 
                this.Id = g.Id
                && this.Name = g.Name
                && this.Price = g.Price
                && this.Discounts = g.Discounts
                && this.Quantity = g.Quantity
            | _ -> false

        static member StorageName = "_good"
        static member Version = "_01"
        static member SnapshotsInterval = 15 

        static member Deserialize x = 
            globalSerializer.Deserialize x
        member this.Serialize  =
            globalSerializer.Serialize this
        interface Aggregate<string> with
            member this.Id = this.Id
            member this.Serialize  =
                this.Serialize 
        interface Entity with
            member this.Id = this.Id

