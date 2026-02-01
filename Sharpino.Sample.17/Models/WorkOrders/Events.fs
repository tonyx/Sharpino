namespace sharpino.Template.Models
open Sharpino.Template.Commons
open System.Text.Json
open Sharpino.Core

type WorkOrderEvents =
    | Started of ProductId
    | Completed of ProductId
    | Failed of ProductId * Quantity
    
    interface Event<WorkOrder> with
        member this.Process (state: WorkOrder) =
            match this with
            | Started productId ->
                state.Start productId
            | Completed productId ->
                state.Complete productId
            | Failed (productId, quantity) ->
                state.Fail productId quantity
                
    static member Deserialize (data: string) =
        try
            JsonSerializer.Deserialize<WorkOrderEvents> (data, jsonOptions) |> Ok
        with
            | ex -> Error ex.Message
    
    member this.Serialize =
        (this, jsonOptions) |> JsonSerializer.Serialize