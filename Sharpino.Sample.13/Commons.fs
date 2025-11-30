module Sharpino.Sample._13.Commons

open System
open Sharpino.Commons
open Sharpino
open Sharpino.Core
open System.Text.Json
open System.Text.Json.Serialization

let reservationGuid = Guid.Parse("3be56fbf-5ef3-4e0d-b26a-87d2663f74ba")

let jsonOptions =
    JsonFSharpOptions.Default()
        .ToJsonSerializerOptions()
