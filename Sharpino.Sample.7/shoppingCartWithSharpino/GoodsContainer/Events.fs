namespace ShoppingCart
open Sharpino.Commons
open System
open Sharpino.Core

open ShoppingCart.GoodsContainer

module GoodsContainerEvents =
    type GoodsContainerEvents =
        | GoodAdded of Guid
        | GoodRemoved of Guid
        | CartAdded of Guid
        | GoodsContainerNoOp
            interface Event<GoodsContainer> with
                member this.Process (goodsContainer: GoodsContainer) =
                    match this with
                    | GoodAdded goodRef -> goodsContainer.AddGood goodRef
                    | GoodRemoved goodRef -> goodsContainer.RemoveGood goodRef
                    | CartAdded cartRef -> goodsContainer.AddCart cartRef
                    | GoodsContainerNoOp -> Ok goodsContainer

        static member Deserialize x =
            jsonPSerializer.Deserialize<GoodsContainerEvents> x
        member this.Serialize =
            jsonPSerializer.Serialize this

