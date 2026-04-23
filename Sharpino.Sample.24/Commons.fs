namespace Sharpino.Template
open System
open System.Text.Json.Serialization

module Commons =
    type TodoId =
        | TodoId of Guid
        with
            static member New = TodoId (Guid.NewGuid())
            member this.Value = match this with TodoId id -> id

    type UserId =
        | UserId of Guid
        with
            static member New = UserId (Guid.NewGuid())
            member this.Value = match this with UserId id -> id

    let jsonOptions =
        JsonFSharpOptions.Default()
            .ToJsonSerializerOptions()


