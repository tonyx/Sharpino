module Sharpino.Lib.Test.Models.ContextObject.Commands

open Sharpino
open Sharpino.Core
open Sharpino.Commons
open Sharpino.Lib.Test.Models.ContextObject.Events
open Sharpino.Lib.Test.Models.ContextObject.SampleContext
open Sharpino.Lib.Test.Models.SampleObject

type SampleContextCommands =
    | AddTag of tag: Tag
    | RemoveTag of tag: Tag
    | Rename of name: string
    interface Command<SampleContext, SampleContextEvents> with
        member this.Execute(state: SampleContext) =
            match this with
            | AddTag tag ->
                state.AddTag tag
                |> Result.map (fun x -> x, [SampleContextTagAdded tag])
            | RemoveTag tag ->
                state.RemoveTag tag
                |> Result.map (fun x -> x, [SampleContextTagRemoved tag])
            | Rename name ->
                state.Rename name
                |> Result.map (fun x -> x, [SampleContextRenamed name])

        member this.Undoer =
            None
                
                