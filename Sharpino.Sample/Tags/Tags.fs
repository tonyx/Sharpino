namespace Sharpino.Sample.Entities
open Sharpino.Utils
open Sharpino.Core
open Sharpino.Repositories
open Sharpino.Sample.Shared.Entities
open FSharpPlus
open System
open FsToolkit.ErrorHandling

module Tags =
    type Tags = 
        {
            tags: IRepository<Tag>
        }
        with
            static member Zero =
                {
                    tags = ListRepository<Tag>.Zero 
                }
            member this.AddTag (t: Tag) =
                result {
                    let! added = 
                        this.tags.AddWithPredicate (t, (fun x -> x.Name = t.Name), sprintf "A tag with name '%s' or id '%A' already exists" t.Name t.Id)
                    return {
                        this with
                            tags = added
                    }
                }
            member this.RemoveTag (id: Guid) =
                result {
                    let! removed =
                        sprintf "A tag with id '%A' does not exist" id
                        |> this.tags.Remove id

                    return 
                        {
                            this with
                                tags = removed //this.tags.Remove id
                        }
                }
            member this.GetTag(id: Guid) =
                result {
                    return! 
                        this.tags.Get id 
                        |> Result.ofOption (sprintf "A tag with id '%A' does not exist" id)
                }

            member this.GetTags() = this.tags.GetAll()