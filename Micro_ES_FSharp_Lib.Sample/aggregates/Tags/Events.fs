namespace Sharpino.Sample.Tags

open Sharpino.Sample
open Sharpino.Cache
open System
open Sharpino.Core
open Sharpino.Sample.TagsAggregate
open Sharpino.Sample.Models.TagsModel

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