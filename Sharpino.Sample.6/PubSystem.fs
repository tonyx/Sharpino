namespace Tonyx.Sharpino.Pub
open Tonyx.Sharpino.Pub.Ingredient
open Tonyx.Sharpino.Pub.Kitchen
open Tonyx.Sharpino.Pub.KitchenEvents
open Tonyx.Sharpino.Pub.Supplier

open Tonyx.Sharpino.Pub.KitchenCommands
open Tonyx.Sharpino.Pub.Dish
open Tonyx.Sharpino.Pub.SupplierEvents
open Sharpino.CommandHandler
open Sharpino.Definitions
open System
open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Sharpino.Storage
open Sharpino.Core
open Sharpino.Utils
open System

module PubSystem =
    open DishEvents
    open Ingredient
    open IngredientEvents

    let doNothingBroker: IEventBroker =
        {
            notify = None
            notifyAggregate = None
        }
    let connection =
        "Server=127.0.0.1;"+
        "Database=es_pub_sharpino;" +
        "User Id=safe;"+
        "Password=safe;"

    type PubSystem (storage: IEventStore, eventBroker: IEventBroker) =
            let kitchenStateViewer = getStorageFreshStateViewer<Kitchen, KitchenEvents> storage
            let dishStateViewer = getAggregateStorageFreshStateViewer<Dish, DishEvents> storage
            let ingredientStateViewer = getAggregateStorageFreshStateViewer<Ingredient, IngredientEvents> storage
            let supplierStateViewer = getAggregateStorageFreshStateViewer<Supplier, SupplierEvents> storage

            new (storage: IEventStore) =
                PubSystem(storage, doNothingBroker)
            member this.SetAggregateStateControlInOptimisticLock version name =
                ResultCE.result {
                    return! storage.SetClassicOptimisticLock version name
                }
            member this.UnSetAggregateStateControlInOptimisticLock version name =
                ResultCE.result {
                    return! storage.UnSetClassicOptimisticLock version name
                }
            member this.AddDish (dish: Dish) =
                ResultCE.result {
                    let addDishReference = KitchenCommands.AddDishReference dish.Id
                    let! result = runInitAndCommand<Kitchen, KitchenEvents, Dish> storage eventBroker kitchenStateViewer dish addDishReference
                    return result
                }
            member this.AddIngredient (ingredientId: Guid, name: string) =
                ResultCE.result {
                    let ingredient = Ingredient.Ingredient(ingredientId, name, [], [])
                    let addIngredientReference = KitchenCommands.AddIngredientReference ingredient.Id
                    let! result = runInitAndCommand<Kitchen, KitchenEvents, Ingredient> storage eventBroker kitchenStateViewer ingredient addIngredientReference
                    return result
                }
            member this.AddSupplier (supplier: Supplier) =
                ResultCE.result {
                    let addSupplierReference = KitchenCommands.AddSupplierReference supplier.Id
                    let! result = runInitAndCommand<Kitchen, KitchenEvents, Supplier> storage eventBroker kitchenStateViewer supplier addSupplierReference
                    return result
                }
            member this.AddIngredientReceiptItem (dishId: Guid, ingredientReceiptItem: IngredientReceiptItem) =
                ResultCE.result {
                    let ingredientId = ingredientReceiptItem.IngredientId
                    let! (_, ingredient, _, _) = ingredientStateViewer ingredientId
                    let ingredientMeasureTypes = ingredient.MeasureTypes
                    let receiptItemMeasureType = ingredientReceiptItem.Quantity
                    
                    let measureTypeMatches = 
                        match receiptItemMeasureType with
                        | None ->
                            () |> Result.Ok
                        | Some measureTypes when ingredientMeasureTypes |> List.contains measureTypes.MeasureType ->
                            () |> Result.Ok
                        | _ ->
                            Result.Error "receipt assumes a measure type that is not in the ingredient measure types"
                    let! measureTypeMatches = measureTypeMatches         
                    let addIngredientReceiptItem = DishCommands.AddIngredientReceiptItem ingredientReceiptItem
                    let! result = runAggregateCommand<Dish, DishEvents> dishId storage eventBroker dishStateViewer addIngredientReceiptItem
                    return result
                }    
            member this.GetAllSuppliers ()      =
                ResultCE.result {
                    let! (_, kitchen , _, _) = kitchenStateViewer ()
                    let suppliersRefs = kitchen.supplierReferences |>> snd
                    return suppliersRefs
                }
                
            member this.GetAllDishReferences () =
                ResultCE.result {
                    let! (_, kitchen , _, _) = kitchenStateViewer ()
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
            member this.GetDish (guid: Guid) =
                ResultCE.result {
                    let! (_, dish, _, _) = dishStateViewer guid
                    return dish
                }     
            member this.GetAllIngredientReferences () =
                ResultCE.result {
                    let! (_, kitchen , _, _) = kitchenStateViewer ()
                    let ingredientRefs = kitchen.GetIngredientReferences ()
                    return ingredientRefs
                }
            member this.GetIngredient (guid: Guid) =
                ResultCE.result {
                    let! (_, ingredient, _, _) = ingredientStateViewer guid
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
                    let! result = runAggregateCommand<Ingredient, IngredientEvents> guid storage eventBroker ingredientStateViewer addIngredientType 
                    return result
                }
            member this.AddMeasureType ( guid: Guid, measureType: MeasureType) =
                ResultCE.result {
                    let! ingredient = this.GetIngredient guid
                    let addMeasureType = IngredientCommands.AddMeasureType measureType
                    let! result = runAggregateCommand<Ingredient, IngredientEvents> guid storage eventBroker ingredientStateViewer addMeasureType 
                    return result
                }     
