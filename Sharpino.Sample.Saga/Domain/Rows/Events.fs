module Sharpino.Sample.Saga.Domain.Seat.Events
open Sharpino.Sample.Saga.Domain.Seat.Row
open Sharpino.Core
open Sharpino.Commons

type SeatEvents =
    | ReservationAdded
    | ReservationsAdded of int
    | ReservationFreed
    
    interface Event<Row> with
        member
            this.Process (x: Row) =
                match this with
                | ReservationsAdded n -> x.AddBookings n
                | ReservationAdded -> x.AddBooking ()
                | ReservationFreed -> x.FreeBookings ()

    member
        this.Serialize =
            jsonPSerializer.Serialize this
    static
        member
            Deserialize (x: string) =
                jsonPSerializer.Deserialize<SeatEvents> x
                