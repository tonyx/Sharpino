
namespace seatsLockWithSharpino 
open Sharpino.Utils
open Sharpino.Core
open Sharpino.Definitions
open seatsLockWithSharpino.Commons
open Sharpino
open Sharpino.Commons

module Row1Events =
    type Row1Events =
        | SeatsBooked of Seats.Booking
        | Row1NoOp
            interface Event<Row1Context.Row1> with
                member this.Process (x: Row1Context.Row1) =
                    match this with
                    | SeatsBooked booking ->
                        x.BookSeats booking
                    | Row1NoOp ->
                        Ok x

        member this.Serialize =
            jsonPSerializer.Serialize this

        static member Deserialize json =
            jsonPSerializer.Deserialize<Row1Events> json




