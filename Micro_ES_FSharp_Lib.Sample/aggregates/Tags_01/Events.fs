namespace Tonyx.EventSourcing.Sample.Tags

open System
open Tonyx.EventSourcing.Core
open Tonyx.EventSourcing.Sample.TagsAggregate
open Tonyx.EventSourcing.Sample.Tags.Models.TagsModel
open Tonyx.EventSourcing.Cache

module TagsEvents =
    type TagEvent =
        | TagAdded of Tag
        | TagRemoved of Guid
            interface Event<TagsAggregate> with
                member this.Process (x: TagsAggregate) =
                    match this with
                    | TagAdded (t: Tag) ->
                        EventCache<TagsAggregate>.Instance.Memoize (fun () -> x.AddTag t) (x, [TagAdded t])
                    | TagRemoved (g: Guid) ->
                        EventCache<TagsAggregate>.Instance.Memoize (fun () -> x.RemoveTag g) (x, [TagRemoved g])