namespace Sharpino.Sample._11
open System.Text.Json
open System.Text.Json.Serialization

module Definitions =
    let jsonOptions =
        JsonFSharpOptions.Default()
            .ToJsonSerializerOptions()
