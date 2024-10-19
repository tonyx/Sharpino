module Sharpino.Sample.Saga.Domain.Seat.Events
open Sharpino.Sample.Saga.Domain.Seat.Row
open Sharpino.Core
open Sharpino.Commons
open System

type RowEvents =
    | BookingAdded of Guid * int
    | BookingFreed of Guid * int
    
    interface Event<Row> with
        member
            this.Process (x: Row) =
                match this with
                | BookingAdded (id, n) -> x.AddBooking (id, n)
                | BookingFreed (id, n) -> x.FreeBooking (id, n)

    member
        this.Serialize =
            jsonPSerializer.Serialize this
    static
        member
            Deserialize (x: string) =
                jsonPSerializer.Deserialize<RowEvents> x
                