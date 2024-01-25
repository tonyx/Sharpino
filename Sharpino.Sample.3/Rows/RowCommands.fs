namespace Tonyx.SeatsBooking
open FsToolkit.ErrorHandling
open Sharpino.Utils
open Sharpino
open Sharpino.Core
open System
open Tonyx.SeatsBooking.NewRow
open Tonyx.SeatsBooking.RowAggregateEvent

module RowAggregateCommand =
    type RowAggregateCommand =
        | BookSeats of Seats.Booking
        | AddSeat of Seats.Seat
        | AddSeats of List<Seats.Seat>
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
                member this.Undoer =
                    None
                