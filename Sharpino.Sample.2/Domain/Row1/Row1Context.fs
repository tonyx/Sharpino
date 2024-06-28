namespace seatsLockWithSharpino
open FsToolkit.ErrorHandling
open Sharpino.Utils
open seatsLockWithSharpino.Commons
open Sharpino
open System
open Row1

// I call it context but it works as an aggregate. Need to fix it in library, docs ...
module Row1Context =
    open Row
    type Row1(rowContext: RowContext) =

        static member Zero =
            Row1(RowContext(row1Seats))

        member this.IsAvailable (seatId: Seats.Id) =
            rowContext.IsAvailable seatId

        member this.BookSeats (booking: Seats.Booking) =
            result {
                let! rowContext' = rowContext.BookSeats booking
                return Row1(rowContext')
            }

        member this.GetAvailableSeats () =
            rowContext.GetAvailableSeats ()
        member this.Serialize =
            this
            |> serializer.Serialize
        static member Deserialize  json =
            serializer.Deserialize<Row1> json

        static member StorageName =
            "_row1"
        static member Version =
            "_01"
        static member SnapshotsInterval =
            15
        static member Lock =
            new Object()
