
namespace Tonyx.Sharpino.Pub

open Sharpino.CommandHandler
open Sharpino.Definitions
open System
open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Sharpino
open Sharpino.Storage
open Sharpino.Core
open Sharpino.Utils
open System
open Tonyx.Sharpino.Pub.Ingredient

module Dish =
    open Sharpino.Lib.Core.Commons
    type Dish(id: Guid, name: String, ingredients: List<Guid>, ingredientReceiptItems: List<IngredientReceiptItem>) =
        let stateId = Guid.NewGuid()
        let ingredients = ingredients
        let ingredientReceiptItems = ingredientReceiptItems
        
        member this.StateId = stateId
        member this.Id = id
        member this.Name = name
        member this.IngredientReceiptItems = ingredientReceiptItems

        member this.AddIngredient (id: Guid) =
            result {
                let! notAlreadyExists =
                    this.Ingredients
                    |> List.contains id
                    |> not
                    |> Result.ofBool (sprintf "An ingredient with id '%A' already exists" id)
                let newIgredients = id :: this.Ingredients
                return Dish(this.Id, this.Name, newIgredients, ingredientReceiptItems)
            }
        member this.AddIngredientReceiptItem (ingredientReceiptItem: IngredientReceiptItem) =
            result {
                let! noAlreadyExists =
                    this.IngredientReceiptItems
                    |> List.exists (fun x -> x.IngredientId = ingredientReceiptItem.IngredientId)
                    |> not
                    |> Result.ofBool (sprintf "An ingredient receipt item with id '%A' already exists" ingredientReceiptItem.IngredientId)
                let newIgredients = ingredientReceiptItem :: this.IngredientReceiptItems
                return Dish(this.Id, this.Name, ingredients, newIgredients)
            }
        
        member this.RemoveIngredientReceiptItem (id: Guid) =
            result {
                let! chckExists =
                    this.IngredientReceiptItems
                    |> List.exists (fun x -> x.IngredientId = id)
                    |> Result.ofBool (sprintf "An ingredient receipt item with id '%A' does not exist" id)
                let newIgredients = this.IngredientReceiptItems |> List.filter (fun x -> x.IngredientId <> id)
                return Dish(this.Id, this.Name, ingredients, newIgredients)
            }
        member this.UpdateIngredientReceiptItem (ingredientReceiptItem: IngredientReceiptItem) =
            result {
                let! chckExists =
                    this.IngredientReceiptItems
                    |> List.exists (fun x -> x.IngredientId = ingredientReceiptItem.IngredientId)
                    |> Result.ofBool (sprintf "An ingredient receipt item with id '%A' does not exist" ingredientReceiptItem.IngredientId)
                let newIgredients = this.IngredientReceiptItems |> List.map (fun x -> if x.IngredientId = ingredientReceiptItem.IngredientId then ingredientReceiptItem else x)
                return Dish(this.Id, this.Name, ingredients, newIgredients)
            }     
            
        member this.RemoveIngredient (id: Guid) =
            result {
                let! chckExists =
                    this.Ingredients
                    |> List.contains id
                    |> Result.ofBool (sprintf "An ingredient with id '%A' does not exist" id)
                let newIgredients = this.Ingredients |> List.filter (fun x -> x <> id)
                return Dish(this.Id, this.Name, newIgredients, ingredientReceiptItems)
            }


        member this.Ingredients = ingredients   

        static member StorageName =
            "_dish"
        static member Version =
            "_01"
        static member SnapshotsInterval =  15
        member this.Serialize (serializer: ISerializer) =
            this 
            |> serializer.Serialize
        static member Deserialize (serializer: ISerializer, json: Json): Result<Dish, string>  =
            serializer.Deserialize<Dish> json

        interface Aggregate with
            member this.StateId = stateId
            member this.Id = this.Id
            member this.Lock: obj = 
                this
            member this.Serialize(serializer: ISerializer): string = 
                this.Serialize serializer
        interface Entity with
            member this.Id = this.Id





