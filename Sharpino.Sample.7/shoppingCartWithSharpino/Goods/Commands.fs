
namespace ShoppingCart 

open ShoppingCart.Commons
open Sharpino.Core
open FsToolkit.ErrorHandling
open ShoppingCart.Good
open ShoppingCart.GoodEvents
open Sharpino
open MBrace.FsPickler.Json

module GoodCommands =
    type GoodCommands =
        | ChangePrice of decimal
        | ChangeDiscounts of List<Good.Discount>
        | AddQuantity of int
        | RemoveQuantity of int
            interface AggregateCommand<Good, GoodEvents> with
                member this.Execute (good: Good) =
                    match this with
                    | ChangePrice price -> 
                        good.SetPrice price
                        |> Result.map (fun s -> (s, [PriceChanged price]))
                    | ChangeDiscounts discounts ->
                        good.ChangeDiscounts discounts
                        |> Result.map (fun s -> (s, [DiscountsChanged discounts]))
                    | AddQuantity quantity ->
                        good.AddQuantity quantity
                        |> Result.map (fun s -> (s, [QuantityAdded quantity]))
                    | RemoveQuantity quantity ->
                        good.RemoveQuantity quantity
                        |> Result.map (fun s -> (s, [QuantityRemoved quantity]))
                member this.Undoer = 
                    match this with
                    | ChangePrice _ -> 
                        Some 
                            (fun (good: Good) (viewer: AggregateViewer<Good>) ->
                                result {
                                    let! (i, state) = viewer (good.Id) 
                                    let oldPrice = state.Price
                                    return
                                        fun () ->
                                            result {
                                                let! (j, state) = viewer (good.Id)
                                                let! isGreater = 
                                                    (j >= i)
                                                    |> Result.ofBool (sprintf "execution undo state '%d' must be after the undo command state '%d'" j i)
                                                let result =
                                                    state.SetPrice oldPrice 
                                                    |> Result.map (fun _ -> [PriceChanged oldPrice])
                                                return! result
                                            }
                                    }
                            )
                    | ChangeDiscounts _ ->
                        Some 
                            (fun (good: Good) (viewer: AggregateViewer<Good>) ->
                                result {
                                    let! (i, state) = viewer (good.Id) 
                                    let oldDiscounts = state.Discounts
                                    return
                                        fun () ->
                                            result {
                                                let! (j, state) = viewer (good.Id)
                                                let! isGreater = 
                                                    (j >= i)
                                                    |> Result.ofBool (sprintf "execution undo command state '%d' must be after the undo command state '%d'" j i)
                                                let result =
                                                    state.ChangeDiscounts oldDiscounts
                                                    |> Result.map (fun _ -> [DiscountsChanged oldDiscounts])
                                                return! result
                                            }
                                    }
                            )       
                    | AddQuantity x -> 
                        Some 
                            (fun (good: Good) (viewer: AggregateViewer<Good>) ->
                                result {
                                    let! (i, state) = viewer (good.Id) 
                                    return
                                        fun () ->
                                            result {
                                                let! (j, state) = viewer (good.Id)
                                                let! isGreater = 
                                                    (j >= i)
                                                    |> Result.ofBool (sprintf "execution undo command state '%d' must be after the undo command state '%d'" j i)
                                                let result =
                                                    state.RemoveQuantity x
                                                    |> Result.map (fun _ -> [QuantityRemoved x])
                                                return! result
                                            }
                                    }

                            )
                    | RemoveQuantity x ->
                        Some 
                            (fun (good: Good) (viewer: AggregateViewer<Good>) ->
                                result {
                                    let! (i, state) = viewer (good.Id) 
                                    return
                                        fun () ->
                                            result {
                                                let! (j, state) = viewer (good.Id)
                                                let! isGreater = 
                                                    (j >= i)
                                                    |> Result.ofBool (sprintf "execution undo command state '%d' must be after the undo command state '%d'" j i)
                                                let result =
                                                    state.AddQuantity x
                                                    |> Result.map (fun _ -> [QuantityAdded x])
                                                return! result
                                            }
                                    }
                            )

            static member Deserialize json =
                globalSerializer.Deserialize<GoodCommands> json
            member this.Serialize =
                globalSerializer.Serialize this
