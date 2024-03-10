namespace Tonyx.Sharpino.Pub
open FSharpPlus
open FsToolkit.ErrorHandling
open Sharpino.Definitions
open Sharpino.Utils
open System

module Kitchen =
    type Kitchen =
        {
            dishReferences: List<DateTime * Guid>
            ingredientReferences: List<DateTime * Guid>
            suppliersReferences: List<DateTime * Guid>
        }
        static member Zero =
            {
                dishReferences = []
                ingredientReferences = []
                suppliersReferences = [] 
            }
        static member StorageName =
            "_kitchen"
        static member Version =
            "_01"
        static member SnapshotsInterval =
            15
        static member Lock =
            new Object()
        static member Deserialize (serializer: ISerializer, json: Json): Result<Kitchen, string>  =
            serializer.Deserialize<Kitchen> json

        member this.Serialize (serializer: ISerializer) =
            this
            |> serializer.Serialize

        member this.AddDishReference (id: Guid) =
            result {
                let! notAlreadyExists =
                    this.dishReferences
                    |>> snd
                    |> List.contains id
                    |> not
                    |> boolToResult (sprintf "A dish with id '%A' already exists" id)
                return {
                    this with
                        dishReferences = ((System.DateTime.Now), id) :: this.dishReferences
                }
            }
        member this.AddIngredientReference (id: Guid) =
            result {
                let! notAlreadyExists =
                    this.ingredientReferences
                    |>> snd
                    |> List.contains id
                    |> not
                    |> boolToResult (sprintf "An ingredient with id '%A' already exists" id)
                return {
                    this with
                        ingredientReferences = ((System.DateTime.Now), id) :: this.ingredientReferences
                }
            }
        member this.RemoveIngredientReference (id: Guid) =
            result {
                let! chckExists =
                    this.ingredientReferences
                    |>> snd
                    |> List.contains id
                    |> boolToResult (sprintf "An ingredient with id '%A' does not exist" id)
                return {
                    this with
                        ingredientReferences = this.ingredientReferences |> List.filter (fun (_, x) -> x <> id)
                }
            }

        member this.RemoveDishReference (id: Guid) =
            result {
                let! chckExists =
                    this.dishReferences
                    |>> snd
                    |> List.contains id
                    |> boolToResult (sprintf "A dish with id '%A' does not exist" id)
                return {
                    this with
                        dishReferences = this.dishReferences |> List.filter (fun (_, x) -> x <> id)
                }
            }
            
        member this.AddSupplierReference (id: Guid) =
            result {
                let! notAlreadyExists =
                    this.suppliersReferences
                    |>> snd
                    |> List.contains id
                    |> not
                    |> boolToResult (sprintf "A supplier with id '%A' already exists" id)
                return {
                    this with
                        suppliersReferences = ((System.DateTime.Now), id) :: this.suppliersReferences
                }
            }
        member this.RemoveSupplierReference (id: Guid) =
            result {
                let! chckExists =
                    this.suppliersReferences
                    |>> snd
                    |> List.contains id
                    |> boolToResult (sprintf "A supplier with id '%A' does not exist" id)
                return {
                    this with
                        suppliersReferences = this.suppliersReferences |> List.filter (fun (_, x) -> x <> id)
                }
            }    
        member this.GetDishReferences () =
            this.dishReferences |>> snd

        member this.GetIngredientReferences () =
            this.ingredientReferences |>> snd


