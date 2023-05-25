
namespace Tonyx.EventSourcing.Sample

open FsToolkit.ErrorHandling

open Tonyx.EventSourcing.Sample.Todos.Models.CategoriesModel
open System

module CategoriesAggregate =
    type CategoriesAggregate =
        {
            Categories: Categories
        }
        static member Zero =
            {
                Categories = Categories.Zero
            }
        // storagename _MUST_ be unique for each aggregate and the relative lock object 
        // must be added in syncobjects map in Conf.fs
        static member StorageName =
            "_categories"
        static member Version =
            "_02"
        member this.AddCategory(c: Category) =
            ResultCE.result {
                let! result = this.Categories.AddCategory(c)
                let result =
                    {
                        this with
                            Categories = result
                    }
                return result
            }
        member this.RemoveCategory(id: Guid) =
            ResultCE.result {
                let! result = this.Categories.RemoveCategory(id)
                let result =
                    {
                        this with
                            Categories = result
                    }
                return result
            }
        member this.AddCategories(cs: List<Category>) =
            ResultCE.result {
                let! result = this.Categories.AddCategories(cs)
                let result =
                    {
                        this with
                            Categories = result
                    }
                return result
            }
        member this.GetCategories() = this.Categories.GetCategories()