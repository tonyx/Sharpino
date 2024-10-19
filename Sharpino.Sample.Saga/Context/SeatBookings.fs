module Sharpino.Sample.Saga.Context.SeatBookings
open Sharpino
open Sharpino.Commons

open Sharpino.Core
open FSharpPlus
open FSharpPlus.Operators
open FsToolkit.ErrorHandling

open System

type SeatAndBookings = {
    Bookings: List<Guid>
    Rows: List<Guid>
}
with
    member this.AddSeats (rowId: Guid) =
        { this with Rows = rowId :: this.Rows } |> Ok
    member this.AddBooking (bookingId: Guid) =
        { this with Bookings = bookingId :: this.Bookings } |> Ok
    member this.RemoveBooking (bookingId: Guid) =
        { this with Bookings = this.Bookings |> List.filter (fun x -> x <> bookingId) } |> Ok
    member this.RemoveRow (rowId: Guid) =
        { this with Rows = this.Rows |> List.filter (fun x -> x <> rowId) } |> Ok
    
    static member Zero = { Bookings = []; Rows = [] }
  
    static member StorageName = "_seatBookings"
    static member Version = "_01"
    static member Deserialize(x: string) =
        jsonPSerializer.Deserialize<SeatAndBookings> x
   
    member this.Serialize =
        jsonPSerializer.Serialize this
        






