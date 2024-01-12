namespace seatsLockWithSharpino 
open FsToolkit.ErrorHandling
open Sharpino.Utils
open Sharpino.Core
open Sharpino.Definitions
open Sharpino.Utils
open seatsLockWithSharpino.Row1Events
open Row2

module Row2Command =
    type Row2Command =
        | BookSeats of Seats.Booking
            interface Command<Row2Context.Row2, Row2Events.Row2Events > with
                member this.Execute (x: Row2Context.Row2) =
                    match this with
                    | BookSeats booking ->
                        x.ReserveSeats booking
                        |> Result.map (fun ctx -> [ Row2Events.SeatsBooked booking ])
                member this.Undoer = None