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
        testCase "seat service has zero rows - Ok" <| fun _ ->
            let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let rows = seatBookingService.GetRows()
            Expect.isOk rows "should be ok"
            Expect.equal rows.OkValue.Length 0 "should be zero"

        testCase "seat service has zero booking - Ok" <| fun _ ->
            let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let bookings = seatBookingService.GetBookings()
            Expect.isOk bookings "should be ok"
            Expect.equal bookings.OkValue.Length 0 "should be zero"

        testCase "add and retrieve a row - Ok" <| fun _ ->
            let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let row = { totalSeats = 10; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }
            let addRow = seatBookingService.AddRow row 
            Expect.isOk addRow "should be ok"
            let rows = seatBookingService.GetRows()
            Expect.isOk rows "should be ok"
            Expect.equal rows.OkValue.Length 1 "should be one"

        testCase "add and retrieve a booking - Ok" <| fun _ ->
            let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let booking = { Id = Guid.NewGuid(); ClaimedSeats = 1; RowId = None}
            let addBooking = seatBookingService.AddBooking booking
            Expect.isOk addBooking "should be ok"
            let bookings = seatBookingService.GetBookings()
            Expect.isOk bookings "should be ok"
            Expect.equal bookings.OkValue.Length 1 "should be one"

        testCase "assign a booking to a row and verify that the booking has a rowId set to that rowId - Ok" <| fun _ ->
            let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let row = { totalSeats = 10; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }
            let addRow = seatBookingService.AddRow row 
            Expect.isOk addRow "should be ok"
            let booking = { Id = Guid.NewGuid(); ClaimedSeats = 1; RowId = None}
            let addBooking = seatBookingService.AddBooking booking
            Expect.isOk addBooking "should be ok"
            let assignBooking = seatBookingService.AssignBooking booking.Id row.Id
            Expect.isOk assignBooking "should be ok"

            let bookings = seatBookingService.GetBooking booking.Id

            Expect.isOk bookings "should be ok"
            Expect.equal bookings.OkValue.RowId (Some row.Id) "should be equal"
        
        testCase "assign a booking to a row and verify that the row has the bookingId in the AssociatedBookings list - Ok" <| fun _ ->
            let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let row = { totalSeats = 10; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }
            let addRow = seatBookingService.AddRow row 
            Expect.isOk addRow "should be ok"
            let booking = { Id = Guid.NewGuid(); ClaimedSeats = 1; RowId = None}
            let addBooking = seatBookingService.AddBooking booking
            Expect.isOk addBooking "should be ok"
            let assignBooking = seatBookingService.AssignBooking booking.Id row.Id
            Expect.isOk assignBooking "should be ok"

            let row = seatBookingService.GetRow row.Id    
            Expect.isOk row "should be ok"
            let associatedBookings = row.OkValue.AssociatedBookings
            Expect.equal associatedBookings.Length 1 "should be one"

        testCase "assign a booking where the number of claimed seats is superior than the availability - Error" <| fun _ ->
            let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let row = { totalSeats = 10; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }
            let addRow = seatBookingService.AddRow row 
            Expect.isOk addRow "should be ok"
            let booking = { Id = Guid.NewGuid(); ClaimedSeats = 11; RowId = None}
            let addBooking = seatBookingService.AddBooking booking
            Expect.isOk addBooking "should be ok"
            let assignBooking = seatBookingService.AssignBooking booking.Id row.Id
            Expect.isError assignBooking "should be error"
            let (Error e) = assignBooking
            Expect.equal e "not enough seats" "should be equal"

        testCase "in doing two consecutive bookings that succeeds, the number of free seats is the initial minus the sum of the claimed seats - Ok" <| fun _ ->
            let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let row = { totalSeats = 10; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }
            let addRow = seatBookingService.AddRow row 
            Expect.isOk addRow "should be ok"
            let booking1 = { Id = Guid.NewGuid(); ClaimedSeats = 1; RowId = None}
            let addBooking1 = seatBookingService.AddBooking booking1
            Expect.isOk addBooking1 "should be ok"
            let assignBooking1 = seatBookingService.AssignBooking booking1.Id row.Id
            Expect.isOk assignBooking1 "should be ok"
            let booking2 = { Id = Guid.NewGuid(); ClaimedSeats = 2; RowId = None}
            let addBooking2 = seatBookingService.AddBooking booking2
            Expect.isOk addBooking2 "should be ok"
            let assignBooking2 = seatBookingService.AssignBooking booking2.Id row.Id
            Expect.isOk assignBooking2 "should be ok"

            let row = seatBookingService.GetRow row.Id    
            Expect.isOk row "should be ok"
            let freeSeats = row.OkValue.FreeSeats
            Expect.equal freeSeats 7 "should be equal"
        
        testCase "make two consecutive bookings, the second one exceeds the total and therefore it fails whereas the number of remaining seats is the initial minus the number related to the first booking - Ok" <| fun _ ->
            // preparation
            let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let row = { totalSeats = 10; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }
            let addRow = seatBookingService.AddRow row 
            Expect.isOk addRow "should be ok"
            let booking1 = { Id = Guid.NewGuid(); ClaimedSeats = 1; RowId = None}
            let addBooking1 = seatBookingService.AddBooking booking1
            Expect.isOk addBooking1 "should be ok"

            // actions
            let assignBooking1 = seatBookingService.AssignBooking booking1.Id row.Id
            Expect.isOk assignBooking1 "should be ok"
            let booking2 = { Id = Guid.NewGuid(); ClaimedSeats = 10; RowId = None}
            let addBooking2 = seatBookingService.AddBooking booking2
            Expect.isOk addBooking2 "should be ok"
            let assignBooking2 = seatBookingService.AssignBooking booking2.Id row.Id
            Expect.isError assignBooking2 "should be error"
            let (Error e) = assignBooking2
            Expect.equal e "not enough seats" "should be equal"
            
            // expectation
            let row = seatBookingService.GetRow row.Id    
            Expect.isOk row "should be ok"
            let freeSeats = row.OkValue.FreeSeats
            Expect.equal freeSeats 9 "should be equal"
        
        testCase "can do in parallel two bookings on two different seats - OK"  <| fun _ ->
            // preparation
            let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let row1 = { totalSeats = 10; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }
            Expect.isOk (seatBookingService.AddRow row1)  "should be ok"
            let row2 = { totalSeats = 10; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }
            Expect.isOk (seatBookingService.AddRow row2) "should be ok"
            let booking1 = { Id = Guid.NewGuid(); ClaimedSeats = 1; RowId = None}
            Expect.isOk (seatBookingService.AddBooking booking1) "should be ok"
            let booking2 = { Id = Guid.NewGuid(); ClaimedSeats = 1; RowId = None}
            Expect.isOk (seatBookingService.AddBooking booking2) "should be ok"

            // action
            let assignBookings = seatBookingService.AssignBookings ([(booking1.Id, row1.Id); (booking2.Id, row2.Id)]) 
            Expect.isOk assignBookings "should be ok"
            let row1 = seatBookingService.GetRow row1.Id
            let row2 = seatBookingService.GetRow row2.Id
            Expect.isOk row1 "should be ok"
            Expect.isOk row2 "should be ok"

            // expectation
            Expect.equal row1.OkValue.FreeSeats 9 "should be equal"
            Expect.equal row2.OkValue.FreeSeats 9 "should be equal"
        
        testCase "do parallel bookings on two different seats whereas one of the booking can't succeed, so all the bookings must fails - Ok" <| fun _ ->
            // preparation
            let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let row1 = { totalSeats = 10; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }
            Expect.isOk (seatBookingService.AddRow row1)  "should be ok"
            let row2 = { totalSeats = 10; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }
            Expect.isOk (seatBookingService.AddRow row2) "should be ok"
            let booking1 = { Id = Guid.NewGuid(); ClaimedSeats = 1; RowId = None}
            Expect.isOk (seatBookingService.AddBooking booking1) "should be ok"
            let booking2 = { Id = Guid.NewGuid(); ClaimedSeats = 11; RowId = None}
            Expect.isOk (seatBookingService.AddBooking booking2) "should be ok"
            
            // action
            let assignBookings = seatBookingService.AssignBookings ([(booking1.Id, row1.Id); (booking2.Id, row2.Id)])
            Expect.isError assignBookings "should be error"
            let (Error e) = assignBookings
            Expect.equal e "not enough seats" "should be equal"
            let row1 = seatBookingService.GetRow row1.Id
            let row2 = seatBookingService.GetRow row2.Id
            Expect.isOk row1 "should be ok"
            Expect.isOk row2 "should be ok"    

            // expectation
            Expect.equal row1.OkValue.FreeSeats 10 "should be equal"
            Expect.equal row2.OkValue.FreeSeats 10 "should be equal"

        testCase "can't do in parallel two bookings on the same row - Error"  <| fun _ ->
            // preparation
            let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let row = { totalSeats = 10; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }
            let booking1 = { Id = Guid.NewGuid(); ClaimedSeats = 1; RowId = None}
            let booking2 = { Id = Guid.NewGuid(); ClaimedSeats = 1; RowId = None}
            let addRow = seatBookingService.AddRow row    
            let addBooking1 = seatBookingService.AddBooking booking1
            let addBooking2 = seatBookingService.AddBooking booking2
            
            // action
            let assignBookings = seatBookingService.AssignBookings ([(booking1.Id, row.Id); (booking2.Id, row.Id)])

            // expectation
            Expect.isError assignBookings "should be error" 
            let (Error e) = assignBookings
            Expect.equal e "aggregateIds2 are not unique" "should be equal"
            let row = seatBookingService.GetRow row.Id
            Expect.isOk row "should be ok"
            Expect.equal row.OkValue.FreeSeats 10 "should be equal"    

        testCase "do in parallel two bookings on two different seats using no different id checks, works as in the normal case - OK"  <| fun _ ->
            // preparation
            let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let row1 = { totalSeats = 10; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }
            Expect.isOk (seatBookingService.AddRow row1)  "should be ok"
            let row2 = { totalSeats = 10; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }
            Expect.isOk (seatBookingService.AddRow row2) "should be ok"
            let booking1 = { Id = Guid.NewGuid(); ClaimedSeats = 1; RowId = None}
            Expect.isOk (seatBookingService.AddBooking booking1) "should be ok"
            let booking2 = { Id = Guid.NewGuid(); ClaimedSeats = 1; RowId = None}
            Expect.isOk (seatBookingService.AddBooking booking2) "should be ok"

            // action
            let assignBookings = seatBookingService.ForceAssignBookings ([(booking1.Id, row1.Id); (booking2.Id, row2.Id)]) 
            Expect.isOk assignBookings "should be ok"
            let row1 = seatBookingService.GetRow row1.Id
            let row2 = seatBookingService.GetRow row2.Id
            Expect.isOk row1 "should be ok"
            Expect.isOk row2 "should be ok"

            // expectation
            Expect.equal row1.OkValue.FreeSeats 9 "should be equal"
            Expect.equal row2.OkValue.FreeSeats 9 "should be equal"

        // this test is to show wrong results in forcing parallel execution using repeated ids
        testCase "do in parallel two bookings on the same row using no id unique check so the result is ok but the resulting state is not correct - _NOT_ OK"  <| fun _ ->
            // preparation
            let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let row = { totalSeats = 10; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }
            let booking1 = { Id = Guid.NewGuid(); ClaimedSeats = 3; RowId = None}
            let booking2 = { Id = Guid.NewGuid(); ClaimedSeats = 1; RowId = None}
            let addRow = seatBookingService.AddRow row    
            let addBooking1 = seatBookingService.AddBooking booking1
            let addBooking2 = seatBookingService.AddBooking booking2
            
            // action
            let assignBookings = seatBookingService.ForceAssignBookings ([(booking1.Id, row.Id); (booking2.Id, row.Id)])

            // expectation
            Expect.isOk assignBookings "should be ok"

            let row = seatBookingService.GetRow row.Id
            Expect.isOk row "should be ok"
            Expect.equal row.OkValue.FreeSeats 9 "should be equal"

            let booking1 = seatBookingService.GetBooking booking1.Id
            Expect.isOk booking1 "should be ok"    
            Expect.equal booking1.OkValue.RowId (Some row.OkValue.Id) "should be equal"

            // both the bookings are associated to the row which is wrong but it what happens
            let booking2 = seatBookingService.GetBooking booking2.Id
            Expect.isOk booking2 "should be ok"
            Expect.equal booking2.OkValue.RowId (Some row.OkValue.Id) "should be equal"

        testCase "do in sequence two bookings on the same row using saga so the resulting state is correct - OK"  <| fun _ ->
            // preparation
            let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let row = { totalSeats = 10; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }
            let booking1 = { Id = Guid.NewGuid(); ClaimedSeats = 3; RowId = None}
            let booking2 = { Id = Guid.NewGuid(); ClaimedSeats = 1; RowId = None}
            let addRow = seatBookingService.AddRow row    
            let addBooking1 = seatBookingService.AddBooking booking1
            let addBooking2 = seatBookingService.AddBooking booking2
            
            // action
            let assignBookings = seatBookingService.AssignBookingUsingSagaWay ([(booking1.Id, row.Id); (booking2.Id, row.Id)])

            // expectation
            Expect.isOk assignBookings "should be ok"

            let row = seatBookingService.GetRow row.Id
            Expect.isOk row "should be ok"
            Expect.equal row.OkValue.FreeSeats 6 "should be equal"

            let booking1 = seatBookingService.GetBooking booking1.Id
            Expect.isOk booking1 "should be ok"    
            Expect.equal booking1.OkValue.RowId (Some row.OkValue.Id) "should be equal"

            let booking2 = seatBookingService.GetBooking booking2.Id
            Expect.isOk booking2 "should be ok"
            Expect.equal booking2.OkValue.RowId (Some row.OkValue.Id) "should be equal"
        
        testCase "do in sequence using saga way a transcation that will exceeds the available seats and so it will rollback - Ok" <| fun _ ->
            // preparation
            let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let row = { totalSeats = 10; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }
            let booking1 = { Id = Guid.NewGuid(); ClaimedSeats = 7; RowId = None}
            let booking2 = { Id = Guid.NewGuid(); ClaimedSeats = 4; RowId = None}
            let addRow = seatBookingService.AddRow row    
            let addBooking1 = seatBookingService.AddBooking booking1
            let addBooking2 = seatBookingService.AddBooking booking2

            // action
            let assignBookings = seatBookingService.AssignBookingUsingSagaWay ([(booking1.Id, row.Id); (booking2.Id, row.Id)])

            // expectation    
            Expect.isError assignBookings "should be error"
            let row = seatBookingService.GetRow row.Id
            Expect.isOk row "should be ok"
            Expect.equal row.OkValue.FreeSeats 10 "should be equal"

    ]
    |> testSequenced

