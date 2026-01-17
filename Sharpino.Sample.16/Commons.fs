namespace Sharpino.Template
open System
open System.Text.Json
open System.Text.Json.Serialization

module Commons =
    type TodoId =
        | TodoId of Guid
        with
            static member New = TodoId (Guid.NewGuid())
            member this.Value = match this with TodoId id -> id
    
    type MaterialId =
        | MaterialId of Guid
        with
            static member New = MaterialId (Guid.NewGuid())
            member this.Value = match this with MaterialId id -> id
    
    type ProductId =
        | ProductId of Guid
        with
            static member New = ProductId (Guid.NewGuid())
            member this.Value = match this with ProductId id -> id        

    type WorkOrderId =
        | WorkOrderId of Guid
        with
            static member New = WorkOrderId (Guid.NewGuid())
            member this.Value = match this with WorkOrderId id -> id
    
    type Quantity =
        | Quantity of int
        with
            static member New (quantity: int) =
                if quantity <= 0 then
                    Error "Quantity must be positive"
                else
                    Ok (Quantity quantity)
            member this.Value =
                match this with
                | Quantity value -> value
            member this.Subtract (quantity: Quantity) =
                match this.Value - quantity.Value with
                | x when x < 0 -> Error "Quantity cannot be negative"
                | x -> Ok (Quantity x)
            member this.Add (quantity: Quantity) =
                Quantity (this.Value + quantity.Value)

    let jsonOptions =
        JsonFSharpOptions.Default()
            .ToJsonSerializerOptions()

module JsonUtils =
    
    let jsonOptions =
        JsonFSharpOptions.Default()
            .ToJsonSerializerOptions()
    
    let serializeJson x =
        (x, jsonOptions) |> JsonSerializer.Serialize
    let DeserializeJson<'A> (s: string) =
        try
            JsonSerializer.Deserialize<'A> (s, jsonOptions) |> Ok
        with
            | ex -> Error ex.Message    