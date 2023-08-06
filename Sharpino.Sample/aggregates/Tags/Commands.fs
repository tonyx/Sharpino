namespace Sharpino.Sample.Tags

open System
open Sharpino.Core
open Sharpino.Cache

open Sharpino.Sample.Models.TagsModel
open Sharpino.Sample.Tags.TagsEvents
open Sharpino.Sample.TagsAggregate

module TagCommands =
    open FsToolkit.ErrorHandling
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
            member this.Undoer = 
                match this with
                | AddTag t ->
                    (fun (_: TagsAggregate) ->
                        fun (x': TagsAggregate) ->
                            x'.RemoveTag t.Id 
                            |> Result.map (fun _ -> [TagAdded t])
                        |> Ok
                    )
                    |> Some
                | RemoveTag g -> 
                    (fun (x: TagsAggregate) ->
                        ResultCE.result {
                            let! tag = x.GetTag g
                            let result =
                                fun (x': TagsAggregate) ->
                                    x'.AddTag tag 
                                    |> Result.map (fun _ -> [TagAdded tag])
                            return result
                        }
                    )
                    |> Some
