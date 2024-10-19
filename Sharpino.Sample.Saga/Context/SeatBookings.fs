module Sharpino.Sample.Saga.Context.SeatBookings
open Sharpino
open Sharpino.Commons

open Sharpino.Core
open FSharpPlus
open FSharpPlus.Operators
open FsToolkit.ErrorHandling

open System

type Theater = {
    Bookings: List<Guid>
    Rows: List<Guid>
}
with
    member this.AddRowReference (rowId: Guid) =
        { this with Rows = rowId :: this.Rows } |> Ok
    member this.AddBookingReference (bookingId: Guid) =
        { this with Bookings = bookingId :: this.Bookings } |> Ok
    member this.RemoveBookingReference (bookingId: Guid) =
        { this with Bookings = this.Bookings |> List.filter (fun x -> x <> bookingId) } |> Ok
    member this.RemoveRowReference (rowId: Guid) =
        { this with Rows = this.Rows |> List.filter (fun x -> x <> rowId) } |> Ok
    
    static member Zero = { Bookings = []; Rows = [] }
  
    static member StorageName = "_seatBookings"
    static member Version = "_01"
    static member SnapshotsInterval = 15
    static member Deserialize(x: string) =
        jsonPSerializer.Deserialize<Theater> x
   
    member this.Serialize =
        jsonPSerializer.Serialize this
        
