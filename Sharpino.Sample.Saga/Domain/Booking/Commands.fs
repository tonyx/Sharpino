module Sharpino.Sample.Saga.Domain.Booking.Commands
open Sharpino.Sample.Saga.Domain.Booking.Booking
open Sharpino.Sample.Saga.Domain.Booking.Events
open System
open Sharpino
open Sharpino.Commons

open Sharpino.Core
open FSharpPlus
open FSharpPlus.Operators
open FsToolkit.ErrorHandling

type BookingCommands =
    | Assign of Guid
    | UnAssign

    interface Command<Booking, BookingEvents> with
        member
            this.Execute (x: Booking) =
                match this with
                | Assign rowId ->
                    x.Assign rowId
                    |> Result.map (fun s -> (s, [Assigned rowId]))
                | UnAssign ->
                    x.UnAssign ()
                    |> Result.map (fun s -> (s, [UnAssigned]))

            member this.Undoer = None
