namespace Sharpino.Sample.Tags

open Sharpino.Core
open Sharpino.Utils
open Sharpino.Definitions
open Sharpino.Sample.TagsCluster
open Sharpino.Sample.Entities.Tags
open Sharpino.Sample.Shared.Entities

open System

module TagsEvents =
    type TagEvent =
        | TagAdded of Tag
        | TagRemoved of Guid
            interface Event<TagsCluster> with
                member this.Process (x: TagsCluster) =
                    match this with
                    | TagAdded (t: Tag) ->
                        x.AddTag t
                    | TagRemoved (g: Guid) ->
                        x.RemoveTag g
        member this.Serialize(serializer: ISerializer) =
            this
            |> serializer.Serialize

        static member Deserialize (serializer: ISerializer, json: Json) =
            serializer.Deserialize<TagEvent> json