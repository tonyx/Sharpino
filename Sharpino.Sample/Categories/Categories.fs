namespace Sharpino.Sample.Entities
open System
open FSharpPlus
open FsToolkit.ErrorHandling

open Sharpino.Core
open Sharpino.Utils
open Sharpino.Repositories

open Sharpino.Sample.Shared.Entities

module Categories =
    type Categories = 
        {
            categories: Repository<Category>
        }
        with
            static member Zero =
                {
                    categories = Repository<Category>.Zero
                }
            member this.AddCategory (c: Category) =
                result {
                    let! mustNotExist =
                        this.categories.Exists (fun x -> x.Name = c.Name || x.Id = c.Id)
                        |> not
                        |> boolToResult (sprintf "There is already another Category with name = '%s' or id = '%A'" c.Name c.Id)

                    return
                        {
                            this with
                                categories = this.categories.Add c
                        }
                }

            member this.AddCategories (cs: List<Category>) =
                let checkNotExists (c: Category) =
                    this.categories.Exists (fun x -> x.Name = c.Name)
                    |> not
                    |> boolToResult (sprintf "There is already another Category named %s " c.Name)

                result {
                    let! mustNotExist =
                        cs |> catchErrors checkNotExists
                    return
                        {
                            this with
                                categories = this.categories.AddMany cs
                        }
                }

            member this.RemoveCategory (id: Guid) =
                result {
                    let! newCategories = this.categories.Remove id
                    return
                        {
                            this with
                                categories = newCategories
                        }
                }
            member this.GetCategories() = this.categories