
namespace seatsLockWithSharpino 
open Sharpino.Utils
open Sharpino.Core
open Sharpino.Definitions
open seatsLockWithSharpino.Commons
open Sharpino

module Row1Events =
    type Row1Events =
        | SeatsBooked of Seats.Booking
            interface Event<Row1Context.Row1> with
                member this.Process (x: Row1Context.Row1) =
                    match this with
                    | SeatsBooked booking ->
                        x.BookSeats booking

        member this.Serialize =
            this
            |> serializer.Serialize

        static member Deserialize json =
            serializer.Deserialize<Row1Events> json




