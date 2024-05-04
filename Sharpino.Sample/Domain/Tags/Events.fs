namespace Sharpino.Sample.Tags

open Sharpino.Core
open Sharpino.Utils
open Sharpino.Definitions
open Sharpino.Sample.TagsContext
open Sharpino.Sample.Entities.Tags
open Sharpino.Sample.Shared.Entities
open Sharpino.Sample.Commons

open System

module TagsEvents =
    type TagEvent =
        | TagAdded of Tag
        | TagRemoved of Guid
        | PingDone of unit
            interface Event<TagsContext> with
                member this.Process (x: TagsContext) =
                    match this with
                    | TagAdded (t: Tag) ->
                        x.AddTag t
                    | TagRemoved (g: Guid) ->
                        x.RemoveTag g
                    | PingDone () ->
                        x.Ping()
        member this.Serialize =
            this
            |> serializer.Serialize

        static member Deserialize json = 
            serializer.Deserialize<TagEvent> json