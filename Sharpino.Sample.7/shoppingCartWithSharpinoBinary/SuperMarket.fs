namespace ShoppingCartBinary

open Sharpino.EventBroker
open Sharpino.StateView
open ShoppingCartBinary.Good
open ShoppingCartBinary.GoodEvents
open ShoppingCartBinary.GoodsContainer
open ShoppingCartBinary.GoodsContainerEvents
open ShoppingCartBinary.GoodsContainerCommands
open ShoppingCartBinary.Cart
open ShoppingCartBinary.CartEvents

open System
open Sharpino
open Sharpino.Storage
open Sharpino.Core
open FSharpPlus
open FsToolkit.ErrorHandling

module Supermarket =
    open Sharpino.CommandHandler
        
    // type Supermarket (eventStore: IEventStore<'F>, eventBroker: IEventBroker<_>, goodsContainerViewer:StateViewer<GoodsContainer>, goodsViewer:AggregateViewer<Good>, cartViewer:AggregateViewer<Cart> ) =
    type Supermarket (eventStore: IEventStore<'F>, messageSender: string -> MessageSender, goodsContainerViewer:StateViewer<GoodsContainer>, goodsViewer:AggregateViewer<Good>, cartViewer:AggregateViewer<Cart> ) =
        new (eventStore: IEventStore<'F>, messageSender: string -> MessageSender) =
            let goodsContainerViewer:StateViewer<GoodsContainer> = getStorageFreshStateViewer<GoodsContainer, GoodsContainerEvents, byte[]> eventStore
            let goodsViewer:AggregateViewer<Good> = getAggregateStorageFreshStateViewer<Good, GoodEvents, byte[]> eventStore
            let cartViewer:AggregateViewer<Cart> = getAggregateStorageFreshStateViewer<Cart, CartEvents, byte[]> eventStore
            Supermarket (eventStore, messageSender, goodsContainerViewer, goodsViewer, cartViewer)

        member this.GoodRefs = 
            result {
                let! (_, state) = goodsContainerViewer ()
                return state.GoodRefs
            }
        member this.CartRefs =
            result {
                let! (_, state) = goodsContainerViewer ()
                return state.CartRefs
            }

        member this.GetGoodsQuantity (goodId: Guid) = 
            result {
                let! (_, state) = goodsViewer goodId
                return state.Quantity
            }

        member this.AddQuantity (goodId: Guid, quantity: int) = 
            result {
                let! (_, state) = goodsViewer goodId
                let command = GoodCommands.AddQuantity  quantity
                return! 
                    command 
                    |> runAggregateCommand<Good, GoodEvents, byte[]> goodId eventStore messageSender
            }
            
        member this.SetPrice (goodId: Guid, price: decimal) = 
            result {
                let! (_, state) = goodsViewer goodId
                let command = GoodCommands.ChangePrice  price
                return! 
                    command 
                    |> runAggregateCommand<Good, GoodEvents, byte[]> goodId eventStore messageSender
            }     
            
        member this.RetrieveGoodBypassingContainer (id: Guid)    =     
            result {
                let! (_, state) = goodsViewer id
                return state
            }

        member this.GetGood (id: Guid) =
            result {
                let! (_, state) = goodsViewer id
                return state
            }
        
        member this.Goods=
            result {
                let! goods =
                    getFilteredAggregateStatesInATimeInterval2<Good, GoodEvents, 'F>
                        eventStore
                        DateTime.MinValue
                        DateTime.MaxValue
                        (fun _ -> true)
                return goods |>> snd  
            }

        member this.AddGood (good: Good) =  
            result {
                
                let! existingGoods =
                    getFilteredAggregateStatesInATimeInterval2<Good, GoodEvents, 'F>
                        eventStore
                        DateTime.MinValue
                        DateTime.MaxValue
                        (fun g -> g.Name = good.Name)
                
                let! existsWithTheSameName =
                    existingGoods
                    |> List.length = 0
                    |> Result.ofBool "Good already in items list"

                let! goodAdded =
                    good
                    |> runInit<Good, GoodEvents,'F> eventStore messageSender
                return ()
            }
        member this.AddGoodAsync (good: Good) =
            async {
                return 
                    this.AddGood good
            }
            |> Async.StartAsTask

        member this.RemoveGood (id: Guid) =
            result {
                let! (_, good) = goodsViewer id
                return! 
                    runDelete<Good, GoodEvents, byte[]> eventStore messageSender id (fun _ -> true)
            }

        member this.AddCart (cart: Cart) = 
            result {
                return! 
                    cart
                    |> runInit<Cart, CartEvents,'F> eventStore messageSender
            }

        member this.GetCart (cartRef: Guid) = 
            result {
                let! (_, state) = cartViewer cartRef
                return state
            }

        member this.AddGoodToCart (cartId: Guid, goodId: Guid, quantity: int) =
            result {
                let removeQuantity: AggregateCommand<Good, GoodEvents> = GoodCommands.RemoveQuantity quantity
                let addGood: AggregateCommand<Cart, CartEvents> = CartCommands.AddGood (goodId, quantity) 
                return! 
                    runTwoNAggregateCommands 
                        [goodId]
                        [cartId] 
                        eventStore 
                        messageSender
                        [removeQuantity] 
                        [addGood] 
            }

        member this.AddGoodsToCart (cartId: Guid, goods: (Guid * int) list) =
            result {
                let commands = 
                    goods
                    |> List.map (fun (goodId, quantity) -> 
                        let removeSomeQuantity: AggregateCommand<Good, GoodEvents> = GoodCommands.RemoveQuantity quantity
                        let addGood: AggregateCommand<Cart, CartEvents> = CartCommands.AddGood (goodId, quantity) 
                        (removeSomeQuantity, addGood)
                    )
                let goodIds = goods |>> fst
                let removeFromMarket = commands |>> fst
                let addToCart = commands |>> snd
                let size = goods.Length

                let cartids =
                    [1..size] |>> (fun _ -> cartId)
                
                return!
                    forceRunTwoNAggregateCommands 
                        goodIds
                        cartids
                        eventStore 
                        messageSender
                        removeFromMarket
                        addToCart
            } 


  
