
namespace seatsLockWithSharpino 
open Sharpino.Utils
open Sharpino.Core
open seatsLockWithSharpino.Commons
open Sharpino
open Sharpino.Definitions

module Row2Events =
    type Row2Events =
        | SeatsBooked of Seats.Booking
            interface Event<Row2Context.Row2> with
                member this.Process (x: Row2Context.Row2) =
                    match this with
                    | SeatsBooked booking ->
                        x.ReserveSeats booking
        member this.Serialize =
            this
            |> serializer.Serialize

        static member Deserialize json =
            serializer.Deserialize<Row2Events> json

