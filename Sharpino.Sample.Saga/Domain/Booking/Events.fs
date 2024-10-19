module Sharpino.Sample.Saga.Domain.Booking.Events

open System
open Sharpino
open Sharpino.Commons

open Sharpino.Core
open FSharpPlus
open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Sharpino.Sample.Saga.Domain.Booking.Booking

type BookingEvents =
    | Assigned of Guid
    | UnAssigned  

    interface Event<Booking> with
        member
            this.Process (x: Booking) =
                match this with
                | Assigned rowId -> x.Assign rowId
                | UnAssigned -> x.UnAssign ()

    member this.Serialize =
            jsonPSerializer.Serialize this
    static member
        Deserialize (x: string) =
                jsonPSerializer.Deserialize<BookingEvents> x
                