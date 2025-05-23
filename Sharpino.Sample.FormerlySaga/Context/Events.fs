module Sharpino.Sample.Saga.Context.Events

open Sharpino.Sample.Saga.Context.SeatBookings
open System
open Sharpino
open Sharpino.Commons

open Sharpino.Core
open FSharpPlus
open FSharpPlus.Operators
open FsToolkit.ErrorHandling

type TheaterEvents =
    | SeatReferenceAdded of Guid
    | SeatReferenceRemoved of Guid
    | BookingReferenceAdded of Guid
    | BookingReferenceRemoved of Guid
    | VoucherReferenceAdded of Guid
    | VoucherReferenceRemoved of Guid
    
    interface Event<Theater> with
        member
            this.Process (x: Theater) =
                match this with
                | SeatReferenceAdded rowId -> x.AddRowReference rowId
                | SeatReferenceRemoved rowId -> x.RemoveRowReference rowId
                | BookingReferenceAdded bookingId -> x.AddBookingReference bookingId
                | BookingReferenceRemoved bookingId -> x.RemoveBookingReference bookingId
                | VoucherReferenceAdded voucherId -> x.AddVoucherReference voucherId
                | VoucherReferenceRemoved voucherId -> x.RemoveVoucherReference voucherId
    member this.Serialize =
        jsonPSerializer.Serialize this
    static member
        Deserialize (x: string) =
            jsonPSerializer.Deserialize<TheaterEvents> x
            
                