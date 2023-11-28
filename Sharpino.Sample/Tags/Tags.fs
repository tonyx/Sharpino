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
            tags: Repository<Tag>
        }
        with
            static member Zero =
                {
                    tags = Repository<Tag>.Zero 
                }
            member this.AddTag (t: Tag) =
                result {
                    let! mustNotExist =
                        this.tags.Exists (fun x -> x.Name = t.Name)
                        |> not
                        |> boolToResult (sprintf "A tag named %s already exists" t.Name)
                    return
                        {
                            this with
                                tags = this.tags.Add t
                        }
                }
            member this.RemoveTag (id: Guid) =
                result {
                    let! mustExist =
                        this.tags.Exists (fun x -> x.Id = id)
                        |> boolToResult (sprintf "A tag with id '%A' does not exist" id)
                    return
                        {
                            this with
                                tags = this.tags.Remove (fun x -> x.Id = id)
                        }
                }
            member this.GetTag(id: Guid) =
                result {
                    return! 
                        this.tags.Get (fun x -> x.Id = id)
                        |> Result.ofOption (sprintf "A tag with id '%A' does not exist" id)
                }

            member this.GetTags() = this.tags.GetAll()