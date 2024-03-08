namespace Sharpino.Sample.Tags

open System
open Sharpino.Core

open Sharpino.Sample.Entities.Tags
open Sharpino.Sample.Tags.TagsEvents
open Sharpino.Sample.TagsContext
open Sharpino.Sample.Shared.Entities

module TagCommands =
    open FsToolkit.ErrorHandling
    type TagCommand =
        | AddTag of Tag
        | RemoveTag of Guid
        | Ping of unit

        interface Command<TagsContext, TagEvent> with
            member this.Execute (x: TagsContext) =
                match this with
                | AddTag t ->
                    x.AddTag t
                    |> Result.map (fun _ -> [TagAdded t]) 
                | RemoveTag g ->
                    x.RemoveTag g
                    |> Result.map (fun _ -> [TagRemoved g])
                | Ping () ->
                    x.Ping()
                    |> Result.map (fun _ -> [PingDone ()])
            member this.Undoer = 
                match this with
                | AddTag t ->
                    (fun (_: TagsContext) ->
                        fun (x': TagsContext) ->
                            x'.RemoveTag t.Id 
                            |> Result.map (fun _ -> [TagRemoved t.Id])
                        |> Ok
                    )
                    |> Some
                | RemoveTag g -> 
                    (fun (x: TagsContext) ->
                        ResultCE.result {
                            let! tag = x.GetTag g
                            let result =
                                fun (x': TagsContext) ->
                                    x'.AddTag tag 
                                    |> Result.map (fun _ -> [TagAdded tag])
                            return result
                        }
                    )
                    |> Some
                | Ping () ->
                    None
