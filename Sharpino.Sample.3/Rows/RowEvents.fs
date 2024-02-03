namespace Tonyx.SeatsBooking
open FsToolkit.ErrorHandling
open Sharpino.Utils
open Sharpino
open Sharpino.Core
open Sharpino.Lib.Core.Commons
open System
open Tonyx.SeatsBooking
open Tonyx.SeatsBooking.SeatRow
open Tonyx.SeatsBooking.Seats

module RowAggregateEvent =
    type RowAggregateEvent =        
        | SeatBooked of Seats.Booking  
        | SeatAdded of Seats.Seat
        | SeatsAdded of Seats.Seat list
        | InvariantAdded of InvariantContainer
            interface Event<SeatsRow> with
                member this.Process (x: SeatsRow) =
                    match this with
                    | SeatBooked booking ->
                        x.BookSeats booking
                    | SeatAdded seat ->
                        x.AddSeat seat
                    | SeatsAdded seats ->
                        x.AddSeats seats
                    | InvariantAdded invariant ->
                        x.AddInvariant invariant
                        
        member this.Serialize(serializer: ISerializer) =
            this
            |> serializer.Serialize
        static member Deserialize(serializer: ISerializer, x: string) =
            serializer.Deserialize<RowAggregateEvent> x