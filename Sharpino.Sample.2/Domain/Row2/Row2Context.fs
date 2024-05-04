
namespace seatsLockWithSharpino
open FsToolkit.ErrorHandling
open Sharpino.Utils
open seatsLockWithSharpino.Commons
open Sharpino
open Row2
open Row
open System

module Row2Context =
    type Row2 (rowContext: RowContext) =
        let stateId = Guid.NewGuid()
        member this.StateId = stateId

        static member Zero =
            Row2 (RowContext row2Seats)


        member this.IsAvailable (seatId: Seats.Id) =
            rowContext.IsAvailable seatId

        member this.ReserveSeats (booking: Seats.Booking) =
            result {
                let! rowContext' = rowContext.BookSeats booking
                return Row2(rowContext')
            }
        member this.GetAvailableSeats () =
            rowContext.GetAvailableSeats ()
        member this.Serialize =
            this
            |> serializer.Serialize
        static member Deserialize json =
            serializer.Deserialize<Row2> json
        static member StorageName =
            "_row2"
        static member Version =
            "_01"
        static member SnapshotsInterval =
            15
        static member Lock =
            new Object()
