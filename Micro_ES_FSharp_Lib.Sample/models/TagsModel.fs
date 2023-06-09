namespace Sharpino.EventSourcing.Sample.Tags.Models
open Sharpino.EventSourcing.Utils
open FSharpPlus
open System
open FsToolkit.ErrorHandling

module TagsModel =
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
                ResultCE.result {
                    let! mustNotExist =
                        this.tags
                        |> List.exists (fun x -> x.Name = t.Name)
                        |> not
                        |> boolToResult (sprintf "A tag named %s already exists" t.Name)
                    let result =
                        {
                            this with
                                tags = t::this.tags
                        }
                    return result
                }
            member this.RemoveTag (id: Guid) =
                ResultCE.result {
                    let! mustExist =
                        this.tags
                        |> List.exists (fun x -> x.Id = id)
                        |> boolToResult (sprintf "A tag with id '%A' does not exist" id)
                    let result =
                        {
                            this with
                                tags = this.tags |> List.filter (fun x -> x.Id <> id)
                        }
                    return result
                }
            member this.GetTags() = this.tags