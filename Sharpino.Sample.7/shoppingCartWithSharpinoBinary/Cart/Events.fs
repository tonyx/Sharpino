namespace ShoppingCartBinary

open ShoppingCartBinary.Cart
open System
open Sharpino.Commons
open Sharpino.Core
module CartEvents =
    type CartEvents =
    | GoodAdded of Guid * int
    | GoodRemoved of Guid
    | GoodsAdded of List<(Guid * int)>
        interface Event<Cart> with
            member this.Process (cart: Cart) =
                match this with
                | GoodAdded (goodRef, quantity) -> cart.AddGood (goodRef, quantity)
                | GoodRemoved goodRef -> cart.RemoveGood goodRef
                | GoodsAdded goodsList -> cart.AddGoods goodsList

        static member Deserialize x =
            binarySerializer.Deserialize<CartEvents> x

        member this.Serialize =
            binarySerializer.Serialize this
