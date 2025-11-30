namespace Sharpino.Sample._13.Models

open System
open Sharpino.Core
open System.Text.Json
open Sharpino.Sample._13.Commons
open Sharpino.Sample._13.Models.Reservation

module ReservationEvents =
    type ReservationEvent =
        | ClaimAdded of Guid * string
        | ClaimRemoved of Guid * string
        | Claimed of Guid * string
        interface Event<Reservation.ReservationForNickNames> with
            member this.Process (x: Reservation.ReservationForNickNames) =
                match this with
                | ClaimAdded(guid, s) -> x.AddClaim (guid, s) 
                | ClaimRemoved(guid, s) -> x.RemoveClaim (guid, s) 
                | Claimed(guid, s) -> x.Claim (guid, s)
        member this.Serialize =
            JsonSerializer.Serialize(this, jsonOptions)

        static member Deserialize (x: string): Result<ReservationEvent, string> =
            try
                JsonSerializer.Deserialize<ReservationEvent>(x, jsonOptions) |> Ok
            with
            | ex -> Error ex.Message
