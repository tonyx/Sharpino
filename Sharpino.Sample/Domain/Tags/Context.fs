namespace Sharpino.Sample

open Sharpino.Sample.Entities.Tags
open Sharpino.Utils
open Sharpino.Definitions
open Sharpino.Repositories

open Sharpino.Sample.Shared.Entities

open System
open FsToolkit.ErrorHandling

module TagsContext =
    type TagsContext (tags: Tags) =
        let stateId = Guid.NewGuid()
        member this.StateId = stateId
        member this.Tags = tags
        static member Zero =
            TagsContext (Tags.Zero)
        member this.Ping(): Result<TagsContext,string> =
            result
                {
                    return
                        this
                }
        member this.AddTag (t: Tag) =
            result {
                let! tags = this.Tags.AddTag t 
                return TagsContext tags
            }

        member this.GetTag (id: Guid) =
            this.Tags.GetTag id 

        member this.RemoveTag (id: Guid) =
            result {
                let! tags = this.Tags.RemoveTag id 
                return TagsContext tags  
            }
        member this.GetTags() = this.Tags.GetTags()

        member this.Serialize(serializer: ISerializer) =
            this
            |> serializer.Serialize

        static member Deserialize (serializer: ISerializer, json: Json) =
            serializer.Deserialize<TagsContext> json
        static member StorageName =
            "_tags"
        static member Version =
            "_01"
        static member SnapshotsInterval =
            15
        static member Lock =
            new Object()
