namespace Sharpino.Sample._9

open System
open Sharpino.Core

open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Sharpino.Sample._9.Reservation
open Sharpino.Sample._9.ReservationEvents

module ReservationCommands =
    type ReservationCommands =
        | CloseItem of Guid
        | Ping
            interface AggregateCommand<Reservation.Reservation, ReservationEvents> with
                member this.Execute (reservation: Reservation.Reservation) =
                    match this with
                    | CloseItem id ->
                        reservation.CloseItem id
                        |> Result.map (fun x -> (x, [ItemClosed id]))
                    | Ping ->
                        reservation.Ping ()
                        |> Result.map (fun x -> (x, [Pinged]))    
                member this.Undoer =
                    None