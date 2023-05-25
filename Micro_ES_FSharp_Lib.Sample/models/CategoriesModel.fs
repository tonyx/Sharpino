namespace Tonyx.EventSourcing.Sample.Todos.Models
open System
open FSharpPlus
open FsToolkit.ErrorHandling

open Tonyx.EventSourcing.Utils

module CategoriesModel =
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
                ResultCE.result {
                    let! mustNotExist =
                        this.categories
                        |> List.exists (fun x -> x.Name = c.Name  || x.Id = c.Id)
                        |> not
                        |> boolToResult (sprintf "There is already another Category with name = '%s' or id = '%A'" c.Name c.Id)
                    let result =
                        {
                            this with
                                categories = c::this.categories
                        }
                    return result
                }

            member this.AddCategories (cs: List<Category>) =
                let checkNotExists c =
                    this.categories
                    |> List.exists (fun x -> x.Name = c.Name)
                    |> not
                    |> boolToResult (sprintf "There is already another Category named %s " c.Name)

                ResultCE.result {
                    let! mustNotExist =
                        cs |> catchErrors checkNotExists
                    let result =
                        {
                            this with
                                categories = cs @ this.categories
                        }
                    return result
                }

            member this.RemoveCategory (id: Guid) =
                ResultCE.result {
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