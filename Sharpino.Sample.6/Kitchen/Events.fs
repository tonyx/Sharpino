
namespace Tonyx.Sharpino.Pub
open Tonyx.Sharpino.Pub.Commons
open Tonyx.Sharpino.Pub.Kitchen
open FSharpPlus
open FsToolkit.ErrorHandling
open Sharpino.Definitions
open Sharpino.Utils
open Sharpino.Core
open System

module KitchenEvents =
    type KitchenEvents =
        | DishReferenceAdded of Guid
        | DishReferenceRemoved of Guid
        | IngredientReferenceAdded of Guid
        | IngredientReferenceRemoved of Guid
        | SupplierReferenceAdded of Guid
        | SupplierReferenceRemoved of Guid

            interface Event<Kitchen> with
                member this.Process (x: Kitchen) =
                    match this with
                    | DishReferenceAdded id ->
                        x.AddDishReference id
                    | DishReferenceRemoved id ->
                        x.RemoveDishReference id
                    | IngredientReferenceAdded id ->
                        x.AddIngredientReference id
                    | IngredientReferenceRemoved id ->
                        x.RemoveIngredientReference id
                    | SupplierReferenceAdded id ->
                        x.AddSupplierReference id
                    | SupplierReferenceRemoved id ->
                        x.RemoveSupplierReference id

        static member Deserialize x =
            serializer.Deserialize<KitchenEvents> x
        member this.Serialize =
            this
            |> serializer.Serialize
