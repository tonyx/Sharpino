namespace Tonyx.SeatsBooking
open Tonyx.SeatsBooking.Stadium
open Tonyx.SeatsBooking.StadiumEvents
open FsToolkit.ErrorHandling
open Sharpino.Core
open System

module StadiumCommands =
    type StadiumCommand =
        | AddRowReference of Guid
        | RemoveRowReference of Guid
            interface Command<Stadium, StadiumEvent> with
                member this.Execute (x: Stadium) =
                    match this with
                    | AddRowReference id ->
                        x.AddRowReference id
                        |> Result.map (fun s -> (s, [StadiumEvent.RowReferenceAdded id]))
                    | RemoveRowReference id ->
                        x.RemoveRowReference id
                        |> Result.map (fun s -> (s, [StadiumEvent.RowReferenceRemoved id]))
                member this.Undoer = None
