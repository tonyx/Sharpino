namespace Tonyx.SeatsBooking
open FsToolkit.ErrorHandling
open Sharpino.Utils
open Sharpino
open Sharpino.Core
open System
open Tonyx.SeatsBooking.SeatRow
open Shared.Entities
open Tonyx.SeatsBooking.RowAggregateEvent

module RowAggregateCommand =
    type RowAggregateCommand =
        | BookSeats of Booking
        | AddSeat of Seat
        | AddSeats of List<Seat>
        | RemoveSeat of Seat
        | AddInvariant of InvariantContainer
        | RemoveInvariant of InvariantContainer
            interface AggregateCommand<SeatsRow, RowAggregateEvent> with
                member this.Execute (x: SeatsRow) =
                    match this with
                    | BookSeats booking ->
                        x.BookSeats booking
                        |> Result.map (fun s -> (s, [SeatBooked booking]))
                    | AddSeat seat ->
                        x.AddSeat seat
                        |> Result.map (fun s -> (s, [SeatAdded seat]))
                    | RemoveSeat seat ->
                        x.RemoveSeat seat
                        |> Result.map (fun s -> (s, [SeatRemoved seat]))
                    | AddSeats seats ->
                        x.AddSeats seats
                        |> Result.map (fun s -> (s, [SeatsAdded seats]))
                    | AddInvariant invariant ->
                        x.AddInvariant invariant
                        |> Result.map (fun s -> (s, [InvariantAdded invariant]))
                    | RemoveInvariant invariant ->
                        x.RemoveInvariant invariant
                        |> Result.map (fun s -> (s, [InvariantRemoved invariant]))
                member this.Undoer =
                    None


