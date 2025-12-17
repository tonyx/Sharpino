namespace Tonyx.Sharpino.Pub
open System.Threading.Tasks
open Sharpino
open Sharpino.Cache
open Sharpino.EventBroker
open Tonyx.Sharpino.Pub.Commons
open Tonyx.Sharpino.Pub.Supplier

open Sharpino.CommandHandler
open Sharpino.Definitions
open System
open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Sharpino.Storage
open Sharpino.Core
open Tonyx.Sharpino.Pub.Details
open Sharpino.Utils
open System

module PubSystem =
    let emptyMessageSender =
        fun queueName ->
            fun message ->
                ValueTask.CompletedTask
    type PubSystem (storage: IEventStore<string>, messageSenders: MessageSenders, aggregateStateViewer : AggregateViewer<Ingredient>) =
            let ingredientStateViewer = getAggregateStorageFreshStateViewer<Ingredient, IngredientEvents, string> storage
            let dishStateViewer = getAggregateStorageFreshStateViewer<Dish, DishEvents, string> storage

            new (storage: IEventStore<string>) =
                PubSystem(storage, MessageSenders.NoSender, getAggregateStorageFreshStateViewer<Ingredient, IngredientEvents, string> storage)
            member this.AddDish (dish: Dish) =
                result {
                    let! result = runInit<Dish, DishEvents, string> storage messageSenders dish
                    return result
                }
                
            member this.AddIngredient ( ingredientId: Guid, name: string ) =
                result {
                    let! result = runInit<Ingredient, IngredientEvents, string> storage messageSenders (Ingredient(ingredientId, name, [], []))
                    return result
                }
                
            member this.AddSupplier ( supplier: Supplier ) =
                result {
                    let! result =
                        runInit<Supplier, SupplierEvents, string> storage messageSenders supplier
                    return result
                }
            member this.GetAllSuppliers ()=
                result {
                    let! suppliers =
                            StateView.getAllAggregateStates<Supplier, SupplierEvents, string> storage
                    return suppliers |>> snd
                }
                
            member this.GetAllDishes () =
                result {
                    let! dishes =
                        StateView.getAllAggregateStates<Dish, DishEvents, string> storage
                   
                    let dishes =
                        dishes |>> snd
                        
                    return dishes
                }
           
            member this.GetDishDetails (id: Guid) =
                let detailsBuilder =
                    fun () ->
                        let refresher =
                            fun () ->
                                result {
                                    let! _, dish = dishStateViewer id
                                    let! ingredients =
                                        dish.Ingredients
                                        |> List.traverseResultM (fun id -> this.GetIngredient id)
                                    return dish, ingredients
                                }
                        result
                            {
                                let! course, ingredients = refresher ()
                                return
                                    (
                                        {
                                            Dish = course
                                            Ingredients = ingredients
                                            Refresher = refresher
                                        }
                                    ) :> Refreshable<_>
                                    ,
                                    id:: (ingredients |> List.map _.Id)
                            }
                let key = DetailsCacheKey (typeof<DishDetails>, id)
                StateView.getRefreshableDetails<DishDetails> detailsBuilder key 
                
            member this.GetIngredient ( guid: Guid ) =
                result {
                    let! _, ingredient = ingredientStateViewer guid
                    return ingredient
                }
                
            member this.GetAllIngredients () =
                result {
                    let! ingredients =
                        StateView.getAllAggregateStates<Ingredient, IngredientEvents, string> storage
                    let ingredients =
                        ingredients |>> snd    
                    
                    return ingredients
                }
                
            member this.AddTypeToIngredient ( guid: Guid, ingredientType: IngredientType ) =
                result {
                    let! ingredient = this.GetIngredient guid
                    let addIngredientType = IngredientCommands.AddIngredientType ingredientType 
                    let! result = runAggregateCommandMd<Ingredient, IngredientEvents, string> guid storage messageSenders "metadata" addIngredientType 
                    return result
                }
                
            member this.AddMeasureType ( guid: Guid, measureType: MeasureType ) =
                result {
                    let! ingredient = this.GetIngredient guid
                    let addMeasureType = IngredientCommands.AddMeasureType measureType
                    let! result = runAggregateCommandMd<Ingredient, IngredientEvents, string> guid storage messageSenders "metadata" addMeasureType 
                    return result
                }     
