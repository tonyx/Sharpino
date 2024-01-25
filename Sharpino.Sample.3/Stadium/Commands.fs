
namespace seatsLockWithSharpino
open seatsLockWithSharpino.Stadium
open seatsLockWithSharpino.StadiumEvents
open FsToolkit.ErrorHandling
open Sharpino.Definitions
open Sharpino.Core
open System
open Sharpino.Utils

module StadiumCommands =
    type StadiumCommand =
        | AddRowReference of Guid
        | RemoveRowReference of Guid
            interface Command<Stadium, StadiumEvent> with
                member this.Execute (x: Stadium) =
                    match this with
                    | AddRowReference id ->
                        x.AddRowReference id
                        |> Result.map (fun _ -> [StadiumEvent.RowReferenceAdded id])
                    | RemoveRowReference id ->
                        x.RemoveRowReference id
                        |> Result.map (fun _ -> [StadiumEvent.RowReferenceRemoved id])
                member this.Undoer = None 

