namespace Sharpino.Template.Models

open Sharpino.Template
open Sharpino.Template.Commons
open Sharpino.Core
open System.Text.Json
open FsToolkit.ErrorHandling
open System

type ProductEvents =
    | ProductNoOp
    interface Event<Product> with
        member this.Process item =
            match this with
            | ProductNoOp -> Ok item
            
    static member Deserialize (s: string) =
        JsonUtils.DeserializeJson<ProductEvents> s
            
    member this.Serialize =
        JsonUtils.serializeJson this