namespace ShoppingCartBinary
open ShoppingCartBinary.Good
open Sharpino.Core
open Sharpino.Commons

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

        static member Deserialize x =
            binarySerializer.Deserialize<GoodEvents> x // |> Ok

        member this.Serialize =
            binarySerializer.Serialize this

    


