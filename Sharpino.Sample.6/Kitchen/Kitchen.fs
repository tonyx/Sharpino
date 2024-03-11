namespace Tonyx.Sharpino.Pub
open FSharpPlus
open FsToolkit.ErrorHandling
open Sharpino.Definitions
open Sharpino.Utils
open Sharpino
open System

module Kitchen =
    type Kitchen (dishReferences: List<DateTime * Guid>, ingredientReferences: List<DateTime * Guid>, supplierReferences: List<DateTime * Guid>)=
        member this.dishReferences = dishReferences
        member this.ingredientReferences = ingredientReferences
        member this.supplierReferences = supplierReferences

        static member Zero =
            Kitchen ([], [], [])
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
                    |> Result.ofBool (sprintf "A dish with id '%A' already exists" id)    
                return 
                    Kitchen ((System.DateTime.Now, id) :: this.dishReferences, this.ingredientReferences, this.supplierReferences)
            }
        member this.AddIngredientReference (id: Guid) =
            result {
                let! notAlreadyExists =
                    this.ingredientReferences
                    |>> snd
                    |> List.contains id
                    |> not
                    |> Result.ofBool (sprintf "An ingredient with id '%A' already exists" id)
                return Kitchen (this.dishReferences, ((System.DateTime.Now, id) :: this.ingredientReferences), this.supplierReferences)
            }
        member this.RemoveIngredientReference (id: Guid) =
            result {
                let! chckExists =
                    this.ingredientReferences
                    |>> snd
                    |> List.contains id
                    |> Result.ofBool (sprintf "An ingredient with id '%A' does not exist" id)
                return Kitchen (this.dishReferences, this.ingredientReferences |> List.filter (fun (_, x) -> x <> id), this.supplierReferences)
            }

        member this.RemoveDishReference (id: Guid) =
            result {
                let! chckExists =
                    this.dishReferences
                    |>> snd
                    |> List.contains id
                    |> Result.ofBool (sprintf "A dish with id '%A' does not exist" id)
                return Kitchen (this.dishReferences |> List.filter (fun (_, x) -> x <> id), this.ingredientReferences, this.supplierReferences)
            }
            
        member this.AddSupplierReference (id: Guid) =
            result {
                let! notAlreadyExists =
                    this.supplierReferences
                    |>> snd
                    |> List.contains id
                    |> not
                    |> Result.ofBool (sprintf "A supplier with id '%A' already exists" id)
                return Kitchen (this.dishReferences, this.ingredientReferences, ((System.DateTime.Now, id) :: this.supplierReferences))
            }
        member this.RemoveSupplierReference (id: Guid) =
            result {
                let! chckExists =
                    this.supplierReferences
                    |>> snd
                    |> List.contains id
                    |> Result.ofBool (sprintf "A supplier with id '%A' does not exist" id)
                return 
                    Kitchen (this.dishReferences, this.ingredientReferences, this.supplierReferences |> List.filter (fun (_, x) -> x <> id))
            }    
        member this.GetDishReferences () =
            this.dishReferences |>> snd

        member this.GetIngredientReferences () =
            this.ingredientReferences |>> snd


