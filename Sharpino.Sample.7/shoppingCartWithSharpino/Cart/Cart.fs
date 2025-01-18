
namespace ShoppingCart
open System
open Sharpino
open Sharpino.Commons
open Sharpino.Core
open FsToolkit.ErrorHandling

module Cart =
    type Cart =
        {
            Id: Guid
            Goods: Map<Guid, int>
        }
        static member MkCart (id: Guid) =
            { Id = id; Goods = Map.empty }
        member this.AddGood (goodRef: Guid, quantity: int) =
            { this with Goods = this.Goods.Add(goodRef, quantity) } |> Ok
        member this.GetGoodsQuantity (goodRef: Guid) =
            result {
                do! 
                    this.Goods.ContainsKey goodRef
                    |> Result.ofBool "good not in cart"
                let quantity = this.Goods.[goodRef]
                return quantity
            }
        member this.RemoveGood (goodRef: Guid) =
            result {
                do! 
                    this.Goods.ContainsKey goodRef
                    |> Result.ofBool "good not in cart"
                return     
                    { this with Goods = this.Goods.Remove goodRef }
            }

        static member StorageName = "_cart" 
        static member Version = "_01"
        static member SnapshotsInterval = 15
        static member Deserialize  x =
            jsonPSerializer.Deserialize<Cart> x
        member this.Serialize =
            jsonPSerializer.Serialize this

        interface Aggregate<string> with
            member this.Id = this.Id
            member this.Serialize  =
                this.Serialize 
        
