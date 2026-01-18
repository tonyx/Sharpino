module Sharpino.Sample.Saga.Domain.Seat.Row
open System
open Sharpino
open Sharpino.Commons

open Sharpino.Core
open FSharpPlus
open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Sharpino.Sample.Saga.Commons.Commons

let maximumSeats = 100
// type RowId = Guid
type Row = {
    TotalSeats: int
    NumberOfSeatsBooked: int
    Id: RowId
    AssociatedBookings: List<BookingId>
    AssociatedVouchers: List<VoucherId>
}

with
    member this.IsFull = this.NumberOfSeatsBooked >= this.TotalSeats
    member this.FreeSeats = this.TotalSeats - this.NumberOfSeatsBooked
    
    member this.AddBooking (bookingId: BookingId, seatsAsked: int) =
        result
            {
                do!
                    seatsAsked <= this.FreeSeats
                    |> Result.ofBool "not enough seats"
                return
                    {
                        this
                            with
                                NumberOfSeatsBooked = this.NumberOfSeatsBooked + seatsAsked
                                AssociatedBookings = bookingId :: this.AssociatedBookings
                    }
            }
            
    member this.AddSeats (n: int) =
        result
            {       
                do!
                    n > 0
                    |> Result.ofBool "n must be greater than 0"
                do!
                    this.TotalSeats + n <= maximumSeats
                    |> Result.ofBool "total seats must be less than 100"
                return
                    { this with TotalSeats = this.TotalSeats + n }
            }
    
    member this.RemoveSeats (n: int) =
        result
            {
                do!
                    n > 0
                    |> Result.ofBool "n must be greater than 0"
                do!
                    this.FreeSeats - n >= 0
                    |> Result.ofBool "not enough seats"
                return
                    { this with TotalSeats = this.TotalSeats - n }   
            }
            
    member this.AddBookings (n: int) =
        if this.NumberOfSeatsBooked + n > this.TotalSeats then
            Error "row is full"
        else
            Ok { this with NumberOfSeatsBooked = this.NumberOfSeatsBooked + n }

    member this.FreeBooking (bookingId: BookingId, seatsFreed: int) =
        result
            {
                do!
                    this.AssociatedBookings |> List.contains bookingId
                    |> Result.ofBool "booking not found"
                return
                    {
                        this
                            with
                                NumberOfSeatsBooked = this.NumberOfSeatsBooked - seatsFreed
                                AssociatedBookings = this.AssociatedBookings |> List.filter (fun x -> x <> bookingId)
                    }
            }

    static member Deserialize(x: string) =
        jsonPSerializer.Deserialize<Row> x

    static member StorageName = "_row"
    static member Version = "_01"
    static member SnapshotsInterval = 15

    member this.Serialize =
        jsonPSerializer.Serialize this
    
