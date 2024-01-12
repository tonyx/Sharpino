
namespace seatsLockWithSharpino 
open Sharpino.Utils
open Sharpino.Core
open Sharpino.Definitions

module Row2Events =
    type Row2Events =
        | SeatsBooked of Seats.Booking
            interface Event<Row2Context.Row2> with
                member this.Process (x: Row2Context.Row2) =
                    match this with
                    | SeatsBooked booking ->
                        x.ReserveSeats booking
        member this.Serialize(serializer: ISerializer) =
            this
            |> serializer.Serialize

        static member Deserialize (serializer: ISerializer, json: Json) =
            serializer.Deserialize<Row2Events> json

