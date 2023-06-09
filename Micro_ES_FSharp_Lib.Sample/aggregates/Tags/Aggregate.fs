namespace Sharpino.Sample

open Sharpino.Sample.Models.TagsModel
open Sharpino.Sample
open System
open FsToolkit.ErrorHandling

module TagsAggregate =
    type LockObject private() =
        let lockObject = new obj()
        static let instance = LockObject()
        static member Instance = instance
        member this.LokObject =
            lockObject

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
            "_01"
        static member LockObj =
            LockObject.Instance.LokObject
        static member SnapshotsInterval =
            15
        member this.AddTag(t: Tag) =
            ResultCE.result {
                let! result = this.Tags.AddTag(t)
                let result =
                    {
                        this with
                            Tags = result
                    }
                return result
            }

        member this.RemoveTag(id: Guid) =
            ResultCE.result {
                let! result = this.Tags.RemoveTag(id)
                let result =
                    {
                        this with
                            Tags = result
                    }
                return result
            }
        member this.GetTags() = this.Tags.GetTags()
