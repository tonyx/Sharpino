namespace ShoppingCartBinary

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
    let doNothingBroker: IEventBroker<_> =
        {  notify = None
           notifyAggregate = None }
        
    type Supermarket (eventStore: IEventStore<'F>, eventBroker: IEventBroker<_>, goodsContainerViewer:StateViewer<GoodsContainer>, goodsViewer:AggregateViewer<Good>, cartViewer:AggregateViewer<Cart> ) =
        new (eventStore: IEventStore<'F>, eventBroker: IEventBroker<_>) =
            let goodsContainerViewer:StateViewer<GoodsContainer> = getStorageFreshStateViewer<GoodsContainer, GoodsContainerEvents, byte[]> eventStore
            let goodsViewer:AggregateViewer<Good> = getAggregateStorageFreshStateViewer<Good, GoodEvents, byte[]> eventStore
            let cartViewer:AggregateViewer<Cart> = getAggregateStorageFreshStateViewer<Cart, CartEvents, byte[]> eventStore
            Supermarket (eventStore, eventBroker, goodsContainerViewer, goodsViewer, cartViewer)

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
                let! esists = this.GetGood goodId
                let! (_, state) = goodsViewer goodId
                return state.Quantity
            }

        member this.AddQuantity (goodId: Guid, quantity: int) = 
            result {
                let! (_, state) = goodsViewer goodId
                let command = GoodCommands.AddQuantity  quantity
                return! 
                    command 
                    |> runAggregateCommand<Good, GoodEvents, byte[]> goodId eventStore eventBroker
            }

        member this.GetGood (id: Guid) = 
            result {
                let! goods = this.GoodRefs
                let! goodExist = 
                    goods
                    |> List.tryFind (fun g -> g = id)
                    |> Result.ofOption "Good not found"
                let! (_, state) = goodsViewer id
                return state
            }
        member this.Goods =
            result {
                let! (_, state) = goodsContainerViewer ()

                // warning: if there is a ref to an unexisting good you are in trouble. fix it
                let! goods =
                    state.GoodRefs
                    |> List.map this.GetGood
                    |> Result.sequence
                return goods |> Array.toList
            }

        member this.AddGood (good: Good) =  
            result {
                let existingGoods = 
                    this.Goods
                    |> Result.defaultValue []
                do! 
                    existingGoods
                    |> List.exists (fun g -> g.Name = good.Name)
                    |> not
                    |> Result.ofBool "Good already in items list"

                let! goodAdded =
                    good.Id 
                    |> AddGood 
                    |> runInitAndCommand<GoodsContainer, GoodsContainerEvents, Good, 'F> eventStore eventBroker good
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
                let! good = this.GetGood id
                let! (_, state) = goodsContainerViewer ()
                let command = GoodsContainerCommands.RemoveGood id
                return! 
                    command
                    |> runCommand<GoodsContainer, GoodsContainerEvents, byte[]> eventStore eventBroker 
            }

        member this.AddCart (cart: Cart) = 
            result {
                return! 
                    cart.Id
                    |> AddCart
                    |> runInitAndCommand<GoodsContainer, GoodsContainerEvents, Cart, byte[]> eventStore eventBroker cart
            }

        member this.GetCart (cartRef: Guid) = 
            result {
                let! cartRefs = this.CartRefs
                let! exists =
                    cartRefs
                    |> List.tryFind (fun c -> c = cartRef)
                    |> Result.ofOption "Cart not found"
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
                        eventBroker 
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
                        eventBroker 
                        removeFromMarket
                        addToCart
            } 


  
