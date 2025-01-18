namespace ShoppingCart

open System
open Sharpino
open Sharpino.Core
open FsToolkit.ErrorHandling
open ShoppingCart.GoodsContainer
open ShoppingCart.GoodsContainerEvents

module GoodsContainerCommands =
    type GoodsContainerCommands =
        | AddGood of Guid
        | RemoveGood of Guid
        | AddCart of Guid
            interface Command<GoodsContainer, GoodsContainerEvents> with
                member this.Execute (goodsContainer: GoodsContainer) =
                    match this with
                    | AddGood goodRef -> 
                        goodsContainer.AddGood goodRef
                        |> Result.map (fun s -> (s, [GoodAdded goodRef]))
                    | RemoveGood goodRef ->
                        goodsContainer.RemoveGood goodRef
                        |> Result.map (fun s -> (s, [GoodRemoved goodRef]))
                    | AddCart cartRef ->
                        goodsContainer.AddCart cartRef
                        |> Result.map (fun s -> (s, [CartAdded cartRef]))
                member this.Undoer = 
                    match this with
                    | AddGood goodRef -> 
                        Some 
                            (fun (goodsContainer: GoodsContainer) (viewer: StateViewer<GoodsContainer>) ->
                                result {
                                    let! (i, _) = viewer ()
                                    return
                                        fun () ->
                                            result {
                                                let! (j, state) = viewer ()
                                                let! isGreater = 
                                                    (j >= i)
                                                    |> Result.ofBool (sprintf "execution undo state '%d' must be after the undo command state '%d'" j i)
                                                let result =
                                                    state.RemoveGood goodRef
                                                    |> Result.map (fun _ -> [GoodRemoved goodRef])
                                                return! result
                                            }
                                    }
                            )
                    | _ -> None




