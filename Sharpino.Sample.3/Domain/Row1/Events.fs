
namespace seatsLockWithSharpino 
open Sharpino.Utils
open Sharpino.Core
open Sharpino.Definitions

module Row1Events =
    type Row1Events =
        | SeatsBooked of Seats.Booking
            interface Event<Row1Context.Row1> with
                member this.Process (x: Row1Context.Row1) =
                    match this with
                    | SeatsBooked booking ->
                        x.BookSeats booking

        member this.Serialize(serializer: ISerializer) =
            this
            |> serializer.Serialize

        static member Deserialize (serializer: ISerializer, json: Json) =
            serializer.Deserialize<Row1Events> json




