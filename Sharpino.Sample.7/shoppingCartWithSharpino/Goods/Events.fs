namespace ShoppingCart 
open ShoppingCart.Commons

open Sharpino.Core
open MBrace.FsPickler.Json
open ShoppingCart.Good
open Sharpino.Core

module GoodEvents =
    type GoodEvents =   
    | PriceChanged of decimal
    | DiscountsChanged of List<Good.Discount>
    | QuantityAdded of int
    | QuantityRemoved of int
     
        interface Event<Good> with
            member this.Process (good: Good) =
                match this with
                | PriceChanged price -> good.SetPrice price
                | DiscountsChanged discounts -> good.ChangeDiscounts discounts
                | QuantityAdded quantity -> good.AddQuantity quantity
                | QuantityRemoved quantity -> good.RemoveQuantity quantity

        static member Deserialize json =
            globalSerializer.Deserialize<GoodEvents> json // |> Ok

        member this.Serialize =
            globalSerializer.Serialize this

