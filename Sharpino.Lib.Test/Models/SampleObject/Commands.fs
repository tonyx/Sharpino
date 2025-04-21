module Sharpino.Lib.Test.Models.SampleObject.Commands

open Sharpino
open Sharpino.Core
open Sharpino.Commons
open Sharpino.Lib.Test.Models.SampleObject
open Sharpino.Lib.Test.Models.SampleObject.Events
open Sharpino.Lib.Test.Models.SampleObject.SampleObject

type SampleObjectCommands =
    | RenameSampleObject of string
    interface AggregateCommand<SampleObject, SampleObjectEvents> with
        member this.Execute (sampleObject: SampleObject) =
            match this with
            | RenameSampleObject name ->
                sampleObject.Rename name
                |> Result.map (fun x -> (x, [SampleObjectRenamed x.Name]))
        member this.Undoer =
            None
            