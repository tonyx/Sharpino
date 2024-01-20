namespace seatsLockWithSharpino
open FsToolkit.ErrorHandling
open Sharpino.Utils
open Sharpino
open Sharpino.Core
open System
open Seats
open seatsLockWithSharpino.RefactoredRow
open seatsLockWithSharpino.RowAggregateEvent

module RowAggregateCommand =
    ()
    // type RowAggregateCommand =
    //     | BookSeats of Seats.Booking
    //         interface Command<RefactoredRow, RowAggregateEvent> with
    //             member this.Execute (x: RefactoredRow) =
    //                 match this with
    //                 | BookSeats booking ->
    //                     x.BookSeats booking
    //                     |> Result.map (fun x -> [SeatBooked booking])
    //             member this.Undoer = None
                    
                