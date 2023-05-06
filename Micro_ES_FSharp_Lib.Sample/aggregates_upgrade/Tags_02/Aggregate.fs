namespace Tonyx.EventSourcing.Sample_02

open Tonyx.EventSourcing.Sample_02.Tags.Models.TagsModel
open Tonyx.EventSourcing.Sample_02
open System

module TagsAggregate =
    type TagsAggregate =
        {
            Tags: Tags
        }
        static member Zero =
            {
                Tags = Tags.Zero
            }
        // storagename _MUST_ be unique for each aggregate and the relative lock object 
        // must be added in syncobjects map in Conf.fs
        static member StorageName =
            "_tags"
        static member Version =
            "_02"
        member this.AddTag(t: Tag) =
            ceResult {
                let! result = this.Tags.AddTag(t)
                let result =
                    {
                        this with
                            Tags = result
                    }
                return result
            }
        member this.RemoveTag(id: Guid) =
            ceResult {
                let! result = this.Tags.RemoveTag(id)
                let result =
                    {
                        this with
                            Tags = result
                    }
                return result
            }
        member this.GetTags() = this.Tags.GetTags()
