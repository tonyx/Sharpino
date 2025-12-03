module Sharpino.Sample.Saga.Domain.Seat.Commands
open Sharpino.Sample.Saga.Domain.Seat.Events
open Sharpino.Sample.Saga.Domain.Seat.Row
open Sharpino.Core
open Sharpino
open System
open FSharpPlus
open FsToolkit.ErrorHandling

type RowCommands =
    | Book of Guid * int
    | Free of Guid * int
    | AddSeats of int
    | RemoveSeats of int

    interface AggregateCommand<Row, RowEvents> with
        member this.Execute (x: Row) =
            match this with
            | Book (bookingId, n) ->
                x.AddBooking (bookingId, n)
                |> Result.map (fun s -> (s, [RowEvents.BookingAdded (bookingId, n)]))
            | Free (bookingId, n) ->
                x.FreeBooking (bookingId, n) 
                |> Result.map (fun s -> (s, [RowEvents.BookingFreed (bookingId, n)]))
            | AddSeats n ->
                x.AddSeats n
                |> Result.map (fun s -> (s, [RowEvents.SeatsAdded n]))
            | RemoveSeats n ->
                x.RemoveSeats n
                |> Result.map (fun s -> (s, [RowEvents.SeatsRemoved n]))    
        member this.Undoer =
            match this with
            | Book (bookingId, numSeats) ->
                Some (fun (row: Row) (viewer: AggregateViewer<Row>) ->
                    result {
                        let! (i, _) = viewer (row.Id)
                        return
                            fun () ->
                                result {
                                    let! (j, state) = viewer (row.Id)
                                    let! isGreater =
                                        (j >= i)
                                        |> Result.ofBool "concurrency error"
                                    let result =
                                        state.FreeBooking (bookingId, numSeats)
                                        |> Result.map (fun s -> s, [RowEvents.BookingFreed (bookingId, numSeats)])
                                    return! result    
                                }
                        }
                    )
            | Free (bookingId, numSeats) ->
                Some (fun (row: Row) (viewer: AggregateViewer<Row>) ->
                    result {
                        let! (i, _) = viewer (row.Id)
                        return
                            fun () ->
                                result {
                                    let! (j, state) = viewer (row.Id)
                                    let! isGreater =
                                        (j >= i)
                                        |> Result.ofBool "concurrency error"
                                    let result =
                                        state.AddBookings numSeats
                                        |> Result.map (fun s -> s, [RowEvents.BookingAdded (bookingId, numSeats)])
                                    return! result    
                                }
                        }
                    )
            | AddSeats n ->
                Some (fun (row: Row) (viewer: AggregateViewer<Row>) ->
                    result {
                        let! (i, _) = viewer (row.Id)
                        return
                            fun () ->
                                result {
                                    let! (j, state) = viewer (row.Id)
                                    let! isGreater =
                                        (j >= i)
                                        |> Result.ofBool "concurrency error"
                                    let result =
                                        state.RemoveSeats n
                                        |> Result.map (fun s -> s, [RowEvents.SeatsRemoved n])
                                    return! result    
                                }
                        }
                    )
            | RemoveSeats n ->
                Some (fun (row: Row) (viewer: AggregateViewer<Row>) ->
                    result {
                        let! (i, _) = viewer (row.Id)
                        return
                            fun () ->
                                result {
                                    let! (j, state) = viewer (row.Id)
                                    let! isGreater =
                                        (j >= i)
                                        |> Result.ofBool "concurrency error"
                                    let result =
                                        state.AddSeats n
                                        |> Result.map (fun s -> s, [RowEvents.SeatsAdded n])
                                    return! result    
                                }
                        }
                    )        