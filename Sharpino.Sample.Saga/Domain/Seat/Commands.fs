module Sharpino.Sample.Saga.Domain.Seat.Commands
open Sharpino.Sample.Saga.Domain.Seat.Events
open Sharpino.Sample.Saga.Domain.Seat.Row
open Sharpino.Core

type SeatCommands =
    | Reserve
    | ReserveMultiple of int
    | Free

    interface AggregateCommand<Row, SeatEvents> with
        member this.Execute (x: Row) =
            match this with
            | ReserveMultiple n ->
                x.AddReservations n |> Result.map (fun s -> (s, [SeatEvents.ReservationsAdded n]))
            | Reserve ->
                x.AddReservation () |> Result.map (fun s -> (s, [SeatEvents.ReservationAdded]))
            | Free ->
                x.FreeReservation () |> Result.map (fun s -> (s, [SeatEvents.ReservationFreed]))
        member this.Undoer = None
