namespace seatsLockWithSharpino
open FsToolkit.ErrorHandling
open Sharpino.Utils
open Sharpino
open Sharpino.Core
open System
open Seats
open seatsLockWithSharpino.RefactoredRow

module RowAggregateEvent =
    ()
    // type RowAggregateEvent =
    //     | SeatBooked of Seats.Booking  
    //         interface Event<RefactoredRow> with
    //             member this.Process (x: RefactoredRow) =
    //                 match this with
    //                 | SeatBooked booking ->
    //                     x.BookSeats booking
    //     member this.Serialize(serializer: ISerializer) =
    //         this
    //         |> serializer.Serialize
    //     static member Deserialize(serializer: ISerializer) =
    //         serializer.Deserialize<RowAggregateEvent>