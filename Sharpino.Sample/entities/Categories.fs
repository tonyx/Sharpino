namespace Sharpino.Sample.Entities
open System
open FSharpPlus
open FsToolkit.ErrorHandling

open Sharpino.Utils

module Categories =
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
                result {
                    let! mustNotExist =
                        this.categories
                        |> List.exists (fun x -> x.Name = c.Name  || x.Id = c.Id)
                        |> not
                        |> boolToResult (sprintf "There is already another Category with name = '%s' or id = '%A'" c.Name c.Id)
                    return
                        {
                            this with
                                categories = c::this.categories
                        }
                }

            member this.AddCategories (cs: List<Category>) =
                let checkNotExists c =
                    this.categories
                    |> List.exists (fun x -> x.Name = c.Name)
                    |> not
                    |> boolToResult (sprintf "There is already another Category named %s " c.Name)

                result {
                    let! mustNotExist =
                        cs |> catchErrors checkNotExists
                    return
                        {
                            this with
                                categories = cs @ this.categories
                        }
                }

            member this.RemoveCategory (id: Guid) =
                result {
                    let! mustExist =
                        this.categories
                        |> List.exists (fun x -> x.Id = id)
                        |> boolToResult (sprintf "A category with id '%A' does not exist" id)
                    return
                        {
                            this with
                                categories = this.categories |> List.filter (fun x -> x.Id <> id)
                        }
                }
            member this.GetCategories() = this.categories