namespace Sharpino.EventSourcing.Sample.Tags

open System
open Sharpino.EventSourcing.Core
open Sharpino.EventSourcing.Sample.TagsAggregate
open Sharpino.EventSourcing.Sample.Tags.Models.TagsModel
open Sharpino.EventSourcing.Cache

module TagsEvents =
    type TagEvent =
        | TagAdded of Tag
        | TagRemoved of Guid
            interface Event<TagsAggregate> with
                member this.Process (x: TagsAggregate) =
                    match this with
                    | TagAdded (t: Tag) ->
                        EventCache<TagsAggregate>.Instance.Memoize (fun () -> x.AddTag t) (x, [this])
                    | TagRemoved (g: Guid) ->
                        EventCache<TagsAggregate>.Instance.Memoize (fun () -> x.RemoveTag g) (x, [this])