namespace ShoppingCartBinary
open ShoppingCartBinary.GoodsContainer
open Sharpino.Commons
open System
open Sharpino.Core

module GoodsContainerEvents =
    type GoodsContainerEvents =
        | GoodAdded of Guid
        | GoodRemoved of Guid
        | CartAdded of Guid
            interface Event<GoodsContainer> with
                member this.Process (goodsContainer: GoodsContainer) =
                    match this with
                    | GoodAdded goodRef -> goodsContainer.AddGood goodRef
                    | GoodRemoved goodRef -> goodsContainer.RemoveGood goodRef
                    | CartAdded cartRef -> goodsContainer.AddCart cartRef

        static member Deserialize x =
            binarySerializer.Deserialize<GoodsContainerEvents> x
        member this.Serialize =
            binarySerializer.Serialize this

