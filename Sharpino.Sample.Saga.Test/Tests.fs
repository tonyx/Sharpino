module Tests
open System
open Sharpino
open Sharpino.CommandHandler
open Sharpino.StateView
open Sharpino.Commons

open Sharpino.Core
open FSharpPlus
open FSharpPlus.Operators
open Sharpino.Sample.Saga.Domain.Seat.Row
open Sharpino.Sample.Saga.Domain.Seat.Events
open Sharpino.Sample.Saga.Domain.Seat.Commands
open Sharpino.Sample.Saga.Api.SeatBooking
open Sharpino.Sample.Saga.Context.SeatBookings
open Sharpino.Sample.Saga.Context.Events
open Sharpino.Sample.Saga.Context.Commands
open Sharpino.Sample.Saga.Domain.Seat.Row
open Sharpino.Sample.Saga.Domain.Seat.Commands
open Sharpino.Sample.Saga.Domain.Seat.Events
open Sharpino.Sample.Saga.Domain.Booking.Booking
open Sharpino.Sample.Saga.Domain.Booking.Commands
open Sharpino.Sample.Saga.Domain.Booking.Events
open Expecto
open Sharpino.MemoryStorage
open Sharpino.Storage

let memoryStorage: IEventStore<_> = new MemoryStorage()

let doNothingBroker: IEventBroker<_> =
    {
        notify = None
        notifyAggregate = None
    }
let teatherContextViewer = getStorageFreshStateViewer<Theater, TheaterEvents, string> memoryStorage 
let seatsAggregateViewer = fun id -> getAggregateFreshState<Row, RowEvents, string> id memoryStorage 
let bookingsAggregateViewer = fun id -> getAggregateFreshState<Booking, BookingEvents, string> id memoryStorage

[<Tests>]
let tests =
    testList "samples" [
        testCase "seat service has zero booking - Ok" <| fun _ ->
            let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker)
            let rows = seatBookingService.GetRows()
            Expect.isOk rows "should be ok"
            Expect.equal rows.OkValue.Length 0 "should be zero"
    ]
    |> testSequenced

