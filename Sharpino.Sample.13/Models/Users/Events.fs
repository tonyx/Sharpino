namespace Sharpino.Sample._13.Models

open System
open Sharpino.Commons
open Sharpino.Core
open Sharpino
open System.Text.Json
open System.Text.Json.Serialization
open Sharpino.Sample._13.Commons
open Sharpino.Sample._13
open Sharpino.Sample._13.Models.User

module Events =
    type UserEvent =
        | PreferenceAdded of Preference
        | PreferenceRemoved of Preference
        interface Event<User> with
            member this.Process (x: User) =
                match this with
                | PreferenceAdded p -> x.AddPreference p
                | PreferenceRemoved p -> x.RemovePreference p
        member this.Serialize =
            JsonSerializer.Serialize(this, jsonOptions)

        static member Deserialize (x: string): Result<UserEvent, string> =
            try
                JsonSerializer.Deserialize<UserEvent>(x, jsonOptions) |> Ok
            with
            | ex -> Error ex.Message