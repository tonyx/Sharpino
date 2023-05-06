namespace Tonyx.EventSourcing.Sample_02.Tags.Models
open Tonyx.EventSourcing.Utils
open FSharpPlus
open System

module TagsModel =
    type Color =
        | Red
        | Green
        | Blue

    let ceResult = CeResultBuilder()
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
                ceResult {
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
                ceResult {
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