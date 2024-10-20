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

    interface AggregateCommand<Booking, BookingEvents> with
        member
            this.Execute (x: Booking) =
                match this with
                | Assign rowId ->
                    x.Assign rowId
                    |> Result.map (fun s -> (s, [Assigned rowId]))
                | UnAssign ->
                    x.UnAssign ()
                    |> Result.map (fun s -> (s, [UnAssigned]))

            member this.Undoer =
                match this with
                | Assign _ ->
                    Some (fun (booking: Booking) (viewer: AggregateViewer<Booking>) ->
                        result {
                            let! (i, _) = viewer (booking.Id)
                            return
                                fun () ->
                                    result {
                                        let! (j, state) = viewer (booking.Id)
                                        let! isGreater =
                                            (j >= i)
                                            |> Result.ofBool "concurrency error"
                                        let result =
                                            state.UnAssign ()
                                            |> Result.map (fun _ -> [UnAssigned])
                                        return! result    
                                    }
                            }
                        )
                
                | UnAssign ->
                    Some (fun (booking: Booking) (viewer: AggregateViewer<Booking>) ->
                        result {
                            let! (i, state) = viewer (booking.Id)
                            let!
                                rowId =
                                    booking.RowId
                                    |> Result.ofOption "row not assigned"
                            return
                                fun () ->
                                    result {
                                        let! (j, _) = viewer (booking.Id)
                                        let! isGreater =
                                            (j >= i)
                                            |> Result.ofBool "concurrency error"
                                        let result =
                                            state.Assign rowId
                                            |> Result.map (fun _ -> [Assigned rowId])
                                        return! result    
                                    }
                            }
                        )