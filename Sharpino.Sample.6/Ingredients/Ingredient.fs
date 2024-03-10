
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
open Sharpino.Lib.Core.Commons
open System

module Ingredient =
    type MeasureType =
        | Centiliters
        | Grams
        | Liters
        | Milliliters
        | Pieces
        | Tablespoons
        | Teaspoons
        | Units
        | Other of string
     
    type IngredientType =
        | Meat
        | Vegetables
        | Alcohol
        | Fruit
        | Liquid
        | Fish
        | Condiment
        | Poultry
        | Venison
        | Cheese
        | Pasta
        | Rice
        | Other of string
    
    type Ingredient(id: Guid, name: String, ingredientTypes: List<IngredientType>, measureTypes: List<MeasureType>) =
        let stateId = Guid.NewGuid()
        member this.StateId = stateId
        member this.Id = id
        member this.IngredientTypes = ingredientTypes
        member this.MeasureTypes = measureTypes
        member this.Name = name

        member this.AddIngredientType (ingredientType: IngredientType) =
            result {
                let! notAlreadyExists = 
                    this.IngredientTypes
                    |> List.exists (fun x -> x = ingredientType)
                    |> not
                    |> Result.ofBool "Ingredient type already exists"
                let newIngredientTypes = ingredientType :: this.IngredientTypes
                return Ingredient(this.Id, this.Name, newIngredientTypes, this.MeasureTypes)
            }
        member this.RemoveIngredientType (ingredientType: IngredientType) =
            result {
                let! mustExist = 
                    this.IngredientTypes
                    |> List.exists (fun x -> x = ingredientType)
                    |> Result.ofBool "Ingredient type does not exists"
                let newIngredientTypes = this.IngredientTypes |> List.filter (fun x -> x <> ingredientType)
                return Ingredient(this.Id, this.Name, newIngredientTypes, this.MeasureTypes)
            }
        member this.AddMeasureType (measureType: MeasureType) =
            result {
                let! notAlreadyExists = 
                    this.MeasureTypes
                    |> List.exists (fun x -> x = measureType)
                    |> not
                    |> Result.ofBool "Measure type already exists"
                let newMeasureTypes = measureType :: this.MeasureTypes
                return Ingredient(this.Id, this.Name, this.IngredientTypes, newMeasureTypes)
            }
        member this.RemoveMeasureType (measureType: MeasureType) =
            result {
                let! mustExist = 
                    this.MeasureTypes
                    |> List.exists (fun x -> x = measureType)
                    |> Result.ofBool "Measure type does not exists"
                let newMeasureTypes = this.MeasureTypes |> List.filter (fun x -> x <> measureType)
                return Ingredient(this.Id, this.Name, this.IngredientTypes, newMeasureTypes)
            }    
        
        member this.Serialize (serializer: ISerializer) =
            this 
            |> serializer.Serialize
        static member Deserialize (serializer: ISerializer, json: Json): Result<Ingredient, string>  =
            serializer.Deserialize<Ingredient> json

        static member StorageName =
            "_ingredient"
        static member Version =
            "_01"

        static member SnapshotsInterval =  15 

        interface Aggregate with
            member this.StateId = stateId
            member this.Id = this.Id
            member this.Lock: obj = 
                this
            member this.Serialize(serializer: ISerializer): string = 
                this.Serialize serializer
        interface Entity with
            member this.Id = this.Id