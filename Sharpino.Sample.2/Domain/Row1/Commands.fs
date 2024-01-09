
namespace seatsLockWithSharpino 
open FsToolkit.ErrorHandling
open Sharpino.Utils
open Sharpino.Core
open Sharpino.Definitions
open Sharpino.Utils
open seatsLockWithSharpino.Row1Events
open Row1

module Row1Command =
    type Row1Command =
        | BookSeats of Seats.Booking
            interface Command<Row1Context.Row1, Row1Events > with
                member this.Execute (x: Row1Context.Row1) =
                    match this with
                    | BookSeats booking ->
                        x.BookSeats booking
                        |> Result.map (fun ctx -> [ SeatsBooked booking ])
                member this.Undoer = None