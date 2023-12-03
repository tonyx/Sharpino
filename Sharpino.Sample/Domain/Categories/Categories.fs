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
            categories: IRepository<Category>
        }
        with
            static member Zero =
                {
                    categories = ListRepository<Category>.Zero
                }
            member this.AddCategory (c: Category) =
                result {
                    let! added = 
                        this.categories.AddWithPredicate (c, (fun x -> x.Name = c.Name), sprintf "A category with name '%s' or id '%A' already exists" c.Name c.Id)
                    return {
                        this with
                            categories = added
                    }
                }

            member this.AddCategories (cs: List<Category>) =
                result {
                    let! added = 
                        this.categories.AddManyWithPredicate 
                            (   
                                cs, 
                                (fun (c: Category) -> sprintf  "a category with id %A or name %s already exists" c.Id c.Name),
                                (fun (x: Category, c: Category) -> x.Name = c.Name)
                            )
                    return 
                        {
                            this with
                                categories = added
                        }
                }

            member this.RemoveCategory (id: Guid) =
                result {
                    let! removed =
                        sprintf "A category with id '%A' does not exist" id
                        |> this.categories.Remove id
                    return
                        {
                            this with
                                categories = removed 
                        }
                }
            member this.GetCategories() = this.categories