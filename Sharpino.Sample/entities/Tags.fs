namespace Sharpino.Sample.Entities
open Sharpino.Utils
open Sharpino.Core
open FSharpPlus
open System
open FsToolkit.ErrorHandling

module Tags =
    type Color =
        | Red
        | Green
        | Blue

    type Tag =
        {
            Id: Guid
            Name: string
            Color: Color
        }
        interface Entity with
            member this.Id = this.Id
    type Tags = 
        {
            tags: List<Tag>
        }
        with
            static member Zero =
                {
                    tags = []
                }
            member this.AddTag (t: Tag) =
                result {
                    let! mustNotExist =
                        this.tags
                        |> List.exists (fun x -> x.Name = t.Name)
                        |> not
                        |> boolToResult (sprintf "A tag named %s already exists" t.Name)
                    return
                        {
                            this with
                                tags = t::this.tags
                        }
                }
            member this.RemoveTag (id: Guid) =
                result {
                    let! mustExist =
                        this.tags
                        |> List.exists (fun x -> x.Id = id)
                        |> boolToResult (sprintf "A tag with id '%A' does not exist" id)
                    return
                        {
                            this with
                                tags = this.tags |> List.filter (fun x -> x.Id <> id)
                        }
                }
            member this.GetTag(id: Guid) =
                result {
                    return! 
                        this.tags
                        |> List.tryFind (fun x -> x.Id = id)
                        |> Result.ofOption (sprintf "A tag with id '%A' does not exist" id)
                }

            member this.GetTags() = this.tags