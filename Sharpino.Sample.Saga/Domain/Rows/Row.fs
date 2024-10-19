module Sharpino.Sample.Saga.Domain.Seat.Row
open System
open Sharpino
open Sharpino.Commons

open Sharpino.Core
open FSharpPlus
open FSharpPlus.Operators
open FsToolkit.ErrorHandling

type Row = {
    totalSeats: int
    numberOfSeatsBooked: int
    Id: Guid
    AssociatedBookings: List<Guid>
}

with
    member this.IsFull = this.numberOfSeatsBooked >= this.totalSeats
    member this.FreeSeats = this.totalSeats - this.numberOfSeatsBooked
    
    member this.AddBooking (bookingId: Guid, seatsAsked: int) =
        result
            {
                do!
                    seatsAsked <= this.FreeSeats
                    |> Result.ofBool "not enough seats"
                return
                    {
                        this
                            with
                                numberOfSeatsBooked = this.numberOfSeatsBooked + seatsAsked
                                AssociatedBookings = bookingId :: this.AssociatedBookings
                    }
            }
    
    member this.AddBookings (n: int) =
        if this.numberOfSeatsBooked + n > this.totalSeats then
            Error "row is full"
        else
            Ok { this with numberOfSeatsBooked = this.numberOfSeatsBooked + n }

    member this.FreeBooking (bookingId: Guid, seatsFreed: int) =
        result
            {
                do!
                    this.AssociatedBookings |> List.contains bookingId
                    |> Result.ofBool "booking not found"
                return
                    {
                        this
                            with
                                numberOfSeatsBooked = this.numberOfSeatsBooked - seatsFreed
                                AssociatedBookings = this.AssociatedBookings |> List.filter (fun x -> x <> bookingId)
                    }
            }

    static member Deserialize(x: string) =
        jsonPSerializer.Deserialize<Row> x

    static member StorageName = "_seat"
    static member Version = "_01"
    static member SnapshotsInterval = 15

    member this.Serialize =
        jsonPSerializer.Serialize this
    
    interface Aggregate<string> with
        member this.Id = this.Id
        member this.Serialize = this.Serialize
