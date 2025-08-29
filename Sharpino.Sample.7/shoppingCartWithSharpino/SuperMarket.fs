namespace ShoppingCart

open Sharpino.EventBroker
open Sharpino.StateView
open ShoppingCart.Good
open ShoppingCart.GoodEvents
open ShoppingCart.GoodsContainer
open ShoppingCart.GoodsContainerEvents
open ShoppingCart.GoodsContainerCommands
open ShoppingCart.Cart
open ShoppingCart.CartEvents

open System
open Sharpino
open Sharpino.Storage
open Sharpino.Core
open FSharpPlus
open FsToolkit.ErrorHandling

module Supermarket =
    open Sharpino.CommandHandler
    let legacyBroker: IEventBroker<_> =
        {  notify = None
           notifyAggregate = None }
    
    let emptyMessageSender =
        fun version name aggregateId eventId events -> Result.Ok ()

    type Supermarket (eventStore: IEventStore<'F>, eventBroker: MessageSenders, goodsContainerViewer:StateViewer<GoodsContainer>, goodsViewer:AggregateViewer<Good>, cartViewer:AggregateViewer<Cart> ) =
        new (eventStore: IEventStore<'F>, eventBroker: MessageSenders) =
            let goodsContainerViewer:StateViewer<GoodsContainer> = getStorageFreshStateViewer<GoodsContainer, GoodsContainerEvents, string> eventStore
            let goodsViewer:AggregateViewer<Good> = getAggregateStorageFreshStateViewer<Good, GoodEvents, string> eventStore
            let cartViewer:AggregateViewer<Cart> = getAggregateStorageFreshStateViewer<Cart, CartEvents, string> eventStore
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
                let! (_, state) = goodsViewer goodId
                return state.Quantity
            }

        member this.AddQuantity (goodId: Guid, quantity: int) = 
            result {
                let! (_, state) = goodsViewer goodId
                let command = GoodCommands.AddQuantity  quantity
                return! 
                    command 
                    |> runAggregateCommand<Good, GoodEvents, string> goodId eventStore eventBroker
            }
        
        member this.SetPrice (goodId: Guid, price: decimal) = 
            result {
                let! (_, state) = goodsViewer goodId
                let command = GoodCommands.ChangePrice  price
                return! 
                    command 
                    |> runAggregateCommand<Good, GoodEvents, string> goodId eventStore eventBroker
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
        
        member this.AddGood (good: Good)     =
            result
                {
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
                        
                    let! shoultNotExits =
                        StateView.getAggregateFreshState<Good, GoodEvents, 'F> good.Id eventStore
                        |> Result.toOption
                        |> Option.isNone
                        |> Result.ofBool "Good already in items list"
                        
                    let! goodAdded =
                        good
                        |> runInit<Good, GoodEvents,'F> eventStore eventBroker
                    return ()     
                }
       
        member this.RetrieveGoodBypassingContainer (id: Guid)    =     
            result {
                let! (_, state) = goodsViewer id
                return state
            }

        member this.RemoveGood (id: Guid) = 
            result {
                let! (_, good) = goodsViewer id
                return! 
                    runDelete<Good, GoodEvents, string> eventStore eventBroker id (fun _ -> true)
                    // command
                    // |> runCommand<GoodsContainer, GoodsContainerEvents, string> eventStore legacyBroker 
            }

        member this.AddCart (cart: Cart) = 
            result {
                return! 
                    cart
                    |> runInit<Cart, CartEvents,'F> eventStore eventBroker
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


  