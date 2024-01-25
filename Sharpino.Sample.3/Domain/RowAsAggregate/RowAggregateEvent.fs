namespace Tonyx.seatsLockWithSharpino
open FsToolkit.ErrorHandling
open Sharpino.Utils
open Sharpino
open Sharpino.Core
open Sharpino.Lib.Core.Commons
open System
open Tonyx.seatsLockWithSharpino
open Tonyx.seatsLockWithSharpino.RefactoredRow
open Tonyx.seatsLockWithSharpino.Seats

module RowAggregateEvent =
    type RowAggregateEvent =        
        | SeatBooked of Seats.Booking  
        | SeatAdded of Seats.Seat
        | SeatsAdded of Seats.Seat list
            interface Event<SeatsRow> with
                member this.Process (x: SeatsRow) =
                    match this with
                    | SeatBooked booking ->
                        x.BookSeats booking
                    | SeatAdded seat ->
                        x.AddSeat seat
                    | SeatsAdded seats ->
                        x.AddSeats seats
        member this.Serialize(serializer: ISerializer) =
            this
            |> serializer.Serialize
        static member Deserialize(serializer: ISerializer, x: string) =
            serializer.Deserialize<RowAggregateEvent> x
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