
namespace ShoppingCart 

open Sharpino.Core
open FsToolkit.ErrorHandling
open ShoppingCart.Good
open ShoppingCart.GoodEvents
open Sharpino

module GoodCommands =
    type GoodCommands =
        | ChangePrice of decimal
        | AddQuantity of int
        | RemoveQuantity of int
            interface AggregateCommand<Good, GoodEvents> with
                member this.Execute (good: Good) =
                    match this with
                    | ChangePrice price -> 
                        good.SetPrice price
                        |> Result.map (fun s -> (s, [PriceChanged price]))
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
                                                    |> Result.map (fun s -> s, [PriceChanged oldPrice])
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
                                                    |> Result.map (fun s -> s, [QuantityRemoved x])
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
                                                    |> Result.map (fun s -> s, [QuantityAdded x])
                                                return! result
                                            }
                                    }
                            )
