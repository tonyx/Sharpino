namespace Tonyx.Sharpino.Pub
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
                    let initSnapshot = dish.Serialize serializer
                    let! stored =
                        storage.SetInitialAggregateState dish.Id dish.StateId Dish.Dish.Version Dish.Dish.StorageName initSnapshot
                    let addDishReference = KitchenCommands.AddDishReference dish.Id
                    let! result = runCommand<Kitchen, KitchenEvents> storage eventBroker kitchenStateViewer addDishReference
                    return result
                }
            member this.AddIngredient (ingredientId: Guid, name: string) =
                ResultCE.result {
                    let ingredient = Ingredient.Ingredient(ingredientId, name, [], [])
                    let initSnapshot = ingredient.Serialize serializer
                    let! stored =
                        storage.SetInitialAggregateState ingredientId ingredient.StateId Ingredient.Ingredient.Version Ingredient.Ingredient.StorageName initSnapshot
                    let addIngredientReference = KitchenCommands.AddIngredientReference ingredientId
                    let! result = runCommand<Kitchen, KitchenEvents> storage eventBroker kitchenStateViewer addIngredientReference
                    return result
                }
           
            member this.AddSupplier (supplier: Supplier) =
                ResultCE.result {
                    let initSnapshot = supplier.Serialize serializer
                    let! stored =
                        storage.SetInitialAggregateState supplier.Id supplier.StateId Supplier.Supplier.Version Supplier.Supplier.StorageName initSnapshot
                    let addSupplierReference = KitchenCommands.AddSupplierReference supplier.Id
                    let! result = runCommand<Kitchen, KitchenEvents> storage eventBroker kitchenStateViewer addSupplierReference
                    return result
                }
            member this.GetAllSuppliers ()      =
                ResultCE.result {
                    let! (_, kitchen , _, _) = kitchenStateViewer ()
                    let suppliersRefs = kitchen.suppliersReferences |>> snd
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
                        |> List.traverseResultM (fun x -> dishStateViewer x)
                    return dishes
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
                        |> List.traverseResultM (fun x -> ingredientStateViewer x)
                    return ingredients
                }

            member this.AddTypeToIngredient ( guid: Guid, ingredientType: IngredientType) =
                ResultCE.result {
                    let! ingredient = this.GetIngredient guid
                    let addIngredient = IngredientCommands.AddIngredientType ingredientType
                    let! result = runAggregateCommand<Ingredient, IngredientEvents> guid storage eventBroker (fun _ -> ingredientStateViewer guid) addIngredient 
                    return result
                }
            member this.AddMeasureType ( guid: Guid, measureType: MeasureType) =
                ResultCE.result {
                    let! ingredient = this.GetIngredient guid
                    let addIngredient = IngredientCommands.AddMeasureType measureType
                    let! result = runAggregateCommand<Ingredient, IngredientEvents> guid storage eventBroker (fun _ -> ingredientStateViewer guid) addIngredient 
                    return result
                }     
