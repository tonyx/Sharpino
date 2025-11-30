namespace Sharpino.Sample._13.Models

open Sharpino.Core
open Sharpino.Sample._13.Models.Reservation
open Sharpino.Sample._13.Models.ReservationEvents

module ReservationCommands =
    type ReservationCommand =
        | AddClaim of System.Guid * string
        | RemoveClaim of System.Guid * string
        | Claim of System.Guid * string
        interface AggregateCommand<Reservation.ReservationForNickNames, ReservationEvent> with
            member this.Execute (x: Reservation.ReservationForNickNames) =
                match this with
                | AddClaim (guid, v) ->
                    x.AddClaim (guid, v) 
                    |> Result.map (fun s -> (s, [ReservationEvent.ClaimAdded (guid, v)]))
                | RemoveClaim(guid, v) ->
                    x.RemoveClaim (guid, v)
                    |> Result.map (fun s -> (s, [ReservationEvent.ClaimRemoved (guid, v)]))
                | Claim (guid, v) ->
                    x.Claim (guid, v) 
                    |> Result.map (fun s -> (s, [ReservationEvent.Claimed (guid, v)]))
            member this.Undoer = None
