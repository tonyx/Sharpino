module Tests
open System
open Sharpino
open Sharpino.PgStorage
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
open Sharpino.Cache
open Sharpino.TestUtils
open DotNetEnv

Env.Load() |> ignore

let doNothingBroker: IEventBroker<_> =
    {
        notify = None
        notifyAggregate = None
    }

let password = Environment.GetEnvironmentVariable("Password"); 
let connection =
    "Server=127.0.0.1;"+
    "Database=sharpino_saga_sample;" +
    "User Id=safe;"+
    $"Password={password};"
let memoryStorage: IEventStore<_> = new MemoryStorage()
let dbEventStore:IEventStore<string> = PgEventStore(connection)

let teatherContextViewer = getStorageFreshStateViewer<Theater, TheaterEvents, string> memoryStorage 
let seatsAggregateViewer = fun id -> getAggregateFreshState<Row, RowEvents, string> id memoryStorage 
let bookingsAggregateViewer = fun id -> getAggregateFreshState<Booking, BookingEvents, string> id memoryStorage

let teatherContextdbViewer = getStorageFreshStateViewer<Theater, TheaterEvents, string> dbEventStore 
let seatsAggregatedbViewer = fun id -> getAggregateFreshState<Row, RowEvents, string> id dbEventStore 
let bookingsAggregatedbViewer = fun id -> getAggregateFreshState<Booking, BookingEvents, string> id dbEventStore


// put the password of db user safe in the .env file
// in the format
// Password=your_password


let setupDbEventStore =
    fun () ->
        let dbEventStore:IEventStore<string> = PgEventStore(connection)
        dbEventStore.Reset "_01" "_row"
        dbEventStore.ResetAggregateStream "_01" "_row"
        dbEventStore.Reset "_01" "_booking"
        dbEventStore.ResetAggregateStream "_01" "_booking"
        dbEventStore.Reset "_01" "_theater"
        StateCache<Theater>.Instance.Clear ()
        AggregateCache<Row, string>.Instance.Clear ()
        AggregateCache<Booking, string>.Instance.Clear
        ()
let setupMemoryStorage =
    fun () ->
        memoryStorage.Reset "_01" "_row"
        memoryStorage.ResetAggregateStream "_01" "_row"
        memoryStorage.Reset "_01" "_booking"
        memoryStorage.ResetAggregateStream "_01" "_booking"
        memoryStorage.Reset "_01" "_theater"
        StateCache<Theater>.Instance.Clear ()
        AggregateCache<Row, string>.Instance.Clear ()
        AggregateCache<Booking, string>.Instance.Clear
        ()    

let appVersionsEnvs =
    [
        (setupMemoryStorage, "memory db", fun () -> SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer))
        // (setupDbEventStore, "postgres db", fun () -> SeatBookingService(dbEventStore, doNothingBroker, teatherContextdbViewer, seatsAggregatedbViewer, bookingsAggregatedbViewer))
    ]

[<Tests>]
let tests =
    testList "samples" [
        multipleTestCase "fresh seat has zero rows (with parameters) - Ok" appVersionsEnvs <| fun (setup, _, service) ->
            setup()
            let service = service ()
        
            let rows = service.GetRows()
            Expect.isOk rows "should be ok"
            Expect.equal rows.OkValue.Length 0 "should be zero"

        ptestCase "fresh seat has zero rows - Ok" <| fun _ ->
            let dbEventStore:IEventStore<string> = PgEventStore(connection)
            dbEventStore.Reset "_01" "_row"
            dbEventStore.Reset "_01" "_booking"
            dbEventStore.ResetAggregateStream "_01" "_booking"
            dbEventStore.Reset "_01" "_theater"
        
            let seatBookingService = new SeatBookingService(dbEventStore, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let rows = seatBookingService.GetRows()
            Expect.isOk rows "should be ok"
            Expect.equal rows.OkValue.Length 0 "should be zero"
        
        multipleTestCase "seat service has zero rows - Ok" appVersionsEnvs <| fun (setup, _, service) ->
            setup()
            let seatBookingService = service ()
            
            let rows = seatBookingService.GetRows()
            Expect.isOk rows "should be ok"
            Expect.equal rows.OkValue.Length 0 "should be zero"

        multipleTestCase "seat service has zero booking - Ok" appVersionsEnvs <| fun (setup, _, service) ->
            setup()
            let seatBookingService = service ()
            let bookings = seatBookingService.GetBookings()
            Expect.isOk bookings "should be ok"
            Expect.equal bookings.OkValue.Length 0 "should be zero"

        multipleTestCase "add and retrieve a row - Ok" appVersionsEnvs <| fun (setup, dbinfo, service) ->
            let seatBookingService = service ()
            setup()
            let id = Guid.NewGuid()
            let row = { totalSeats = 10; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = id }
            dbEventStore.Reset "_01" "_theater"
            let addRow = seatBookingService.AddRow row 
            Expect.isOk addRow "should be ok"
            let rows = seatBookingService.GetRows ()
            Expect.isOk rows "should be ok"
            Expect.equal rows.OkValue.Length 1 "should be one"

        multipleTestCase "add and retrieve a booking - Ok" appVersionsEnvs <| fun (setup, _, service) ->
            setup()
            let seatBookingService = service ()
            let booking = { Id = Guid.NewGuid(); ClaimedSeats = 1; RowId = None}
            let addBooking = seatBookingService.AddBooking booking
            Expect.isOk addBooking "should be ok"
            let bookings = seatBookingService.GetBookings()
            Expect.isOk bookings "should be ok"
            Expect.equal bookings.OkValue.Length 1 "should be one"

        multipleTestCase "assign a booking to a row and verify that the booking has a rowId set to that rowId - Ok" appVersionsEnvs <| fun (setup, _, service) ->
            setup()
            let seatBookingService = service ()
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
        
        multipleTestCase "assign a booking to a row and verify that the row has the bookingId in the AssociatedBookings list - Ok" appVersionsEnvs <| fun (setup, _, service) ->
            setup()
            let seatBookingService = service ()
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

        multipleTestCase "assign a booking where the number of claimed seats is superior than the availability - Error" appVersionsEnvs <| fun (setup, _, service) ->
            setup()
            let seatBookingService = service ()
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

        multipleTestCase "in doing two consecutive bookings that succeeds, the number of free seats is the initial minus the sum of the claimed seats - Ok" appVersionsEnvs <| fun (setup, _, service) ->
            setup()
            let seatBookingService = service ()
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
        
        multipleTestCase "make two consecutive bookings, the second one exceeds the total and therefore it fails whereas the number of remaining seats is the initial minus the number related to the first booking - Ok" appVersionsEnvs <| fun (setup, _, service) ->
            setup()
            // preparation
            let seatBookingService = service ()
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
        
        multipleTestCase "can do in parallel two bookings on two different seats - OK" appVersionsEnvs  <| fun (setup, _, service) ->
            setup()
            // preparation
            // let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let seatBookingService = service ()
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
        
        multipleTestCase "do parallel bookings on two different seats whereas one of the booking can't succeed, so all the bookings must fails - Ok" appVersionsEnvs <| fun (setup, _, service) ->
            setup()
            // preparation
            let seatBookingService = service() 
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

        multipleTestCase "can't do in parallel two bookings on the same row - Error" appVersionsEnvs  <| fun (setup, _, service) ->
            setup()
            // preparation
            let seatBookingService = service ()
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

        multipleTestCase "do in parallel two bookings on two different seats using no different id checks, works as in the normal case - OK" appVersionsEnvs  <| fun (setup, _, service) ->
            setup ()
            // preparation
            let seatBookingService = service ()
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
        multipleTestCase "do in parallel two bookings on the same row using no id unique check so the result is ok but the resulting state is not correct - _NOT_ OK" appVersionsEnvs  <| fun (setup, _, service) ->
            setup ()
            // preparation
            let seatBookingService = service () 
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

        multipleTestCase "do in sequence two bookings on the same row using saga so the resulting state is correct - OK" appVersionsEnvs <| fun (setup, _, service) ->

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
        
        multipleTestCase "do in sequence using saga way a transaction that will exceeds the available seats and so it will rollback - Error" appVersionsEnvs <| fun (setup, _, service) ->
            setup ()
            // preparation
            let seatBookingService = service () 
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
            let booking1 = seatBookingService.GetBooking booking1.Id
            Expect.isOk booking1 "should be ok"
            Expect.equal booking1.OkValue.RowId None "should be equal"
            let booking2 = seatBookingService.GetBooking booking2.Id
            Expect.isOk booking2 "should be ok"    
            Expect.equal booking2.OkValue.RowId None "should be equal"

        multipleTestCase "a more generalized saga example - Ok" appVersionsEnvs <| fun (setup, _, service) ->
            setup ()
            service ()
            // preparation
            let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let row = { totalSeats = 20; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }
            let booking1 = { Id = Guid.NewGuid(); ClaimedSeats = 7; RowId = None}
            let booking2 = { Id = Guid.NewGuid(); ClaimedSeats = 7; RowId = None}
            let booking3 = { Id = Guid.NewGuid(); ClaimedSeats = 7; RowId = None}
            // let booking4 = { Id = Guid.NewGuid(); ClaimedSeats = 4; RowId = None}
            let addRow = seatBookingService.AddRow row    
            Expect.isOk addRow "should be ok" 
            let addBooking1 = seatBookingService.AddBooking booking1
            Expect.isOk addBooking1 "should be ok"
            let addBooking2 = seatBookingService.AddBooking booking2
            Expect.isOk addBooking2 "should be ok"
            let addBooking3 = seatBookingService.AddBooking booking3
            Expect.isOk addBooking3 "should be ok"

            // let addBooking4 = seatBookingService.AddBooking booking4
            // Expect.isOk addBooking4 "should be ok"

            // action 
            let assignBookings = 
                seatBookingService.AssignBookingUsingSagaWay 
                    [
                        (booking1.Id, row.Id);
                        (booking2.Id, row.Id);
                        (booking3.Id, row.Id);
                        // (booking4.Id, row.Id)
                    ]
                
            Expect.isError assignBookings "should be error"
            
            // expectation    
            let row = seatBookingService.GetRow row.Id
            Expect.isOk row "should be ok"
            Expect.equal row.OkValue.FreeSeats 20 "should be equal"

            let booking1 = seatBookingService.GetBooking booking1.Id
            Expect.isOk booking1 "should be ok"
            Expect.isNone booking1.OkValue.RowId "should be none"


        pmultipleTestCase "a more generalized saga example where compensation take place - Error" appVersionsEnvs <| fun (setup, _, service) ->
            setup ()
            
            // preparation

            let seatBookingService = service ()
            let row = { totalSeats = 20; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }
            let booking1 = { Id = Guid.NewGuid(); ClaimedSeats = 7; RowId = None}
            let booking2 = { Id = Guid.NewGuid(); ClaimedSeats = 7; RowId = None}
            let booking3 = { Id = Guid.NewGuid(); ClaimedSeats = 7; RowId = None}

            let addRow = seatBookingService.AddRow row    
            Expect.isOk addRow "should be ok" 
            let addBooking1 = seatBookingService.AddBooking booking1
            Expect.isOk addBooking1 "should be ok"
            let addBooking2 = seatBookingService.AddBooking booking2
            Expect.isOk addBooking2 "should be ok"

            let addBooking3 = seatBookingService.AddBooking booking3
            Expect.isOk addBooking3 "should be ok"


            // action 
            let assignBookings = 
                seatBookingService.AssignBookingUsingSagaWay 
                    [
                        (booking1.Id, row.Id);
                        (booking2.Id, row.Id);
                        (booking3.Id, row.Id)
                        // (booking4.Id, row.Id)
                    ]
                
            Expect.isError assignBookings "should be ok"
            
            // expectation    
            let row = seatBookingService.GetRow row.Id
            Expect.isOk row "should be ok"
            Expect.equal row.OkValue.FreeSeats 20 "should be equal"

            let booking1 = seatBookingService.GetBooking booking1.Id
            Expect.isOk booking1 "should be ok"

        testCase "add a row and then new seats to that row - Ok" <| fun _ ->
            memoryStorage.Reset "_01" "_seat"
            memoryStorage.ResetAggregateStream "_01" "_seat"
            memoryStorage.Reset "_01" "_booking"
            memoryStorage.ResetAggregateStream "_01" "_booking"
            memoryStorage.Reset "_01" "_theater"
            let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let row = { totalSeats = 20; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }
            let addRow = seatBookingService.AddRow row    
            Expect.isOk addRow "should be ok"
            let addSeats = seatBookingService.AddSeatsToRow (row.Id, 10)
            Expect.isOk addSeats "should be ok"
            let retrievedRow = seatBookingService.GetRow row.Id
            Expect.isOk retrievedRow "should be ok"
            Expect.equal retrievedRow.OkValue.FreeSeats 30 "should be equal"

        ftestCase "add a row and then remove seats from that row - Ok" <| fun _ ->
            memoryStorage.Reset "_01" "_seat"
            memoryStorage.ResetAggregateStream "_01" "_seat"
            memoryStorage.Reset "_01" "_booking"
            memoryStorage.ResetAggregateStream "_01" "_booking"
            memoryStorage.Reset "_01" "_theater"
            let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let row = { totalSeats = 20; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }
            let addRow = seatBookingService.AddRow row    
            Expect.isOk addRow "should be ok"
            let removeSeats = seatBookingService.RemoveSeatsFromRow (row.Id, 10)
            Expect.isOk removeSeats "should be ok"
            let retrievedRow = seatBookingService.GetRow row.Id
            Expect.isOk retrievedRow "should be ok"
            Expect.equal retrievedRow.OkValue.FreeSeats 10 "should be equal"
        
        testCase "add a row, then add a booking, then remove some of the seats that are left free - Ok" <| fun _ ->
            memoryStorage.Reset "_01" "_seat"
            memoryStorage.ResetAggregateStream "_01" "_seat"
            memoryStorage.Reset "_01" "_booking"
            memoryStorage.ResetAggregateStream "_01" "_booking"
            memoryStorage.Reset "_01" "_theater"
            let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let row = { totalSeats = 20; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }
            let addRow = seatBookingService.AddRow row    
            Expect.isOk addRow "should be ok"
            let booking = { Id = Guid.NewGuid(); ClaimedSeats = 10; RowId = None}
            let addBooking = seatBookingService.AddBooking booking
            Expect.isOk addBooking "should be ok"
            let bookingAssigned = seatBookingService.AssignBooking booking.Id row.Id 
            Expect.isOk bookingAssigned "should be ok"    
            let row = seatBookingService.GetRow row.Id
            Expect.isOk row "should be ok"
            Expect.equal row.OkValue.FreeSeats 10 "should be equal"

        pmultipleTestCase "add a row, then add a booking, try to remove more seats that available - Error" appVersionsEnvs  <| fun (setup, _, service) ->
            setup ()
            let seatBookingService = service ()
            let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let row = { totalSeats = 20; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }
            let addRow = seatBookingService.AddRow row    
            Expect.isOk addRow "should be ok"
            let booking = { Id = Guid.NewGuid(); ClaimedSeats = 10; RowId = None}
            let addBooking = seatBookingService.AddBooking booking
            Expect.isOk addBooking "should be ok"
            let bookingAssigned = seatBookingService.AssignBooking booking.Id row.Id 
            Expect.isOk bookingAssigned "should be ok"    
            let row = seatBookingService.GetRow row.Id
            Expect.isOk row "should be ok"
            Expect.equal row.OkValue.FreeSeats 10 "should be equal"
            let removeSeats = seatBookingService.RemoveSeatsFromRow (row.OkValue.Id, 11) 
            Expect.isError removeSeats "should be error"

            let reRetrieveRow = seatBookingService.GetRow row.OkValue.Id
            Expect.isOk reRetrieveRow "should be ok"
            Expect.equal reRetrieveRow.OkValue.FreeSeats 10 "should be equal"

        multipleTestCase "remove zero seats using saga like multicommand - Ok" appVersionsEnvs <| fun (setup, _, service) ->
            setup ()
            service ()
            
            let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let row1 = { totalSeats = 20; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }    
            let addRow1 = seatBookingService.AddRow row1
            Expect.isOk addRow1 "should be ok"
            let removeSeats = seatBookingService.RemoveSeatsFromRow (row1.Id, [1])
            Expect.isOk removeSeats "should be ok"

            let row = seatBookingService.GetRow row1.Id
            Expect.isOk row "should be ok"
            Expect.equal row.OkValue.FreeSeats 19 "should be equal"

        // focus
        multipleTestCase "remove three seats using saga like multicommand, two different removals, - Ok" appVersionsEnvs <| fun (setup, _, service) ->
            setup ()
            let seatBookingService = service ()
            
            let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let row1 = { totalSeats = 20; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }    
            let addRow1 = seatBookingService.AddRow row1
            Expect.isOk addRow1 "should be ok"
            let removeSeats = seatBookingService.RemoveSeatsFromRow (row1.Id, [1; 2])
            Expect.isOk removeSeats "should be ok"

            let row = seatBookingService.GetRow row1.Id
            Expect.isOk row "should be ok"
            Expect.equal row.OkValue.FreeSeats 17 "should be equal"
        // focus 
        multipleTestCase "trying to remove more seats than existing ones, one shot - Error" appVersionsEnvs <| fun (setup, _, service) ->
            setup ()    
            let seatBookingService = service ()
            let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let row = { totalSeats = 10; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }
            let addRow = seatBookingService.AddRow row
            Expect.isOk addRow "should be ok"

            let removeSeats = seatBookingService.RemoveSeatsFromRow (row.Id, [11])
            Expect.isError removeSeats "should be error"

            let retrieveRow = seatBookingService.GetRow row.Id
            Expect.isOk retrieveRow "should be ok"
            Expect.equal retrieveRow.OkValue.FreeSeats 10 "should be equal"

        // focus
        multipleTestCase "trying to remove more seats than existing ones, three shots - Error" appVersionsEnvs <| fun (setup, _, service) ->
            setup ()    
            let seatBookingService = service ()
            let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let row = { totalSeats = 10; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }
            let addRow = seatBookingService.AddRow row
            Expect.isOk addRow "should be ok"

            let removeSeats = seatBookingService.RemoveSeatsFromRow (row.Id, [4; 4; 3])
            Expect.isError removeSeats "should be error"

            let retrieveRow = seatBookingService.GetRow row.Id
            Expect.isOk retrieveRow "should be ok"
            Expect.equal retrieveRow.OkValue.FreeSeats 10 "should be equal"

        multipleTestCase "a more generalized saga example where compensation take place 2 - Error" appVersionsEnvs <| fun (setup, _, service) ->
            setup ()    
            let seatBookingService = service ()

            let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let row = { totalSeats = 20; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }
            let booking1 = { Id = Guid.NewGuid(); ClaimedSeats = 7; RowId = None}
            let booking2 = { Id = Guid.NewGuid(); ClaimedSeats = 7; RowId = None}
            let booking3 = { Id = Guid.NewGuid(); ClaimedSeats = 4; RowId = None}
            let booking4 = { Id = Guid.NewGuid(); ClaimedSeats = 3; RowId = None}
            let addRow = seatBookingService.AddRow row    
            Expect.isOk addRow "should be ok" 
            let addBooking1 = seatBookingService.AddBooking booking1
            Expect.isOk addBooking1 "should be ok"
            let addBooking2 = seatBookingService.AddBooking booking2
            Expect.isOk addBooking2 "should be ok"

            let addBooking3 = seatBookingService.AddBooking booking3
            Expect.isOk addBooking3 "should be ok"

            let addBooking4 = seatBookingService.AddBooking booking4
            Expect.isOk addBooking4 "should be ok"

            // action 
            let assignBookings = 
                seatBookingService.AssignBookingUsingSagaWay 
                    [
                        (booking1.Id, row.Id);
                        (booking2.Id, row.Id);
                        (booking3.Id, row.Id)
                        (booking4.Id, row.Id)
                    ]
                
            Expect.isError assignBookings "should be ok"
            
            // expectation    
            let row = seatBookingService.GetRow row.Id
            Expect.isOk row "should be ok"
            Expect.equal row.OkValue.FreeSeats 20 "should be equal"

            let booking1 = seatBookingService.GetBooking booking1.Id
            Expect.isOk booking1 "should be ok"

        fmultipleTestCase "a more generalized saga example of compensation 3 - Error" appVersionsEnvs <| fun (setup, _, service) ->
            setup ()
            let seatBookingService = service ()

            // let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let row = { totalSeats = 20; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }
            let booking1 = { Id = Guid.NewGuid(); ClaimedSeats = 7; RowId = None}
            let booking2 = { Id = Guid.NewGuid(); ClaimedSeats = 7; RowId = None}
            let booking3 = { Id = Guid.NewGuid(); ClaimedSeats = 7; RowId = None}
            let booking4 = { Id = Guid.NewGuid(); ClaimedSeats = 1; RowId = None}
            let addRow = seatBookingService.AddRow row    
            Expect.isOk addRow "should be ok" 
            let addBooking1 = seatBookingService.AddBooking booking1
            Expect.isOk addBooking1 "should be ok"
            let addBooking2 = seatBookingService.AddBooking booking2
            Expect.isOk addBooking2 "should be ok"

            let addBooking3 = seatBookingService.AddBooking booking3
            Expect.isOk addBooking3 "should be ok"

            let addBooking4 = seatBookingService.AddBooking booking4
            Expect.isOk addBooking4 "should be ok"

            // action 
            let assignBookings = 
                seatBookingService.AssignBookingUsingSagaWay 
                    [
                        (booking1.Id, row.Id);
                        (booking2.Id, row.Id);
                        (booking3.Id, row.Id)
                        (booking4.Id, row.Id)
                    ]
                
            Expect.isError assignBookings "should be ok"
            
            // expectation    
            let row = seatBookingService.GetRow row.Id
            Expect.isOk row "should be ok"
            Expect.equal row.OkValue.FreeSeats 20 "should be equal"

            let booking1 = seatBookingService.GetBooking booking1.Id
            Expect.isOk booking1 "should be ok"

        testCase "a more generalized saga example of compensation 4 - Error" <| fun _ ->
            // preparation
            memoryStorage.Reset "_01" "_seat"
            memoryStorage.ResetAggregateStream "_01" "_seat"
            memoryStorage.Reset "_01" "_booking"
            memoryStorage.ResetAggregateStream "_01" "_booking"

            memoryStorage.Reset "_01" "_theater"

            let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let row = { totalSeats = 20; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }
            let booking1 = { Id = Guid.NewGuid(); ClaimedSeats = 21; RowId = None}
            let booking2 = { Id = Guid.NewGuid(); ClaimedSeats = 76576; RowId = None}
            let booking3 = { Id = Guid.NewGuid(); ClaimedSeats = 887; RowId = None}
            let booking4 = { Id = Guid.NewGuid(); ClaimedSeats = 76765765; RowId = None}
            let addRow = seatBookingService.AddRow row    
            Expect.isOk addRow "should be ok" 
            let addBooking1 = seatBookingService.AddBooking booking1
            Expect.isOk addBooking1 "should be ok"
            let addBooking2 = seatBookingService.AddBooking booking2
            Expect.isOk addBooking2 "should be ok"

            let addBooking3 = seatBookingService.AddBooking booking3
            Expect.isOk addBooking3 "should be ok"

            let addBooking4 = seatBookingService.AddBooking booking4
            Expect.isOk addBooking4 "should be ok"

            // action 
            let assignBookings = 
                seatBookingService.AssignBookingUsingSagaWay 
                    [
                        (booking1.Id, row.Id);
                        (booking2.Id, row.Id);
                        (booking3.Id, row.Id)
                        (booking4.Id, row.Id)
                    ]
                
            Expect.isError assignBookings "should be ok"
            
            // expectation    
            let row = seatBookingService.GetRow row.Id
            Expect.isOk row "should be ok"
            Expect.equal row.OkValue.FreeSeats 20 "should be equal"

            let booking1 = seatBookingService.GetBooking booking1.Id
            Expect.isOk booking1 "should be ok"

        testCase "a more general saga example of compensation 5 - Error" <| fun _ ->
            // preparation
            memoryStorage.Reset "_01" "_seat"
            memoryStorage.ResetAggregateStream "_01" "_seat"
            memoryStorage.Reset "_01" "_booking"
            memoryStorage.ResetAggregateStream "_01" "_booking"

            memoryStorage.Reset "_01" "_theater"

            let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let row = { totalSeats = 20; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }
            let booking1 = { Id = Guid.NewGuid(); ClaimedSeats = 5; RowId = None}
            let booking2 = { Id = Guid.NewGuid(); ClaimedSeats = 16; RowId = None}
            let booking3 = { Id = Guid.NewGuid(); ClaimedSeats = 887; RowId = None}
            let booking4 = { Id = Guid.NewGuid(); ClaimedSeats = 76765765; RowId = None}
            let addRow = seatBookingService.AddRow row    
            Expect.isOk addRow "should be ok" 
            let addBooking1 = seatBookingService.AddBooking booking1
            Expect.isOk addBooking1 "should be ok"
            let addBooking2 = seatBookingService.AddBooking booking2
            Expect.isOk addBooking2 "should be ok"

            let addBooking3 = seatBookingService.AddBooking booking3
            Expect.isOk addBooking3 "should be ok"

            let addBooking4 = seatBookingService.AddBooking booking4
            Expect.isOk addBooking4 "should be ok"

            // action 
            let assignBookings = 
                seatBookingService.AssignBookingUsingSagaWay 
                    [
                        (booking1.Id, row.Id);
                        (booking2.Id, row.Id);
                        (booking3.Id, row.Id)
                        (booking4.Id, row.Id)
                    ]
                
            Expect.isError assignBookings "should be ok"
            
            // expectation    
            let row = seatBookingService.GetRow row.Id
            Expect.isOk row "should be ok"
            Expect.equal row.OkValue.FreeSeats 20 "should be equal"

            let booking1 = seatBookingService.GetBooking booking1.Id
            Expect.isOk booking1 "should be ok"

        testCase "a more general saga example of compensation 6 - Error" <| fun _ ->
            // preparation
            memoryStorage.Reset "_01" "_seat"
            memoryStorage.ResetAggregateStream "_01" "_seat"
            memoryStorage.Reset "_01" "_booking"
            memoryStorage.ResetAggregateStream "_01" "_booking"

            memoryStorage.Reset "_01" "_theater"

            let seatBookingService = new SeatBookingService(memoryStorage, doNothingBroker, teatherContextViewer, seatsAggregateViewer, bookingsAggregateViewer)
            let row = { totalSeats = 20; numberOfSeatsBooked = 0; AssociatedBookings = []; Id = Guid.NewGuid() }
            let bookings = 
                [
                    { Id = Guid.NewGuid(); ClaimedSeats = 1; RowId = None}
                    { Id = Guid.NewGuid(); ClaimedSeats = 1; RowId = None}
                    { Id = Guid.NewGuid(); ClaimedSeats = 1; RowId = None}
                    { Id = Guid.NewGuid(); ClaimedSeats = 1; RowId = None}
                    { Id = Guid.NewGuid(); ClaimedSeats = 2; RowId = None}
                    { Id = Guid.NewGuid(); ClaimedSeats = 2; RowId = None}
                    { Id = Guid.NewGuid(); ClaimedSeats = 2; RowId = None}
                    { Id = Guid.NewGuid(); ClaimedSeats = 2; RowId = None}
                    { Id = Guid.NewGuid(); ClaimedSeats = 1; RowId = None}
                    { Id = Guid.NewGuid(); ClaimedSeats = 8; RowId = None}
                    { Id = Guid.NewGuid(); ClaimedSeats = 1; RowId = None}
                    { Id = Guid.NewGuid(); ClaimedSeats = 1; RowId = None}
                    { Id = Guid.NewGuid(); ClaimedSeats = 1; RowId = None}
                ]

            let addRow = seatBookingService.AddRow row    
            Expect.isOk addRow "should be ok" 

            let addAllBookings =
                bookings
                |> List.map (fun b -> seatBookingService.AddBooking b)
                |> List.map (fun r -> Expect.isOk r "should be ok")

            let assignBookings = 
                seatBookingService.AssignBookingUsingSagaWay 
                    (bookings |> List.map (fun b -> (b.Id, row.Id)))
                
            Expect.isError assignBookings "should be ok"
            
            // expectation    
            let row = seatBookingService.GetRow row.Id
            Expect.isOk row "should be ok"
            Expect.equal row.OkValue.FreeSeats 20 "should be equal"

            let booking1 = seatBookingService.GetBooking bookings.[0].Id
            Expect.isOk booking1 "should be ok"
            Expect.isNone booking1.OkValue.RowId "should be none"
    ]
    |> testSequenced

