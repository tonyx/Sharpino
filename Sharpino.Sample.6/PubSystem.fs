namespace Tonyx.Sharpino.Pub
open Tonyx.Sharpino.Pub.Commons
open Tonyx.Sharpino.Pub.Kitchen
open Tonyx.Sharpino.Pub.KitchenEvents
open Tonyx.Sharpino.Pub.Supplier

open Tonyx.Sharpino.Pub.KitchenCommands
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

    let doNothingBroker: IEventBroker<string> =
        {
            notify = None
            notifyAggregate = None
        }
    let connection =
        "Server=127.0.0.1;"+
        "Database=es_pub_sharpino;" +
        "User Id=safe;"+
        "Password=XXXXX;"

    type PubSystem (storage: IEventStore<string>, eventBroker: IEventBroker<string>) =
            let kitchenStateViewer = getStorageFreshStateViewer<Kitchen, KitchenEvents, string> storage
            let dishStateViewer = getAggregateStorageFreshStateViewer<Dish, DishEvents, string> storage
            let ingredientStateViewer = getAggregateStorageFreshStateViewer<Ingredient, IngredientEvents, string> storage
            let supplierStateViewer = getAggregateStorageFreshStateViewer<Supplier, SupplierEvents, string> storage

            new (storage: IEventStore<string>) =
                PubSystem(storage, doNothingBroker)
            member this.AddDish (dish: Dish) =
                ResultCE.result {
                    let addDishReference = KitchenCommands.AddDishReference dish.Id
                    let! result = runInitAndCommand<Kitchen, KitchenEvents, Dish, string> storage eventBroker dish addDishReference
                    return result
                }
            member this.AddIngredient (ingredientId: Guid, name: string) =
                ResultCE.result {
                    let ingredient = Ingredient.Ingredient(ingredientId, name, [], [])
                    let addIngredientReference = KitchenCommands.AddIngredientReference ingredient.Id
                    let! result = runInitAndCommand<Kitchen, KitchenEvents, Ingredient, string> storage eventBroker ingredient addIngredientReference
                    return result
                }
            member this.AddSupplier (supplier: Supplier) =
                ResultCE.result {
                    let addSupplierReference = KitchenCommands.AddSupplierReference supplier.Id
                    let! result = runInitAndCommand<Kitchen, KitchenEvents, Supplier, string> storage eventBroker supplier addSupplierReference
                    return result
                }
            member this.GetAllSuppliers ()      =
                ResultCE.result {
                    let! (_, kitchen) = kitchenStateViewer ()
                    let suppliersRefs = kitchen.supplierReferences |>> snd
                    return suppliersRefs
                }
                
            member this.GetAllDishReferences () =
                ResultCE.result {
                    let! (_, kitchen) = kitchenStateViewer ()
                    let dishesRefs = kitchen.dishReferences |>> snd
                    return dishesRefs
                }
            member this.GetAllDishes () =
                ResultCE.result {
                    let! dishesRefs = this.GetAllDishReferences ()
                    let! dishes = 
                        dishesRefs 
                        |> List.traverseResultM dishStateViewer
                    return dishes
                }
            member this.GetAllIngredientReferences () =
                ResultCE.result {
                    let! (_, kitchen) = kitchenStateViewer ()
                    let ingredientRefs = kitchen.GetIngredientReferences ()
                    return ingredientRefs
                }
            member this.GetIngredient (guid: Guid) =
                ResultCE.result {
                    let! (_, ingredient) = ingredientStateViewer guid
                    return ingredient
                }
            member this.GetAllIngredients () =
                ResultCE.result {
                    let! ingredientRefs = this.GetAllIngredientReferences ()
                    let! ingredients = 
                        ingredientRefs 
                        |> List.traverseResultM ingredientStateViewer
                    return ingredients
                }

            member this.AddTypeToIngredient ( guid: Guid, ingredientType: IngredientType) =
                ResultCE.result {
                    let! ingredient = this.GetIngredient guid
                    let addIngredientType = IngredientCommands.AddIngredientType ingredientType 
                    let! result = runAggregateCommand<Ingredient, IngredientEvents, string> guid storage eventBroker addIngredientType 
                    return result
                }
            member this.AddMeasureType ( guid: Guid, measureType: MeasureType) =
                ResultCE.result {
                    let! ingredient = this.GetIngredient guid
                    let addMeasureType = IngredientCommands.AddMeasureType measureType
                    let! result = runAggregateCommand<Ingredient, IngredientEvents, string> guid storage eventBroker addMeasureType 
                    return result
                }     
