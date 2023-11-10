namespace Sharpino.Sample.Tags

open System
open Sharpino.Core

open Sharpino.Sample.Entities.Tags
open Sharpino.Sample.Tags.TagsEvents
open Sharpino.Sample.TagsCluster

module TagCommands =
    open FsToolkit.ErrorHandling
    type TagCommand =
        | AddTag of Tag
        | RemoveTag of Guid

        interface Command<TagsCluster, TagEvent> with
            member this.Execute (x: TagsCluster) =
                match this with
                | AddTag t ->
                    x.AddTag t
                    |> Result.map (fun _ -> [TagAdded t]) 
                | RemoveTag g ->
                    x.RemoveTag g
                    |> Result.map (fun _ -> [TagRemoved g])
            member this.Undoer = 
                match this with
                | AddTag t ->
                    (fun (_: TagsCluster) ->
                        fun (x': TagsCluster) ->
                            x'.RemoveTag t.Id 
                            |> Result.map (fun _ -> [TagAdded t])
                        |> Ok
                    )
                    |> Some
                | RemoveTag g -> 
                    (fun (x: TagsCluster) ->
                        ResultCE.result {
                            let! tag = x.GetTag g
                            let result =
                                fun (x': TagsCluster) ->
                                    x'.AddTag tag 
                                    |> Result.map (fun _ -> [TagAdded tag])
                            return result
                        }
                    )
                    |> Some
