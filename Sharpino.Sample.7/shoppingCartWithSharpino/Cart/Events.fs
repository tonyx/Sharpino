
namespace ShoppingCart 

open Sharpino.Commons
open ShoppingCart.Cart
open System
open Sharpino.Core

module CartEvents =
    type CartEvents =
    | GoodAdded of Guid * int
    | GoodRemoved of Guid
        interface Event<Cart> with
            member this.Process (cart: Cart) =
                match this with
                | GoodAdded (goodRef, quantity) -> cart.AddGood (goodRef, quantity)
                | GoodRemoved goodRef -> cart.RemoveGood goodRef

        static member Deserialize json =
            jsonPSerializer.Deserialize<CartEvents> json

        member this.Serialize =
            jsonPSerializer.Serialize this
