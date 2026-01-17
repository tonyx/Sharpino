namespace sharpino.Template.Models

open Sharpino.Template
open Sharpino.Template.Commons
open System.Text.Json
open Sharpino.Core
open Sharpino.Template.Models

type MaterialEvents =
    | Consumed of Quantity
    | Added of Quantity
    interface Event<Material> with
        member this.Process (state: Material) =
            match this with
            | Consumed quantity -> state.Consume quantity
            | Added quantity -> state.Add quantity
    
    static member Deserialize (x: string) =
        JsonUtils.DeserializeJson<MaterialEvents> x
    
    member this.Serialize =
        JsonUtils.serializeJson this