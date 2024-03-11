
namespace Sharpino.Sample

open FsToolkit.ErrorHandling
open Sharpino.Utils
open Sharpino.Storage
open Sharpino.Definitions

open Sharpino.Sample.Entities.Categories
open Sharpino.Sample.Shared.Entities
open System

module CategoriesContext =
    type CategoriesContext (categories: Categories) =
        let stateId = Guid.NewGuid()
        member this.StateId = stateId
        member this.Categories = categories
        static member Zero =
            CategoriesContext Categories.Zero
        static member StorageName =
            "_categories"
        static member Version =
            "_02"
        static member SnapshotsInterval =
            15
        static member Lock =
            new Object()
        member this.Ping(): Result<CategoriesContext, string> =
            result
                {
                    return
                        this
                }
        member this.AddCategory (c: Category) =
            result {
                let! categories = this.Categories.AddCategory c 
                return
                        CategoriesContext categories
            }
        member this.RemoveCategory (id: Guid) =
            result {
                let! categories = this.Categories.RemoveCategory id 
                return CategoriesContext categories
            }
        member this.AddCategories (cs: List<Category>) =
            result {
                let! categories = this.Categories.AddCategories cs 
                return CategoriesContext categories
            }
        member this.GetCategories() = this.Categories.GetCategories().GetAll()

        member this.Serialize(serializer: ISerializer) =
            this
            |> serializer.Serialize
        static member Deserialize (serializer: ISerializer, json: Json)=
            serializer.Deserialize<CategoriesContext> json