module Sharpino.Sample.Saga.Context.Commands
open Sharpino.Sample.Saga.Context.Events
open Sharpino.Sample.Saga.Context.SeatBookings

open System
open Sharpino
open Sharpino.Commons

open Sharpino.Core
open FSharpPlus
open FSharpPlus.Operators
open FsToolkit.ErrorHandling

type TheaterCommands =
    | AddRowReference of Guid
    | RemoveRowReference of Guid
    | AddBookingReference of Guid
    | RemoveBookingReference of Guid

    interface Command<Theater, TheaterEvents> with
        member
            this.Execute (x: Theater) =
                match this with
                | AddRowReference rowId ->
                    x.AddRowReference rowId
                    |> Result.map (fun s -> (s, [SeatReferenceAdded rowId]))
                | RemoveRowReference rowId ->
                    x.RemoveRowReference rowId
                    |> Result.map (fun s -> (s, [SeatReferenceRemoved rowId]))
                | AddBookingReference bookingId ->
                    x.AddBookingReference bookingId
                    |> Result.map (fun s -> (s, [BookingReferenceAdded bookingId]))
                | RemoveBookingReference bookingId ->
                    x.RemoveBookingReference bookingId
                    |> Result.map (fun s -> (s, [BookingReferenceRemoved bookingId]))
        member this.Undoer = None
        