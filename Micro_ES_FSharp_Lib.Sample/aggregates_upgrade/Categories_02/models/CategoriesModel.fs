namespace Tonyx.EventSourcing.Sample_02.Categories.Models
open System
open FSharpPlus

open Tonyx.EventSourcing.Utils

module CategoriesModel =
    let ceResult = CeResultBuilder()
    type Category =
        {
            Id: Guid
            Name: string
        }
    type Categories = 
        {
            categories: List<Category>
        }
        with
            static member Zero =
                {
                    categories = []
                }
            member this.AddCategory (c: Category) =
                ceResult {
                    let! mustNotExist =
                        this.categories
                        |> List.exists (fun x -> x.Name = c.Name)
                        |> not
                        |> boolToResult (sprintf "There is already another Category named %s " c.Name)
                    let result =
                        {
                            this with
                                categories = c::this.categories
                        }
                    return result
                }
            member this.RemoveCategory (id: Guid) =
                ceResult {
                    let! mustExist =
                        this.categories
                        |> List.exists (fun x -> x.Id = id)
                        |> boolToResult (sprintf "A category with id '%A' does not exist" id)
                    let result =
                        {
                            this with
                                categories = this.categories |> List.filter (fun x -> x.Id <> id)
                        }
                    return result
                }
            member this.GetCategories() = this.categories