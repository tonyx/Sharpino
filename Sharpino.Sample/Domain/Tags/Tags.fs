namespace Sharpino.Sample.Entities
open Sharpino.Utils
open Sharpino.Core
open Sharpino.Repositories
open Sharpino.Sample.Shared.Entities
open FSharpPlus
open System
open FsToolkit.ErrorHandling

module Tags =
    type Tags (tags: IRepository<Tag>) = 
        member this.Tags = tags
        with
            static member Zero =
                Tags (tags = ListRepository<Tag>.Zero)
            member this.AddTag (t: Tag) =
                result {
                    let! added = 
                        this.Tags.AddWithPredicate (t, (fun x -> x.Name = t.Name), sprintf "A tag with name '%s' or id '%A' already exists" t.Name t.Id)
                    return 
                        Tags (tags = added)
                }
            member this.RemoveTag (id: Guid) =
                result {
                    let! removed =
                        sprintf "A tag with id '%A' does not exist" id
                        |> this.Tags.Remove id

                    return 
                        Tags (tags = removed)
                }
            member this.GetTag(id: Guid) =
                result {
                    return! 
                        this.Tags.Get id 
                        |> Result.ofOption (sprintf "A tag with id '%A' does not exist" id)
                }

            member this.GetTags() = this.Tags.GetAll()