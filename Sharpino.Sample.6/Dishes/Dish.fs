
namespace Tonyx.Sharpino.Pub
open Sharpino.CommandHandler
open Tonyx.Sharpino.Pub.Commons
open Sharpino.Definitions
open System
open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Sharpino
open Sharpino.Storage
open Sharpino.Core
open Sharpino.Utils
open System

open Sharpino.Lib.Core.Commons
type Dish(id: Guid, name: String, ingredients: List<Guid>) =
    let stateId = Guid.NewGuid()
    let ingredients = ingredients

    member this.StateId = stateId
    member this.Id = id
    member this.Name = name
    member this.AddIngredient (id: Guid) =
        result {
            let! notAlreadyExists =
                this.Ingredients
                |> List.contains id
                |> not
                |> Result.ofBool (sprintf "An ingredient with id '%A' already exists" id)
            let newIgredients = id :: this.Ingredients
            return Dish(this.Id, this.Name, newIgredients)
        }

    member this.RemoveIngredient (id: Guid) =
        result {
            let! chckExists =
                this.Ingredients
                |> List.contains id
                |> Result.ofBool (sprintf "An ingredient with id '%A' does not exist" id)
            let newIgredients = this.Ingredients |> List.filter (fun x -> x <> id)
            return Dish(this.Id, this.Name, newIgredients)
        }

    member this.Ingredients = ingredients   

    static member StorageName =
        "_dish"
    static member Version =
        "_01"
    static member SnapshotsInterval =  15
    member this.Serialize =
        this 
        |> serializer.Serialize
    static member Deserialize json =
        serializer.Deserialize<Dish> json

    interface Aggregate<string> with
        member this.Id = this.Id
        member this.Serialize =
            this.Serialize 
    interface Entity with
        member this.Id = this.Id





