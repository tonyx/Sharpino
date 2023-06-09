
namespace Sharpino.EventSourcing.Sample.Tags

open System
open Sharpino.EventSourcing.Core
open Sharpino.EventSourcing.Cache

open Sharpino.EventSourcing.Sample.Tags.Models.TagsModel
open Sharpino.EventSourcing.Sample.Tags.TagsEvents
open Sharpino.EventSourcing.Sample.TagsAggregate

module TagCommands =
    type TagCommand =
        | AddTag of Tag
        | RemoveTag of Guid

        interface Command<TagsAggregate, TagEvent> with
            member this.Execute (x: TagsAggregate) =
                match this with
                | AddTag t ->
                    match
                        EventCache<TagsAggregate>.Instance.Memoize (fun () -> x.AddTag t) (x, [TagAdded t]) with
                        | Ok _ -> [TagAdded t] |> Ok
                        | Error x -> x |> Error
                | RemoveTag g ->
                    match
                        EventCache<TagsAggregate>.Instance.Memoize (fun () -> x.RemoveTag g) (x, [TagRemoved g]) with
                        | Ok _ -> [TagRemoved g] |> Ok
                        | Error x -> x |> Error