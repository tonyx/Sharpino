namespace Tonyx.Sharpino.Pub
open System.Threading.Tasks
open Sharpino
open Sharpino.EventBroker
open Tonyx.Sharpino.Pub.Commons
open Tonyx.Sharpino.Pub.Supplier

open Tonyx.Sharpino.Pub.Dish
open Sharpino.CommandHandler
open Sharpino.Definitions
open System
open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Sharpino.Storage
open Sharpino.Core
open Sharpino.Utils
open System
open Tonyx.Sharpino.Pub.SupplierEvents

module PubSystem =
    open DishEvents
    open Ingredient
    open IngredientEvents
        
    let emptyMessageSender =
        fun queueName ->
            fun message ->
                ValueTask.CompletedTask
    // type PubSystem (storage: IEventStore<string>, messageSender: string -> MessageSender) =
    type PubSystem (storage: IEventStore<string>, messageSenders: MessageSenders, AggregateStateViewer : AggregateViewer<Ingredient>) =
            let ingredientStateViewer = getAggregateStorageFreshStateViewer<Ingredient, IngredientEvents, string> storage

            new (storage: IEventStore<string>) =
                PubSystem(storage, MessageSenders.NoSender, getAggregateStorageFreshStateViewer<Ingredient, IngredientEvents, string> storage)
            member this.AddDish (dish: Dish) =
                result {
                    let! result = runInit<Dish, DishEvents, string> storage messageSenders dish
                    return result
                }
                
            member this.AddIngredient ( ingredientId: Guid, name: string ) =
                result {
                    let! result = runInit<Ingredient, IngredientEvents, string> storage messageSenders (Ingredient.Ingredient(ingredientId, name, [], []))
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
