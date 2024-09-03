namespace Sharpino.Sample.Tags

open System
open Sharpino
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
                    |> Result.map (fun s -> (s, [TagAdded t]))
                | RemoveTag g ->
                    x.RemoveTag g
                    |> Result.map (fun s -> (s, [TagRemoved g]))
                | Ping () ->
                    x.Ping()
                    |> Result.map (fun s -> (s, [PingDone ()]))
            member this.Undoer =
                match this with
                | AddTag t ->
                    Some
                        (fun (tagsContext: TagsContext) (viewer: StateViewer<TagsContext>) ->
                            result {
                                let! (i, _) = viewer ()
                                return
                                    fun () ->
                                        result {
                                            let! (j, state) = viewer ()
                                            let! isGreater =
                                                (j >= i)
                                                |> Result.ofBool "Cannot undo this command"
                                            let result =
                                                state.RemoveTag t.Id
                                                |> Result.map (fun _ -> [TagRemoved t.Id])
                                            return! result      
                                        }
                                    
                            }
                        )
                | _ -> None
                        
                    
