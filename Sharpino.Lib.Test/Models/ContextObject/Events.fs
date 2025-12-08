module Sharpino.Lib.Test.Models.ContextObject.Events

open Sharpino
open Sharpino.Core
open Sharpino.Commons
open Sharpino.Lib.Test.Models.ContextObject.SampleContext

type SampleContextEvents =
    | SampleContextTagAdded of tag: Tag
    | SampleContextTagRemoved of tag: Tag
    | SampleContextRenamed of name: string
        interface Event<SampleContext> with
            member this.Process(state: SampleContext) =
                match this with
                | SampleContextTagAdded tag -> state.AddTag tag
                | SampleContextTagRemoved tag -> state.RemoveTag tag
                | SampleContextRenamed name -> state.Rename name
            
    static member Deserialize x =
        jsonPSerializer.Deserialize<SampleContextEvents> x
    member this.Serialize =
        jsonPSerializer.Serialize this
    
