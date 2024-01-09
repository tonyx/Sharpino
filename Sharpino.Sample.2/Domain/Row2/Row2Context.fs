
namespace seatsLockWithSharpino
open FsToolkit.ErrorHandling
open Sharpino.Utils
open Row2
open Row

// I call it context but it works as an aggregate. Need to fix it in library, docs ...
module Row2Context =
    open System
    type Row2(rowContext: RowContext) =

        static member Zero =
            Row2(RowContext(row2Seats))

        static member StorageName =
            "_row2"
        static member Version =
            "_01"
        static member SnapshotsInterval =
            15
        static member Lock =
            new Object()

        member this.IsAvailable (seatId: Seats.Id) =
            rowContext.IsAvailable seatId

        member this.ReserveSeats (booking: Seats.Booking) =
            result {
                let! rowContext' = rowContext.BookSeats booking
                return Row2(rowContext')
            }

        member this.GetAvailableSeats () =
            rowContext.GetAvailableSeats ()
        member this.Serialize(serializer: ISerializer) =
            this
            |> serializer.Serialize
        static member Deserialize (serializer: ISerializer, json: string)=
            serializer.Deserialize<Row2> json