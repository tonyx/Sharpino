module Sharpino.Lib.Test.Models.SampleObject.Events

open Sharpino
open Sharpino.Core
open Sharpino.Commons
open Sharpino.Lib.Test.Models.SampleObject
open Sharpino.Lib.Test.Models.SampleObject.SampleObject

type SampleObjectEvents =
    | SampleObjectRenamed of string
    interface Event<SampleObject> with
        member this.Process (sampleObject: SampleObject) =
            match this with
            | SampleObjectRenamed name -> sampleObject.Rename name

    static member Deserialize x =
        jsonPSerializer.Deserialize<SampleObjectEvents> x
    member this.Serialize =
        jsonPSerializer.Serialize this
        