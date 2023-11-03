namespace Sharpino.Sample.Tags

open Sharpino.Cache
open Sharpino.Core
open Sharpino.Storage
open Sharpino.Utils
open Sharpino.Definitions
open Sharpino.Sample.TagsAggregate
open Sharpino.Sample.Entities.Tags
open type Sharpino.Cache.EventCache<TagsAggregate>

open System

module TagsEvents =
    type TagEvent =
        | TagAdded of Tag
        | TagRemoved of Guid
            interface Event<TagsAggregate> with
                member this.Process (x: TagsAggregate) =
                    match this with
                    | TagAdded (t: Tag) ->
                        Instance.Memoize (fun () -> x.AddTag t) (x, [this])
                    | TagRemoved (g: Guid) ->
                        Instance.Memoize (fun () -> x.RemoveTag g) (x, [this])
        member this.Serialize(serializer: ISerializer) =
            this
            |> serializer.Serialize

        static member Deserialize (serializer: ISerializer, json: Json) =
            serializer.Deserialize<TagEvent> json