namespace ShoppingCart 

open Sharpino.Core
open Sharpino.Commons
open ShoppingCart.Good

module GoodEvents =
    type GoodEvents =   
    | PriceChanged of decimal
    | QuantityAdded of int
    | QuantityRemoved of int
     
        interface Event<Good> with
            member this.Process (good: Good) =
                match this with
                | PriceChanged price -> good.SetPrice price
                | QuantityAdded quantity -> good.AddQuantity quantity
                | QuantityRemoved quantity -> good.RemoveQuantity quantity

        static member Deserialize json =
            jsonPSerializer.Deserialize<GoodEvents> json // |> Ok

        member this.Serialize =
            jsonPSerializer.Serialize this

