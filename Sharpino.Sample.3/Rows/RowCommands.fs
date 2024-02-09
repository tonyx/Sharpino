namespace Tonyx.SeatsBooking
open FsToolkit.ErrorHandling
open Sharpino.Utils
open Sharpino
open Sharpino.Core
open System
open Tonyx.SeatsBooking.SeatRow
open Tonyx.SeatsBooking.Shared.Entities
open Tonyx.SeatsBooking.RowAggregateEvent

module RowAggregateCommand =
    type RowAggregateCommand =
        | BookSeats of Booking
        | AddSeat of Seat
        | AddSeats of List<Seat>
        | AddInvariant of InvariantContainer
            interface Command<SeatsRow, RowAggregateEvent> with
                member this.Execute (x: SeatsRow) =
                    match this with
                    | BookSeats booking ->
                        x.BookSeats booking
                        |> Result.map (fun _ -> [SeatBooked booking])
                    | AddSeat seat ->
                        x.AddSeat seat
                        |> Result.map (fun _ -> [SeatAdded seat])
                    | AddSeats seats ->
                        x.AddSeats seats
                        |> Result.map (fun _ -> [SeatsAdded seats])
                    | AddInvariant invariant ->
                        x.AddInvariant invariant
                        |> Result.map (fun _ -> [InvariantAdded invariant])
                member this.Undoer =
                    None
                