namespace Tonyx.SeatsBooking

open Tonyx.SeatsBooking.Commons
open FsToolkit.ErrorHandling
open Shared.Entities
open Sharpino.Utils
open Sharpino
open Sharpino.Core
open Sharpino.Definitions
open Sharpino.Lib.Core.Commons
open Tonyx.SeatsBooking
open Tonyx.SeatsBooking.SeatRow

module RowAggregateEvent =
    type RowAggregateEvent =
        | SeatBooked of Booking
        | SeatAdded of Seat
        | SeatsAdded of List<Seat>
        | SeatRemoved of Seat
        | InvariantAdded of InvariantContainer
            interface Event<SeatsRow> with
                member this.Process (x: SeatsRow) =
                    match this with
                    | SeatBooked booking ->
                        x.BookSeats booking
                    | SeatAdded seat ->
                        x.AddSeat seat
                    | SeatRemoved seat ->
                        x.RemoveSeat seat
                    | SeatsAdded seats ->
                        x.AddSeats seats
                    | InvariantAdded invariant ->
                        x.AddInvariant invariant

        member this.Serialize =
            this
            |> serializer.Serialize
        static member Deserialize  x =
            serializer.Deserialize<RowAggregateEvent> x
