namespace ShoppingCart
open ShoppingCart.Commons
open System
open Sharpino.Core

open MBrace.FsPickler.Json
open ShoppingCart.GoodsContainer

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
            globalSerializer.Deserialize<GoodsContainerEvents> x
        member this.Serialize =
            globalSerializer.Serialize this

