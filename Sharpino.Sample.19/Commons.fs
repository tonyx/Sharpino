namespace Sharpino.Template
open System
open System.Text.Json.Serialization

module Commons =
    type TodoId =
        | TodoId of Guid
        with
            static member New = TodoId (Guid.NewGuid())
            member this.Value = match this with TodoId id -> id

    let jsonOptions =
        JsonFSharpOptions.Default()
            .ToJsonSerializerOptions()




