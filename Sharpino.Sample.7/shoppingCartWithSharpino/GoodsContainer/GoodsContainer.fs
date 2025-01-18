namespace ShoppingCart
open Sharpino.Commons
open System
open Sharpino

open FsToolkit.ErrorHandling

module GoodsContainer =

    type GoodsContainer =
        {
            GoodRefs: List<Guid>
            CartRefs: List<Guid>
        }
         
        static member MkGoodsContainer () =
                { GoodRefs = []; CartRefs = [] }
        member
            this.AddGood(goodRef: Guid) =
                result {
                    do! 
                        this.GoodRefs 
                        |> List.contains goodRef
                        |> not
                        |> Result.ofBool "Good already in items list"
                    return { this with GoodRefs = goodRef :: this.GoodRefs }
                }
        member
            this.RemoveGood(goodRef: Guid) =
                { this with GoodRefs = this.GoodRefs |> List.filter (fun x -> x <> goodRef) } |> Ok
        member        
            this.AddCart(cartRef: Guid) =
                { this with CartRefs = cartRef :: this.CartRefs } |> Ok
                
        static member Zero = {GoodRefs = []; CartRefs = []}
        static member StorageName = "_goodsContainer"
        static member Version = "_01"
        static member SnapshotsInterval = 15
        static member Deserialize x =
            jsonPSerializer.Deserialize<GoodsContainer> x 
        member this.Serialize =
            this |> jsonPSerializer.Serialize
