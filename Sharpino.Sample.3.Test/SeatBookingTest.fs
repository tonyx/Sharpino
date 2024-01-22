module Tests

open seatsLockWithSharpino.Row1Context
open seatsLockWithSharpino.RefactoredRow
open seatsLockWithSharpino.Seats
open seatsLockWithSharpino
open seatsLockWithSharpino.Row
open seatsLockWithSharpino.Row1
open seatsLockWithSharpino.Row2
open FsToolkit.ErrorHandling
open Expecto
open Sharpino
open FSharpPlus.Operators
open Sharpino.MemoryStorage
open seatsLockWithSharpino.App
open Sharpino.Storage
open Sharpino.Cache
open Sharpino.Core
open Sharpino
open System
open seatsLockWithSharpino
open seatsLockWithSharpino.Stadium
open Sharpino.Utils
open Sharpino.StateView
open Sharpino.CommandHandler

[<Tests>]
let hackingEventInStorageTest =
    let serializer = new Utils.JsonSerializer(Utils.serSettings) :> Utils.ISerializer
    testList "hacks the events in the storage to make sure that invalid events will be skipped, and concurrency cannot end up in invariant rule violation " [
        testCase "evolve unforginv errors: second booking is not accapted - Error" <| fun _ ->
            let firstBookingOfFirstTwoSeats =  { id = 1; seats = [1; 2] }
            let thirdBookingOfLastTwoSeats = { id = 3; seats = [4; 5] }

            let bookingEvent1 = Row1Events.SeatsBooked firstBookingOfFirstTwoSeats 
            let bookingEvent3 = Row1Events.SeatsBooked thirdBookingOfLastTwoSeats

            let evolved =
                [ bookingEvent1
                  bookingEvent3
                ]
                |> evolveUNforgivingErrors<Row1, Row1Events.Row1Events> Row1.Zero
            Expect.isError evolved "should be equal"

        testCase "evolve unforgiving errors: one seat reservation is not accepted  - Error" <| fun _ ->
            let firstBookingOfFirstTwoSeats =  { id = 1; seats = [1; 2] }
            let secondBookingOfLastTwoSeats = { id = 2; seats = [4; 5] }
            let thirdBookingOfLastTwoSeats = { id = 3; seats = [4] }

            let bookingEvent1 = Row1Events.SeatsBooked firstBookingOfFirstTwoSeats 
            let bookingEvent2 = Row1Events.SeatsBooked secondBookingOfLastTwoSeats
            let bookingEvent3 = Row1Events.SeatsBooked thirdBookingOfLastTwoSeats

            let evolved =
                [ bookingEvent1
                  bookingEvent2
                  bookingEvent3
                ]
                |> evolveUNforgivingErrors<Row1, Row1Events.Row1Events> Row1.Zero
            Expect.isError evolved "should be equal"

        testCase "evolve forgiving errors: one seat reservation is not accepted, whereas the others are accepted  - Error" <| fun _ ->
            let firstBookingOfFirstTwoSeats =  { id = 1; seats = [1; 2] }
            let secondBookingOfLastTwoSeats = { id = 2; seats = [4; 5] }
            let thirdBookingOfLastTwoSeats = { id = 3; seats = [4] }

            let bookingEvent1 = Row1Events.SeatsBooked firstBookingOfFirstTwoSeats 
            let bookingEvent2 = Row1Events.SeatsBooked secondBookingOfLastTwoSeats
            let bookingEvent3 = Row1Events.SeatsBooked thirdBookingOfLastTwoSeats

            let evolved =
                [ bookingEvent1
                  bookingEvent2
                  bookingEvent3
                ]
                |> evolve<Row1, Row1Events.Row1Events> Row1.Zero
            Expect.isOk evolved "should be equal"
            let remainingSeats = (evolved.OkValue).GetAvailableSeats() 
            Expect.equal remainingSeats.Length 2 "should be equal"

        testCase "process events skippin second one which is inconsistent - Ok" <| fun _ ->
            let storage = MemoryStorage()
            StateCache<Row1>.Instance.Clear()
            StateCache<Row2Context.Row2>.Instance.Clear()
            let app = App(storage)
            let availableSeats = app.GetAllAvailableSeats() |> Result.get
            Expect.equal availableSeats.Length 10 "should be equal"
            
            let firstBookingOfFirstTwoSeats =  { id = 1; seats = [1; 2] }
            let secondBookingOfLastTwoSeats = { id = 2; seats = [4; 5] }
            let thirdBookingOfLastTwoSeats = { id = 3; seats = [4] }

            let bookingEvent1 = Row1Events.SeatsBooked firstBookingOfFirstTwoSeats |> serializer.Serialize
            let bookingEvent2 = Row1Events.SeatsBooked secondBookingOfLastTwoSeats |> serializer.Serialize
            let bookingEvent3 = Row1Events.SeatsBooked thirdBookingOfLastTwoSeats |> serializer.Serialize
            let eventsAdded = (storage :> IEventStore).AddEvents Row1Context.Row1.Version Row1Context.Row1.StorageName [bookingEvent1; bookingEvent2; bookingEvent3] 
            Expect.isOk eventsAdded "should be equal"
            let availableSeats = app.GetAllAvailableSeats() |> Result.get
            Expect.equal availableSeats.Length 7 "should be equal"

        testCase "add a booking event in the storage and show the result by the app - Ok" <| fun _ ->
            let storage = MemoryStorage()
            StateCache<Row1>.Instance.Clear()
            StateCache<Row2Context.Row2>.Instance.Clear()
            let app = App(storage)
            let availableSeats = app.GetAllAvailableSeats() |> Result.get
            Expect.equal availableSeats.Length 10 "should be equal"
            let seatOneBooking = { id = 1; seats = [1] }
            let bookingEvent = Row1Events.SeatsBooked seatOneBooking
            let serializedEvent = bookingEvent.Serialize serializer
            let eventsAdded = (storage :> IEventStore).AddEvents Row1Context.Row1.Version Row1Context.Row1.StorageName [serializedEvent]
            Expect.isOk eventsAdded "should be equal"
            let availableSeats = app.GetAllAvailableSeats() |> Result.get
            Expect.equal availableSeats.Length 9 "should be equal"
        
        testCase "try add a single event that violates the middle row seat invariant rule - Ok" <| fun _ ->
            let storage = MemoryStorage()
            StateCache<Row1>.Instance.Clear()
            StateCache<Row2Context.Row2>.Instance.Clear()
            let app = App(storage)
            let availableSeats = app.GetAllAvailableSeats() |> Result.get
            Expect.equal availableSeats.Length 10 "should be equal"
            
            let invalidBookingViolatesInvariant = { id = 1; seats = [1; 2; 4; 5] }
            
            let bookingEvent = Row1Events.SeatsBooked invalidBookingViolatesInvariant
            let serializedEvent = bookingEvent.Serialize serializer 
            let eventsAdded = (storage :> IEventStore).AddEvents Row1Context.Row1.Version Row1Context.Row1.StorageName [serializedEvent] 
            Expect.isOk eventsAdded "should be equal"
            let availableSeats = app.GetAllAvailableSeats() |> Result.get
            Expect.equal availableSeats.Length 10 "should be equal"

        // this example simulates when one event that is not supposed to be added is added anyway because is processed
        // in parallel 
        testCase "try add two events where one of those violates the middle chair invariant rule. Only one of those can be processed - Ok" <| fun _ ->
            let storage = MemoryStorage()
            StateCache<Row1>.Instance.Clear()
            StateCache<Row2Context.Row2>.Instance.Clear()
            let app = App(storage)
            let availableSeats = app.GetAllAvailableSeats() |> Result.get
            Expect.equal availableSeats.Length 10 "should be equal"
            
            let firstBookingOfFirstTwoSeats =  { id = 1; seats = [1; 2] }
            let secondBookingOfLastTwoSeats = { id = 2; seats = [4; 5] }
            
            let booking1 = (Row1Events.SeatsBooked firstBookingOfFirstTwoSeats).Serialize  serializer
            let booking2 = (Row1Events.SeatsBooked secondBookingOfLastTwoSeats).Serialize serializer
            
            let eventsAdded = (storage :> IEventStore).AddEvents Row1Context.Row1.Version Row1Context.Row1.StorageName [booking1; booking2] 
            Expect.isOk eventsAdded "should be equal"
            let availableSeats = app.GetAllAvailableSeats() |> Result.get
            Expect.equal availableSeats.Length 8 "should be equal"

        testCase "store events that books seat on the left and two seat on the right of the row 1, so they are both valid and there are 7 seats left free  - Ok" <| fun _ ->
            let storage = MemoryStorage()
            StateCache<Row1>.Instance.Clear()
            StateCache<Row2Context.Row2>.Instance.Clear()
            let app = App(storage)
            let availableSeats = app.GetAllAvailableSeats() |> Result.get
            Expect.equal availableSeats.Length 10 "should be equal"
            
            let firstBookingOfFirstSeats =  { id = 1; seats = [1] }
            let secondBookingOfLastTwoSeats = { id = 2; seats = [4; 5] }
            
            let booking1 = (Row1Events.SeatsBooked firstBookingOfFirstSeats).Serialize  serializer
            let booking2 = (Row1Events.SeatsBooked secondBookingOfLastTwoSeats).Serialize serializer
            
            let eventsAdded = (storage :> IEventStore).AddEvents Row1Context.Row1.Version Row1Context.Row1.StorageName [booking1; booking2]
            Expect.isOk eventsAdded "should be equal"
            let availableSeats = app.GetAllAvailableSeats() |> Result.get
            Expect.equal availableSeats.Length 7 "should be equal"

        testCase "store three single booking events that end up in a valid state - Ok" <| fun _ ->
            let storage = MemoryStorage()
            StateCache<Row1>.Instance.Clear()
            StateCache<Row2Context.Row2>.Instance.Clear()
            let app = App(storage)
            let availableSeats = app.GetAllAvailableSeats() |> Result.get
            Expect.equal availableSeats.Length 10 "should be equal"
            let firstBooking =  { id = 1; seats = [1] }
            let secondBooking = { id = 2; seats = [2] }
            let thirdBooking = { id = 3; seats = [3] }

            let bookingEvent1 = (Row1Events.SeatsBooked firstBooking).Serialize  serializer
            let bookingEvent2 = (Row1Events.SeatsBooked secondBooking).Serialize serializer
            let bookingEvent3 = (Row1Events.SeatsBooked thirdBooking).Serialize serializer
            let eventsAdded = (storage :> IEventStore).AddEvents Row1Context.Row1.Version Row1Context.Row1.StorageName [bookingEvent1; bookingEvent2; bookingEvent3]
            Expect.isOk eventsAdded "should be equal"
            let availableSeats = app.GetAllAvailableSeats() |> Result.get
            Expect.equal availableSeats.Length 7 "should be equal"
            
    ]
    |> testSequenced
    
[<Tests>]
let tests =
    testList "singleRows tests" [

        testCase "all seats of the first row are free - Ok" <| fun _ ->
            let currentSeats = Row1.Zero
            let availableSeats = currentSeats.GetAvailableSeats()
            Expect.equal availableSeats.Length 5 "should be equal"

        testCase "cannot leave the only central 3 seat free - Error" <| fun _ ->
            let currentSeats = Row1.Zero
            let booking = { id = 1; seats = [1; 2; 4; 5] }
            let reservedSeats = currentSeats.BookSeats booking
            Expect.isError reservedSeats "should be equal"
            
        testCase "can leave the two central seats 2 and 3 free - Ok" <| fun _ ->
            let currentSeats = Row1.Zero
            let booking = { id = 1; seats = [1; 4; 5] }
            let reservedSeats = currentSeats.BookSeats booking
            Expect.isOk reservedSeats "should be equal"

        testCase "book a single seat from the first row - Ok" <| fun _ ->
            let booking = { id = 1; seats = [1] }
            let row1WithOneSeatBooked = Row1.Zero.BookSeats booking |> Result.get
            let availableSeats = row1WithOneSeatBooked.GetAvailableSeats()
            Expect.equal availableSeats.Length 4 "should be equal"

        testCase "book a single seat from the second row - Ok" <| fun _ ->
            let booking = { id = 2; seats = [6] }
            let row2Context = RowContext(row2Seats)
            let row2WithOneSeatBooked = row2Context.BookSeats booking |> Result.get
            let availables = row2WithOneSeatBooked.GetAvailableSeats()
            Expect.equal availables.Length 4 "should be equal"

        testCase "book a seat that is already booked - Error" <| fun _ ->
            let booking = { id = 1; seats = [1] }
            let row1WithOneSeatBooked = Row1.Zero.BookSeats booking |> Result.get
            Expect.isFalse (row1WithOneSeatBooked.IsAvailable 1) "should be equal"
            let newBooking = { id = 1; seats = [1] }
            let reservedSeats' = row1WithOneSeatBooked.BookSeats newBooking 
            Expect.isError reservedSeats' "should be equal"

        testCase "book five seats - Ok" <| fun _ ->
            let booking = { id = 1; seats = [1;2;3;4;5] }
            let row1FullyBooked = Row1.Zero.BookSeats booking |> Result.get
            let availableSeats = row1FullyBooked.GetAvailableSeats()    
            Expect.equal availableSeats.Length 0 "should be equal"
    ]
    |> 
    testSequenced

[<Tests>]
let apiTests =
    testList "test api level (multi-rows) tests" [
        testCase "book seats affecting first and second row - Ok" <| fun _ ->
            // setup
            let storage = MemoryStorage()
            StateCache<Row1>.Instance.Clear()
            StateCache<Row2Context.Row2>.Instance.Clear()
            let app = App(storage)

            let booking = { id = 1; seats = [3;7] }
            let booked = app.BookSeats booking 
            Expect.isOk booked "should be equal"
            let available = app.GetAllAvailableSeats() |> Result.get
            Expect.equal available.Length 8 "should be equal"

            Expect.equal (available |> Set.ofList) ([1;2;4;5;6;8;9;10] |> Set.ofList) "should be equal"

        testCase "book seats affecting only the first row - Ok" <| fun _ ->
            // setup
            let storage = MemoryStorage()
            StateCache<Row1>.Instance.Clear()
            StateCache<Row2Context.Row2>.Instance.Clear()

            let app = App(storage)
            let booking = { id = 1; seats = [1;2;3;4;5] }
            let booked = app.BookSeats booking
            Expect.isOk booked "should be equal"

        testCase "book all seats on row1 - Ok" <| fun _ ->

            let storage = MemoryStorage()
            StateCache<Row1>.Instance.Clear()
            StateCache<Row2Context.Row2>.Instance.Clear()

            let app = App(storage)
            let booking1 = { id = 1; seats = [1;2;3;4;5] }
            let booked = app.BookSeats booking1 
            Expect.isOk booked "should be equal"
            let availableSeats = app.GetAllAvailableSeats() |> Result.get
            Expect.equal availableSeats.Length 5 "should be equal"
            Expect.equal (availableSeats |> Set.ofList) ([6;7;8;9;10] |> Set.ofList) "should be equal"

        testCase "book all row2 - Ok" <| fun _ ->
            let storage = MemoryStorage()
            StateCache<Row1>.Instance.Clear()
            StateCache<Row2Context.Row2>.Instance.Clear()
            let app = App(storage)
            let booking2 = { id = 2; seats = [6;7;8;9;10] }
            let booked = app.BookSeats booking2 
            Expect.isOk booked "should be equal"
            let availableSeats = app.GetAllAvailableSeats() |> Result.get
            Expect.equal availableSeats.Length 5 "should be equal"
            Expect.equal (availableSeats |> Set.ofList) ([1;2;3;4;5] |> Set.ofList) "should be equal"

        testCase "book only one seat at row2 " <| fun _ ->
            let storage = MemoryStorage()
            StateCache<Row1>.Instance.Clear()
            StateCache<Row2Context.Row2>.Instance.Clear()
            let app = App(storage)
            let booking2 = { id = 2; seats = [6] }
            let booked = app.BookSeats booking2 
            Expect.isOk booked "should be equal"
            let availableSeats = app.GetAllAvailableSeats() |> Result.get
            Expect.equal (availableSeats |> Set.ofList) ([1;2;3;4;5;7;8;9;10] |> Set.ofList) "should be equal"

        testCase "book only one seat at row1 " <| fun _ ->
            let storage = MemoryStorage()
            StateCache<Row1>.Instance.Clear()
            StateCache<Row2Context.Row2>.Instance.Clear()
            let app = App(storage)
            let booking = { id = 2; seats = [1] }
            let booked = app.BookSeats booking 
            Expect.isOk booked "should be equal"
            let availableSeats = app.GetAllAvailableSeats() |> Result.get
            Expect.equal (availableSeats |> Set.ofList) ([2;3;4;5;6;7;8;9;10] |> Set.ofList) "should be equal"

        testCase "book seats partial row2 - Ok" <| fun _ ->
            let storage = MemoryStorage()
            StateCache<Row1>.Instance.Clear()
            StateCache<Row2Context.Row2>.Instance.Clear()
            let app = App(storage)
            let booking = { id = 2; seats = [6;7] }
            let booked = app.BookSeats booking 
            Expect.isOk booked "should be equal"
            let availableSeats = app.GetAllAvailableSeats() |> Result.get
            Expect.equal availableSeats.Length 8 "should be equal"
            Expect.equal (availableSeats |> Set.ofList) ([1;2;3;4;5;8;9;10] |> Set.ofList) "should be equal"

        testCase "book a seat that is already taken - Error " <| fun _ ->
            let storage = MemoryStorage()
            StateCache<Row1>.Instance.Clear()
            StateCache<Row2Context.Row2>.Instance.Clear()
            let app = App(storage)
            let booking = { id = 1; seats = [6] }
            let booked = app.BookSeats booking 
            Expect.isOk booked "should be equal"
            let booking2 = { id = 2; seats = [6] }
            let booked2 = app.BookSeats booking2
            Expect.isError booked2 "should be equal"

        testCase "try do a booking containing a seat that is already taken - Error " <| fun _ ->
            let storage = MemoryStorage()
            StateCache<Row1>.Instance.Clear()
            StateCache<Row2Context.Row2>.Instance.Clear()
            let app = App(storage)
            let booking = { id = 1; seats = [6] }
            let booked = app.BookSeats booking 
            Expect.isOk booked "should be equal"
            let booking2 = { id = 2; seats = [6; 7] }
            let booked2 = app.BookSeats booking2
            Expect.isError booked2 "should be equal"

        testCase "reserve places related to the second row - Ok" <| fun _ ->
            let storage = MemoryStorage()
            StateCache<Row1>.Instance.Clear()
            StateCache<Row2Context.Row2>.Instance.Clear()
            let app = App(storage)
            let booking  = { id = 3; seats = [6;7;8;9;10] }
            let booked = app.BookSeats booking
            Expect.isOk booked "should be equal"

        testCase "no bookings, all seats are available" <| fun _ ->
            let storage = MemoryStorage()
            StateCache<Row1>.Instance.Clear()
            StateCache<Row2Context.Row2>.Instance.Clear()

            let app = App(storage)
            let availableSeats = app.GetAllAvailableSeats() |> Result.get
            Expect.equal availableSeats.Length 10 "should be equal"

        testCase "can't leave the single seat free in the middle - Error" <| fun _ ->
            let storage = MemoryStorage()
            StateCache<Row1>.Instance.Clear()
            StateCache<Row2Context.Row2>.Instance.Clear()
            let app = App(storage)
            let booking  = { id = 3; seats = [6;7;9;10] }
            let booked = app.BookSeats booking
            Expect.isError booked "should be equal"
            let availableSeats = app.GetAllAvailableSeats() |> Result.get
            Expect.equal availableSeats.Length 10 "should be equal"

        testCase "try book already booked in first row - Error" <| fun _ -> 
            let storage = MemoryStorage()
            StateCache<Row1>.Instance.Clear()
            StateCache<Row2Context.Row2>.Instance.Clear()

            let app = App(storage)
            let row1FreeSeats = app.GetAllAvailableSeats() |> Result.get
            Expect.equal row1FreeSeats.Length 10 "should be equal"
            let booking =  { id = 1; seats = [1;2;3;4;5] }
            let booked = app.BookSeats booking
            Expect.isOk booked "should be equal"
            let availableSeats = app.GetAllAvailableSeats() |> Result.get

            Expect.isTrue (availableSeats |> List.contains 6) "should be equal" 
            Expect.isFalse (availableSeats |> List.contains 1) "should be equal" 
            let booking2 = { id = 2; seats = [1] }
            let booked2 = app.BookSeats booking2
            Expect.isError booked2 "should be equal"

        testCase "reserve places related to already booked only in the second row and so no place is booked at all - Error" <| fun _ ->
            let storage = MemoryStorage()
            StateCache<Row1>.Instance.Clear()
            StateCache<Row2Context.Row2>.Instance.Clear()

            let app = App(storage)
            let booking1 = { id = 1; seats = [1;2;3;4;5] }
            let booked = app.BookSeats booking1
            Expect.isOk booked "should be equal"

            let booking2 =  { id = 3; seats = [1; 6; 7; 8; 9; 10]}
            let newBooking = app.BookSeats booking2
            Expect.isError newBooking "should be equal"

            let booking3 = { id = 6; seats = [6;7;8;9;10]}
            let newBooking2 = app.BookSeats booking3
            Expect.isOk newBooking2 "should be equal"

        testCase "try booking seats on many rows, expecting that if one of them is not available than none of them is booked - Error" <| fun _ ->
            let storage = MemoryStorage()
            StateCache<Row1>.Instance.Clear()
            StateCache<Row2Context.Row2>.Instance.Clear()

            let app = App(storage)
            let booking1 = { id = 1; seats = [1;2;3;4;5] }
            let booked = app.BookSeats booking1
            Expect.isOk booked "should be equal"

            let booking2 =  { id = 3; seats = [1; 6; 7; 8; 9; 10]}
            let newBooking = app.BookSeats booking2
            Expect.isError newBooking "should be equal"

            let booking3 = { id = 6; seats = [6;7;8;9;10]}
            let newBooking2 = app.BookSeats booking3
            Expect.isOk newBooking2 "should be equal"
    ] 
    |> testSequenced
    
[<Tests>]
let refactorAggregateTests =
    let connection = 
        "Server=127.0.0.1;"+
        "Database=es_seat_booking;" +
        "User Id=safe;"+
        "Password=safe;"
    let serializer = new Utils.JsonSerializer(Utils.serSettings) :> Utils.ISerializer
    testList "test the evolve about refactored aggregate  " [
        testCase "add a single seat - Ok" <|  fun _ ->
            let seat = { id = 1; State = Free }
            let row = RefactoredRow (Guid.NewGuid())
            let rowWithSeat = row.AddSeat seat |> Result.get
            let seats = rowWithSeat.Seats
            Expect.equal seats.Length 1 "should be equal"

        testCase "add twice the same seat - Error" <| fun _ ->
            let seat = { id = 1; State = Free }
            let row = RefactoredRow (Guid.NewGuid())
            let rowWithSeat = row.AddSeat seat |> Result.get
            let seats = rowWithSeat.Seats
            Expect.equal seats.Length 1 "should be equal"
            let addSeatAgain = rowWithSeat.AddSeat seat
            Expect.isError addSeatAgain "should be equal"
            
        testCase "add a single seat 2 -  Ok" <|  fun _ ->
            let seat = { id = 1; State = Free }
            let row = RefactoredRow (Guid.NewGuid())
            let rowWithSeat = row.AddSeat seat |> Result.get
            let seats = rowWithSeat.GetAvailableSeats()
            Expect.equal seats.Length 1 "should be equal"

        testCase "add a group of one seat -  Ok" <|  fun _ ->
            let seat = { id = 1; State = Free }
            let row = RefactoredRow (Guid.NewGuid())
            let rowWithSeat = row.AddSeats [seat] |> Result.get
            let seats = rowWithSeat.Seats
            Expect.equal seats.Length 1 "should be equal"

        testCase "add a group of one seat 2 -  Ok" <|  fun _ ->
            let seat = { id = 1; State = Free }
            let row = RefactoredRow (Guid.NewGuid())
            let rowWithSeat = row.AddSeats [seat] |> Result.get
            let seats = rowWithSeat.GetAvailableSeats()
            Expect.equal seats.Length 1 "should be equal"

        testCase "available seats  are all seats - OK" <| fun _ ->
            let seats = 
                [ { id = 1; State = Free }
                  { id = 2; State = Free }
                  { id = 3; State = Free }
                  { id = 4; State = Free }
                  { id = 5; State = Free }
                ]
            let row = RefactoredRow (Guid.NewGuid())
            let rowWithSeats = row.AddSeats seats |> Result.get
            let result = rowWithSeats.GetAvailableSeats()
            printf "%A" result
            Expect.equal result.Length 5 "should be equal"
        
        testCase "book one seat - Ok" <| fun _ ->
            let seats = 
                [ { id = 1; State = Free }
                  { id = 2; State = Free }
                  { id = 3; State = Free }
                  { id = 4; State = Free }
                  { id = 5; State = Free }
                ]

            let row = RefactoredRow (Guid.NewGuid())
            let row' = row.AddSeats seats |> Result.get
            let booking = { id = 1; seats = [1] }
            let booked = row'.BookSeats booking |> Result.get
            let availableSeats = booked.GetAvailableSeats()
            Expect.equal availableSeats.Length 4 "should be equal"
        
        testCase "create a booking event and process it - Ok" <| fun _ ->
            let seats = 
                [ { id = 1; State = Free }
                  { id = 2; State = Free }
                  { id = 3; State = Free }
                  { id = 4; State = Free }
                  { id = 5; State = Free }
                ]
            let row = RefactoredRow (Guid.NewGuid())
            let row' = row.AddSeats seats |> Result.get
            let booking = { id = 1; seats = [1] }
            let bookingEvent = RowAggregateEvent.SeatBooked booking
            let rowAfterBooking =  evolveUNforgivingErrors<RefactoredRow, RowAggregateEvent> row' [bookingEvent]
            Expect.isOk rowAfterBooking "should be equal"
            
            let availableSeats = (rowAfterBooking.OkValue).GetAvailableSeats()
            Expect.equal availableSeats.Length 4 "should be equal"
        
        testCase "create a booking event that violates the invariant rule and process it - Error" <| fun _ ->
            let seats = 
                [ { id = 1; State = Free }
                  { id = 2; State = Free }
                  { id = 3; State = Free }
                  { id = 4; State = Free }
                  { id = 5; State = Free }
                ]
            let row = RefactoredRow (Guid.NewGuid())
            let row  = row.AddSeats seats |> Result.get
            let booking = { id = 1; seats = [1;2;4;5] }
            let bookingEvent = RowAggregateEvent.SeatBooked booking
            let rowAfterBooking =  evolveUNforgivingErrors<RefactoredRow, RowAggregateEvent> row  [bookingEvent]
            Expect.isError rowAfterBooking "should be equal"
            
        testCase "create two bookings. Use the forgiving strategy. One will violate the invariant rule so just one is considered - Ok" <| fun _ ->
            let seats = 
                [ { id = 1; State = Free }
                  { id = 2; State = Free }
                  { id = 3; State = Free }
                  { id = 4; State = Free }
                  { id = 5; State = Free }
                ]
            let row = RefactoredRow (Guid.NewGuid())
            let row' = row.AddSeats seats |> Result.get
            let booking1 = { id = 1; seats = [1; 2] }
            let booking2 = { id = 2; seats = [4; 5] }
            let bookingEvent1 = RowAggregateEvent.SeatBooked booking1
            let bookingEvent2 = RowAggregateEvent.SeatBooked booking2
            let rowAfterBooking = evolve<RefactoredRow, RowAggregateEvent> row'  [bookingEvent1; bookingEvent2]
            Expect.isOk rowAfterBooking "should be equal"

        testCase "add and retrieve events in refactored aggregate way - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            AggregateCache<RefactoredRow>.Instance.Clear()
            storage.Reset "_01" "_seatrow" |> ignore
            let seats = 
                [ { id = 1; State = Free }
                  { id = 2; State = Free }
                  { id = 3; State = Free }
                  { id = 4; State = Free }
                  { id = 5; State = Free }
                ]
            let refactoredRow = RefactoredRow (Guid.NewGuid())
            let refactoredRow' = refactoredRow.AddSeats seats |> Result.get
            let booking = { id = 1; seats = [1; 2] }
            let bookingEvent = (RowAggregateEvent.SeatBooked booking).Serialize serializer

            let eventsAdded = 
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    refactoredRow'.Id
                    [bookingEvent]
            Expect.isOk eventsAdded "should be equal"

        testCase "add events in refactored way and retrieve it - Ok " <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            storage.Reset "_01" "_seatrow" |> ignore
            AggregateCache<RefactoredRow>.Instance.Clear()
            let seats = 
                [ { id = 1; State = Free }
                  { id = 2; State = Free }
                  { id = 3; State = Free }
                  { id = 4; State = Free }
                  { id = 5; State = Free }
                ]
            let refactoredRow = RefactoredRow (Guid.NewGuid())
            let refactoredRow' = refactoredRow.AddSeats seats |> Result.get
            let booking = { id = 1; seats = [1; 2] }
            let bookingEvent = (RowAggregateEvent.SeatBooked booking).Serialize serializer

            let eventsAdded = 
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    refactoredRow'.Id
                    [bookingEvent]
            Expect.isOk eventsAdded "should be equal"
            let version = 
                    RefactoredRow.Version
            let name = 
                    RefactoredRow.StorageName

            let storageEvents = storage.TryGetLastEventIdByAggregateIdWithKafkaOffSet version name refactoredRow.Id
            Expect.isSome storageEvents "should be equal"

        testCase "add seats events  - Ok " <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            storage.Reset "_01" "_seatrow" |> ignore
            AggregateCache<RefactoredRow>.Instance.Clear()

            let refactoredRow = RefactoredRow (Guid.NewGuid())
            let seat = { id = 1; State = Free }
            let seatAdded = (RowAggregateEvent.SeatAdded seat).Serialize serializer
            let eventAdded = 
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    refactoredRow.Id
                    [seatAdded]
            Expect.isOk eventAdded "should be equal"
            let storageEvents = storage.TryGetLastEventIdByAggregateIdWithKafkaOffSet RefactoredRow.Version RefactoredRow.StorageName refactoredRow.Id
            Expect.isSome storageEvents "should be equal"

        testCase "add more seats events - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            storage.Reset "_01" "_seatrow" |> ignore
            AggregateCache<RefactoredRow>.Instance.Clear()
            let refactoredRow = RefactoredRow (Guid.NewGuid())
            let seats = 
                [ { id = 1; State = Free }
                  { id = 2; State = Free }
                ]
            let seatsAdded = (RowAggregateEvent.SeatsAdded seats).Serialize serializer
            let eventAdded = 
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    refactoredRow.Id
                    [seatsAdded]
            Expect.isOk eventAdded "should be equal"

        testCase "write and retrieve series of events about two different aggregates of the same stream - OK" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            storage.Reset "_01" "_seatrow" |> ignore
            AggregateCache<RefactoredRow>.Instance.Clear()
            let seats = 
                [ { id = 1; State = Free }
                  { id = 2; State = Free }
                  { id = 3; State = Free }
                  { id = 4; State = Free }
                  { id = 5; State = Free }
                ]
            let refactoredRowX = RefactoredRow (Guid.NewGuid())
            let refactoredRow' = refactoredRowX.AddSeats seats |> Result.get

            let seats2 = 
                [ { id = 6; State = Free }
                  { id = 7; State = Free }
                  { id = 8; State = Free }
                  { id = 9; State = Free }
                  { id = 10; State = Free }
                ]

            let refactoredRow2X = RefactoredRow (Guid.NewGuid())
            let refactoredRow2 = refactoredRow2X.AddSeats seats2 |> Result.get

            let booking = { id = 1; seats = [1; 2] }
            let booking2 = { id = 2; seats = [6; 7] }
            let bookingEvent = (RowAggregateEvent.SeatBooked booking).Serialize serializer
            let bookingEvent2 = (RowAggregateEvent.SeatBooked booking2).Serialize serializer

            let eventsAdded = 
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    refactoredRow'.Id
                    [bookingEvent]

            Expect.isOk eventsAdded "should be equal"

            let eventsAdded2 =
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    refactoredRow2.Id
                    [bookingEvent2]
            Expect.isOk eventsAdded2 "should be equal"

            let version = 
                    RefactoredRow.Version
            let name = 
                    RefactoredRow.StorageName

            let retrievedEvents1 = storage.GetAggregateEventsAfterId version name refactoredRow'.Id 0
            Expect.isOk retrievedEvents1 "should be equal"
            Expect.equal (retrievedEvents1.OkValue |> List.length) 1 "should be equal"

            let retrievedEvents2 = storage.GetAggregateEventsAfterId version name refactoredRow2.Id 0
            Expect.isOk retrievedEvents2 "should be equal"
            Expect.equal (retrievedEvents2.OkValue |> List.length) 1 "should be equal"

        testCase "add row to the storage, add seat events and retrieve them - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            storage.Reset "_01" "_seatrow" |> ignore
            AggregateCache<RefactoredRow>.Instance.Clear()
            let row = RefactoredRow (Guid.NewGuid())
            let id = row.Id
            let seatAdded = (RowAggregateEvent.SeatAdded { id = 1; State = Free })
            let seatAddedSerialized = seatAdded.Serialize serializer
            let storedEvent = 
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    id
                    [seatAddedSerialized]
            Expect.isOk storedEvent "should be equal"

            let retrievedEvents = storage.GetAggregateEventsAfterId RefactoredRow.Version RefactoredRow.StorageName id 0
            Expect.isOk retrievedEvents "should be equal"
            let result = retrievedEvents.OkValue
            Expect.equal result.Length 1 "should be equal"

        testCase "add seat on a row and process the related event - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            storage.Reset "_01" "_seatrow" |> ignore
            AggregateCache<RefactoredRow>.Instance.Clear()
            let row = RefactoredRow (Guid.NewGuid())
            let id = row.Id
            let seatAdded = (SeatAdded { id = 1; State = Free })
            let evolved = evolve<RefactoredRow, RowAggregateEvent> row [seatAdded]
            Expect.isOk evolved "should be equal"
            let result = evolved.OkValue
            let seats = result.GetAvailableSeats()
            Expect.equal seats.Length 1 "should be equal"

        testCase "add seat on a row, store it, retrieve it and process to the current state - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            storage.Reset "_01" "_seatrow" |> ignore
            AggregateCache<RefactoredRow>.Instance.Clear()
            let row = RefactoredRow (Guid.NewGuid())
            let seat = { id = 1; State = Free }
            let seatAdded = SeatAdded seat
            let storedEvent = 
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seatAdded.Serialize serializer]

            Expect.isOk storedEvent "should be equal"

            let retrievedEvents = storage.GetAggregateEventsAfterId RefactoredRow.Version RefactoredRow.StorageName row.Id 0
            Expect.isOk retrievedEvents "should be equal"
            let retrievedEventsJsonValue = retrievedEvents.OkValue
            let retrievedEventsValue = 
                retrievedEventsJsonValue 
                |>> snd
                |> catchErrors (fun x -> RowAggregateEvent.Deserialize (serializer, x))
                |> Result.get

            let rebuiltRow = evolve<RefactoredRow, RowAggregateEvent> row retrievedEventsValue 
            Expect.isOk rebuiltRow "should be ok"

            let result = (rebuiltRow.OkValue).Seats
            Expect.equal result.Length 1 "should be equal"
            Expect.equal result.[0] seat "should be equal"

        testCase "add an invalid event being able to process it anyway - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            storage.Reset "_01" "_seatrow" |> ignore
            AggregateCache<RefactoredRow>.Instance.Clear()
            let row = RefactoredRow (Guid.NewGuid())
            let seat = { id = 1; State = Free }
            let seatAdded = SeatAdded seat
            let storedEvent = 
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seatAdded.Serialize serializer]

            Expect.isOk storedEvent "should be equal"

            let storeTheEventAgain =
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seatAdded.Serialize serializer]
            Expect.isOk storeTheEventAgain "should be equal"


            let retrievedEvents = storage.GetAggregateEventsAfterId RefactoredRow.Version RefactoredRow.StorageName row.Id 0
            Expect.isOk retrievedEvents "should be equal"
            let retrievedEventsJsonValue = retrievedEvents.OkValue
            Expect.equal retrievedEventsJsonValue.Length 2 "should be equal"
            let retrievedEventsValue = 
                retrievedEventsJsonValue 
                |>> snd
                |> catchErrors (fun x -> RowAggregateEvent.Deserialize (serializer, x))
                |> Result.get

            let rebuiltRow = evolve<RefactoredRow, RowAggregateEvent> row retrievedEventsValue 
            Expect.isOk rebuiltRow "should be ok"

            let result = (rebuiltRow.OkValue).Seats
            Expect.equal result.Length 1 "should be equal"
            Expect.equal result.[0] seat "should be equal"

        testCase "add two row events - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            storage.Reset "_01" "_seatrow" |> ignore
            AggregateCache<RefactoredRow>.Instance.Clear()
            let row = RefactoredRow (Guid.NewGuid())
            let seat1 = { id = 1; State = Free }
            let seat2 = { id = 2; State = Free }
            let seat1Added = SeatAdded seat1
            let seat2Added = SeatAdded seat2
            let storeFirstEvent = 
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat1Added.Serialize serializer]

            Expect.isOk storeFirstEvent "should be equal"

            let storeSecondEvent = 
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat2Added.Serialize serializer]
            Expect.isOk storeSecondEvent "should be equal"

            let retrievedEvents = storage.GetAggregateEventsAfterId RefactoredRow.Version RefactoredRow.StorageName row.Id 0
            Expect.isOk retrievedEvents "should be equal"
            let retrievedEventsJsonValue = retrievedEvents.OkValue
            Expect.equal retrievedEventsJsonValue.Length 2 "should be equal"
            let retrievedEventsValue = 
                retrievedEventsJsonValue 
                |>> snd
                |> catchErrors (fun x -> RowAggregateEvent.Deserialize (serializer, x))
                |> Result.get

            let rebuiltRow = evolve<RefactoredRow, RowAggregateEvent> row retrievedEventsValue 
            Expect.isOk rebuiltRow "should be ok"

            let result = (rebuiltRow.OkValue).Seats
            Expect.equal result.Length 2 "should be equal"
            let retrievedSeats = result |> Set.ofList
            Expect.equal retrievedSeats ([seat1; seat2] |> Set.ofList) "should be equal"

        testCase "add two row events, add second one twice - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            storage.Reset "_01" "_seatrow" |> ignore
            AggregateCache<RefactoredRow>.Instance.Clear()
            let row = RefactoredRow (Guid.NewGuid())
            let seat1 = { id = 1; State = Free }
            let seat2 = { id = 2; State = Free }
            let seat1Added = SeatAdded seat1
            let seat2Added = SeatAdded seat2
            let storeFirstEvent = 
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat1Added.Serialize serializer]

            Expect.isOk storeFirstEvent "should be equal"

            let storeSecondEvent = 
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat2Added.Serialize serializer]
            Expect.isOk storeSecondEvent "should be equal"

            let storeSecondEventAgain = 
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat2Added.Serialize serializer]
            Expect.isOk storeSecondEventAgain "should be equal"

            let retrievedEvents = storage.GetAggregateEventsAfterId RefactoredRow.Version RefactoredRow.StorageName row.Id 0
            Expect.isOk retrievedEvents "should be equal"
            let retrievedEventsJsonValue = retrievedEvents.OkValue
            Expect.equal retrievedEventsJsonValue.Length 3 "should be equal"
            let retrievedEventsValue = 
                retrievedEventsJsonValue 
                |>> snd
                |> catchErrors (fun x -> RowAggregateEvent.Deserialize (serializer, x))
                |> Result.get

            let rebuiltRow = evolve<RefactoredRow, RowAggregateEvent> row retrievedEventsValue 
            Expect.isOk rebuiltRow "should be ok"

            let result = (rebuiltRow.OkValue).Seats
            Expect.equal result.Length 2 "should be equal"
            let retrievedSeats = result |> Set.ofList
            Expect.equal retrievedSeats ([seat1; seat2] |> Set.ofList) "should be equal"

        testCase "add two row events, add first one twice - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            storage.Reset "_01" "_seatrow" |> ignore
            AggregateCache<RefactoredRow>.Instance.Clear()
            let row = RefactoredRow (Guid.NewGuid())
            let seat1 = { id = 1; State = Free }
            let seat2 = { id = 2; State = Free }
            let seat1Added = SeatAdded seat1
            let seat2Added = SeatAdded seat2
            let storeFirstEvent = 
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat1Added.Serialize serializer]

            Expect.isOk storeFirstEvent "should be equal"

            let storeFirstEventAgain = 
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat1Added.Serialize serializer]
            Expect.isOk storeFirstEventAgain "should be equal"

            let storeSecondEvent = 
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat2Added.Serialize serializer]
            Expect.isOk storeSecondEvent "should be equal"

            let retrievedEvents = storage.GetAggregateEventsAfterId RefactoredRow.Version RefactoredRow.StorageName row.Id 0
            Expect.isOk retrievedEvents "should be equal"
            let retrievedEventsJsonValue = retrievedEvents.OkValue
            Expect.equal retrievedEventsJsonValue.Length 3 "should be equal"
            let retrievedEventsValue = 
                retrievedEventsJsonValue 
                |>> snd
                |> catchErrors (fun x -> RowAggregateEvent.Deserialize (serializer, x))
                |> Result.get

            let rebuiltRow = evolve<RefactoredRow, RowAggregateEvent> row retrievedEventsValue 
            Expect.isOk rebuiltRow "should be ok"

            let result = (rebuiltRow.OkValue).Seats
            Expect.equal result.Length 2 "should be equal"
            let retrievedSeats = result |> Set.ofList
            Expect.equal retrievedSeats ([seat1; seat2] |> Set.ofList) "should be equal"

        testCase "add two row events, add first one twice - process with unforgiving strategy - Error" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            storage.Reset "_01" "_seatrow" |> ignore
            AggregateCache<RefactoredRow>.Instance.Clear()
            let row = RefactoredRow (Guid.NewGuid())
            let seat1 = { id = 1; State = Free }
            let seat2 = { id = 2; State = Free }
            let seat1Added = SeatAdded seat1
            let seat2Added = SeatAdded seat2
            let storeFirstEvent = 
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat1Added.Serialize serializer]

            Expect.isOk storeFirstEvent "should be equal"

            let storeFirstEventAgain = 
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat1Added.Serialize serializer]
            Expect.isOk storeFirstEventAgain "should be equal"

            let storeSecondEvent = 
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat2Added.Serialize serializer]
            Expect.isOk storeSecondEvent "should be equal"

            let retrievedEvents = storage.GetAggregateEventsAfterId RefactoredRow.Version RefactoredRow.StorageName row.Id 0
            Expect.isOk retrievedEvents "should be equal"
            let retrievedEventsJsonValue = retrievedEvents.OkValue
            Expect.equal retrievedEventsJsonValue.Length 3 "should be equal"
            let retrievedEventsValue = 
                retrievedEventsJsonValue 
                |>> snd
                |> catchErrors (fun x -> RowAggregateEvent.Deserialize (serializer, x))
                |> Result.get

            let rebuiltRow = evolveUNforgivingErrors<RefactoredRow, RowAggregateEvent> row retrievedEventsValue 
            Expect.isError rebuiltRow "should be Error"
            let (Error x) = rebuiltRow
            Expect.equal x "Seat with id '1' already exists" "should be equal"

        testCase "add two row events, add first one twice, and second one twice - process with unforgiving strategy, gets the first of the errors - Error" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            storage.Reset "_01" "_seatrow" |> ignore
            AggregateCache<RefactoredRow>.Instance.Clear()
            let row = RefactoredRow (Guid.NewGuid())
            let seat1 = { id = 1; State = Free }
            let seat2 = { id = 2; State = Free }
            let seat1Added = SeatAdded seat1
            let seat2Added = SeatAdded seat2
            let storeFirstEvent = 
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat1Added.Serialize serializer]

            Expect.isOk storeFirstEvent "should be equal"

            let storeFirstEventAgain = 
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat1Added.Serialize serializer]
            Expect.isOk storeFirstEventAgain "should be equal"

            let storeSecondEvent = 
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat2Added.Serialize serializer]
            Expect.isOk storeSecondEvent "should be equal"

            let storeSecondEventAgain =
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat2Added.Serialize serializer]

            let retrievedEvents = storage.GetAggregateEventsAfterId RefactoredRow.Version RefactoredRow.StorageName row.Id 0
            Expect.isOk retrievedEvents "should be equal"
            let retrievedEventsJsonValue = retrievedEvents.OkValue
            Expect.equal retrievedEventsJsonValue.Length 4 "should be equal"
            let retrievedEventsValue = 
                retrievedEventsJsonValue 
                |>> snd
                |> catchErrors (fun x -> RowAggregateEvent.Deserialize (serializer, x))
                |> Result.get

            let rebuiltRow = evolveUNforgivingErrors<RefactoredRow, RowAggregateEvent> row retrievedEventsValue 
            Expect.isError rebuiltRow "should be Error"
            let (Error x) = rebuiltRow
            Expect.equal x "Seat with id '1' already exists" "should be equal"

        testCase "add two row events, add first one twice, and second one twice - process with forgiving strategy, gets both the seats - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            storage.Reset "_01" "_seatrow" |> ignore
            AggregateCache<RefactoredRow>.Instance.Clear()
            let row = RefactoredRow (Guid.NewGuid())
            let seat1 = { id = 1; State = Free }
            let seat2 = { id = 2; State = Free }
            let seat1Added = SeatAdded seat1
            let seat2Added = SeatAdded seat2
            let storeFirstEvent = 
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat1Added.Serialize serializer]

            Expect.isOk storeFirstEvent "should be equal"

            let storeFirstEventAgain = 
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat1Added.Serialize serializer]
            Expect.isOk storeFirstEventAgain "should be equal"

            let storeSecondEvent = 
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat2Added.Serialize serializer]
            Expect.isOk storeSecondEvent "should be equal"

            let storeSecondEventAgain =
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat2Added.Serialize serializer]

            let retrievedEvents = storage.GetAggregateEventsAfterId RefactoredRow.Version RefactoredRow.StorageName row.Id 0
            Expect.isOk retrievedEvents "should be equal"
            let retrievedEventsJsonValue = retrievedEvents.OkValue
            Expect.equal retrievedEventsJsonValue.Length 4 "should be equal"
            let retrievedEventsValue = 
                retrievedEventsJsonValue 
                |>> snd
                |> catchErrors (fun x -> RowAggregateEvent.Deserialize (serializer, x))
                |> Result.get

            let rebuiltRow = evolve<RefactoredRow, RowAggregateEvent> row retrievedEventsValue 
            Expect.isOk rebuiltRow "should be ok"
            let actualSeats = (rebuiltRow.OkValue).Seats
            Expect.equal actualSeats.Length 2 "should be equal"
            Expect.equal (actualSeats |> Set.ofList) ([seat1; seat2] |> Set.ofList) "should be equal"

        testCase "add three row events, add first one twice, and second one twice, then add another one - process with forgiving strategy, gets all thred the seats - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            storage.Reset "_01" "_seatrow" |> ignore
            AggregateCache<RefactoredRow>.Instance.Clear()
            let row = RefactoredRow (Guid.NewGuid())
            let seat1 = { id = 1; State = Free }
            let seat2 = { id = 2; State = Free }
            let seat3 = { id = 3; State = Free }
            let seat1Added = SeatAdded seat1
            let seat2Added = SeatAdded seat2
            let seat3Added = SeatAdded seat3
            let storeFirstEvent = 
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat1Added.Serialize serializer]

            Expect.isOk storeFirstEvent "should be equal"

            let storeFirstEventAgain = 
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat1Added.Serialize serializer]
            Expect.isOk storeFirstEventAgain "should be equal"

            let storeSecondEvent = 
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat2Added.Serialize serializer]
            Expect.isOk storeSecondEvent "should be equal"

            let storeSecondEventAgain =
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat2Added.Serialize serializer]
            Expect.isOk storeSecondEventAgain "should be equal"

            let storeThirdSeat =
                storage.AddAggregateEvents
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat3Added.Serialize serializer]

            Expect.isOk storeThirdSeat "should be equal"

            let retrievedEvents = storage.GetAggregateEventsAfterId RefactoredRow.Version RefactoredRow.StorageName row.Id 0
            Expect.isOk retrievedEvents "should be equal"
            let retrievedEventsJsonValue = retrievedEvents.OkValue
            Expect.equal retrievedEventsJsonValue.Length 5 "should be equal"
            let retrievedEventsValue = 
                retrievedEventsJsonValue 
                |>> snd
                |> catchErrors (fun x -> RowAggregateEvent.Deserialize (serializer, x))
                |> Result.get

            let rebuiltRow = evolve<RefactoredRow, RowAggregateEvent> row retrievedEventsValue 
            Expect.isOk rebuiltRow "should be ok"
            let actualSeats = (rebuiltRow.OkValue).Seats
            Expect.equal actualSeats.Length 3 "should be equal"
            Expect.equal (actualSeats |> Set.ofList) ([seat1; seat2; seat3] |> Set.ofList) "should be equal"
    ]
    |> testSequenced

[<Tests>]
let buildCurrentStateOfaRowAggregateTest  =
    let connection = 
        "Server=127.0.0.1;"+
        "Database=es_seat_booking;" +
        "User Id=safe;"+
        "Password=safe;"
    let serializer = new Utils.JsonSerializer(Utils.serSettings) :> Utils.ISerializer
    testList "test building the current state" [
        testCase "there is no snapshot about an aggregate by its id, so storage will return none - Ok" <|  fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            let anyGuid = Guid.NewGuid()
            let snapshot = storage.TryGetLastSnapshotIdByAggregateId RefactoredRow.Version RefactoredRow.StorageName anyGuid
            Expect.isNone snapshot "should be equal"

        testCase "get fresh state about an unexisting aggregate - Error" <|  fun _ ->
            let id = Guid.NewGuid()
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            storage.Reset StadiumContext.StadiumContext.Version StadiumContext.StadiumContext.StorageName |> ignore
            storage.Reset RefactoredRow.Version RefactoredRow.StorageName |> ignore

            let state = getAggregateFreshState<RefactoredRow, RowAggregateEvent> id  storage
            Expect.isError state "should be equal"
            let (Error e) = state
            printf "error %A\n" e

        testCase "when the aggregate snapshot is stored then get a fresh state which will return that snapshot - Error" <|  fun _ ->
            let row = RefactoredRow (Guid.NewGuid())
            let id = row.Id
            let deserializedRow = (row :> Aggregate).Serialize serializer
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            AggregateCache<RefactoredRow.RefactoredRow>.Instance.Clear()
            storage.Reset RefactoredRow.Version RefactoredRow.StorageName |> ignore
            storage.SetInitialAggregateState row.Id RefactoredRow.Version RefactoredRow.StorageName deserializedRow |> ignore
            let state = getAggregateFreshState<RefactoredRow, RowAggregateEvent> id storage
            let (_, state', _, _) = state |> Result.get
            Expect.equal state'.Id id "should be equal"
            let seats = state'.Seats
            Expect.equal seats.Length 0 "should be equal"
            Expect.isOk state "should be equal"

        testCase "store the snapshot and then store and add seat event and then retrieve that state - Ok" <|  fun _ ->
            let row = RefactoredRow (Guid.NewGuid())
            let id = row.Id
            let deserializedRow = (row :> Aggregate).Serialize serializer
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            AggregateCache<RefactoredRow.RefactoredRow>.Instance.Clear()
            storage.Reset RefactoredRow.Version RefactoredRow.StorageName |> ignore
            storage.SetInitialAggregateState row.Id RefactoredRow.Version RefactoredRow.StorageName deserializedRow |> ignore

            let seat = { id = 1; State = Free }
            let seatAdded = (RowAggregateEvent.SeatAdded seat).Serialize serializer
            storage.AddAggregateEvents RefactoredRow.Version RefactoredRow.StorageName row.Id [seatAdded] |> ignore

            let state = getAggregateFreshState<RefactoredRow, RowAggregateEvent> id storage
            let (_, state', _, _) = state |> Result.get
            Expect.equal state'.Id id "should be equal"
            let seats = state'.Seats
            Expect.equal seats.Length 1 "should be equal"
            Expect.isOk state "should be equal"

        testCase "store the snapshot and then store and add two seats - Ok" <|  fun _ ->
            let row = RefactoredRow (Guid.NewGuid())
            let id = row.Id
            let deserializedRow = (row :> Aggregate).Serialize serializer
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            AggregateCache<RefactoredRow.RefactoredRow>.Instance.Clear()
            storage.Reset RefactoredRow.Version RefactoredRow.StorageName |> ignore
            storage.SetInitialAggregateState row.Id RefactoredRow.Version RefactoredRow.StorageName deserializedRow |> ignore

            let seat1 = { id = 1; State = Free }
            let seat2 = { id = 2; State = Free }
            let seat1Added = (RowAggregateEvent.SeatAdded seat1).Serialize serializer
            let seat2Added = (RowAggregateEvent.SeatAdded seat2).Serialize serializer
            storage.AddAggregateEvents RefactoredRow.Version RefactoredRow.StorageName row.Id [seat1Added] |> ignore
            storage.AddAggregateEvents RefactoredRow.Version RefactoredRow.StorageName row.Id [seat2Added] |> ignore

            let state = getAggregateFreshState<RefactoredRow, RowAggregateEvent> id storage
            let (_, state', _, _) = state |> Result.get
            Expect.equal state'.Id id "should be equal"
            let seats = state'.Seats
            Expect.equal seats.Length 2 "should be equal"
            Expect.isOk state "should be equal"

        testCase "create a row and add five seats to it - Ok" <| fun _ ->
            let row = RefactoredRow (Guid.NewGuid())
            let id = row.Id
            let deserializedRow = (row :> Aggregate).Serialize serializer
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            AggregateCache<RefactoredRow.RefactoredRow>.Instance.Clear()
            storage.Reset RefactoredRow.Version RefactoredRow.StorageName |> ignore
            storage.SetInitialAggregateState row.Id RefactoredRow.Version RefactoredRow.StorageName deserializedRow |> ignore
            let seatsAddedEvents = 
                [ 1 .. 5] 
                |> List.map (fun x -> (SeatAdded { id = x; State = Free}).Serialize serializer)
            storage.AddAggregateEvents RefactoredRow.Version RefactoredRow.StorageName id seatsAddedEvents |> ignore 
            let state = getAggregateFreshState<RefactoredRow, RowAggregateEvent> id storage
            let (_, state', _, _) = state |> Result.get
            let seats = state'.Seats
            Expect.equal seats.Length 5 "should be equal"

        testCase "create a row, five seats, and two bookings to it - Ok" <| fun _ ->
            let row = RefactoredRow (Guid.NewGuid())
            let id = row.Id
            let deserializedRow = (row :> Aggregate).Serialize serializer
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            AggregateCache<RefactoredRow.RefactoredRow>.Instance.Clear()
            storage.Reset RefactoredRow.Version RefactoredRow.StorageName |> ignore
            storage.SetInitialAggregateState row.Id RefactoredRow.Version RefactoredRow.StorageName deserializedRow |> ignore
            let seatsAddedEvents = 
                [ 1 .. 5] 
                |> List.map (fun x -> (SeatAdded { id = x; State = Free}).Serialize serializer)
            storage.AddAggregateEvents RefactoredRow.Version RefactoredRow.StorageName id seatsAddedEvents |> ignore 
            let state = getAggregateFreshState<RefactoredRow, RowAggregateEvent> id storage
            let (_, state', _, _) = state |> Result.get
            let seats = state'.Seats
            Expect.equal seats.Length 5 "should be equal"

            let booking1: Booking  = { id = 1; seats = [1]}
            let booking2: Booking  = { id = 2; seats = [2; 3]}

            let booking1Event = (RefactoredRow.SeatBooked booking1).Serialize serializer
            let booking2Event = (RefactoredRow.SeatBooked booking2).Serialize serializer
            let bookingDone = storage.AddAggregateEvents RefactoredRow.Version RefactoredRow.StorageName id [booking1Event; booking2Event]
            Expect.isOk bookingDone "should be ok"

            let state = getAggregateFreshState<RefactoredRow, RowAggregateEvent> id storage
            let (_, state', _, _) = state |> Result.get
            let seats = state'.Seats
            let bookedSeatsIds = seats |> List.filter (fun x -> x.State = Booked) |>> _.id
            let freeSeatsIds = seats |> List.filter (fun x -> x.State = Free) |>> _.id
            Expect.equal bookedSeatsIds [1; 2; 3] "should be equal"
            Expect.equal freeSeatsIds [4; 5] "should be equal"

        testCase "create a row, five seats, and two bookings to it. One is invalid - Ok" <| fun _ ->
            let row = RefactoredRow (Guid.NewGuid())
            let id = row.Id
            let deserializedRow = (row :> Aggregate).Serialize serializer
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            AggregateCache<RefactoredRow.RefactoredRow>.Instance.Clear()
            storage.Reset RefactoredRow.Version RefactoredRow.StorageName |> ignore
            storage.SetInitialAggregateState row.Id RefactoredRow.Version RefactoredRow.StorageName deserializedRow |> ignore
            let seatsAddedEvents = 
                [ 1 .. 5] 
                |> List.map (fun x -> (SeatAdded { id = x; State = Free}).Serialize serializer)
            storage.AddAggregateEvents RefactoredRow.Version RefactoredRow.StorageName id seatsAddedEvents |> ignore 
            let state = getAggregateFreshState<RefactoredRow, RowAggregateEvent> id storage
            let (_, state', _, _) = state |> Result.get
            let seats = state'.Seats
            Expect.equal seats.Length 5 "should be equal"

            let booking1: Booking  = { id = 1; seats = [1]}
            let booking2: Booking  = { id = 2; seats = [2; 3]}

            let booking1Event = (RefactoredRow.SeatBooked booking1).Serialize serializer
            let booking2Event = (RefactoredRow.SeatBooked booking2).Serialize serializer
            let bookingDone = storage.AddAggregateEvents RefactoredRow.Version RefactoredRow.StorageName id [booking1Event; booking2Event]
            Expect.isOk bookingDone "should be ok"

            let state = getAggregateFreshState<RefactoredRow, RowAggregateEvent> id storage
            let (_, state', _, _) = state |> Result.get
            let seats = state'.Seats
            let bookedSeatsIds = seats |> List.filter (fun x -> x.State = Booked) |>> _.id
            let freeSeatsIds = seats |> List.filter (fun x -> x.State = Free) |>> _.id
            Expect.equal bookedSeatsIds [1; 2; 3] "should be equal"
            Expect.equal freeSeatsIds [4; 5] "should be equal"
    ]
    |> testSequenced


[<Tests>]
let stadiumtestsAggregateTests =
    let connection = 
        "Server=127.0.0.1;"+
        "Database=es_seat_booking;" +
        "User Id=safe;"+
        "Password=safe;"
    let serializer = new Utils.JsonSerializer(Utils.serSettings) :> Utils.ISerializer
    testList "add rows to stadium" [
        testCase "there are no rows at the beginning - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            (storage :> IEventStore).Reset "_01" "_stadium" |> ignore
            let refactoredApp = RefactoredApp.RefactoredApp(storage)
            let rows = refactoredApp.GetAllRows() |> Result.get
            Expect.equal rows.Length 0 "should be equal"
            
        testCase "add a row - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            (storage :> IEventStore).Reset "_01" "_stadium" |> ignore
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            let refactoredApp = RefactoredApp.RefactoredApp(storage)
            let row = RefactoredRow (Guid.NewGuid())
            let rowAdded = refactoredApp.AddRow row
            Expect.isOk rowAdded "should be equal"

        testCase "add a row and retrieve it - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            AggregateCache<RefactoredRow>.Instance.Clear()
            (storage :> IEventStore).Reset "_01" "_stadium" |> ignore
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            let refactoredApp = RefactoredApp.RefactoredApp(storage)
            let row = RefactoredRow (Guid.NewGuid())
            let rowAdded = refactoredApp.AddRow row
            Expect.isOk rowAdded "should be equal"
            let rows = refactoredApp.GetAllRows() |> Result.get
            Expect.equal rows.Length 1 "should be equal"
            printf "row: %A" rows.[0].Id

        testCase "add a row and retrieve it, verify the number of seats - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            AggregateCache<RefactoredRow>.Instance.Clear()
            (storage :> IEventStore).Reset "_01" "_stadium" |> ignore
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            let refactoredApp = RefactoredApp.RefactoredApp(storage)
            let row = RefactoredRow (Guid.NewGuid())
            let rowAdded = refactoredApp.AddRow row
            Expect.isOk rowAdded "should be equal"
            let rows = refactoredApp.GetAllRows() |> Result.get
            Expect.equal rows.Length 1 "should be equal"
            let seats = rows.[0].GetAvailableSeats()
            Expect.equal seats.Length 0 "should be equal"

        testCase "when a row is added then you can retrieve it (using lowlevel rowstateviewer) - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            AggregateCache<RefactoredRow>.Instance.Clear()
            storage.Reset "_01" "_stadium" |> ignore
            storage.Reset "_01" "_seatrow" |> ignore
            let refactoredApp = RefactoredApp.RefactoredApp(storage)
            let row = RefactoredRow (Guid.NewGuid())
            let rowAdded = refactoredApp.AddRowRefactored row
            Expect.isOk rowAdded "should be equal"

            let rowStateViewer = getAggregateStorageFreshStateViewer<RefactoredRow, RowAggregateEvent> storage  

            let retrieved = rowStateViewer row.Id
            Expect.isOk retrieved "should be equal"

        testCase "retrieve a row that does not exists - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            AggregateCache<RefactoredRow>.Instance.Clear()
            storage.Reset "_01" "_stadium" |> ignore
            storage.Reset "_01" "_seatrow" |> ignore
            let id = Guid.NewGuid()

            let rowStateViewer = getAggregateStorageFreshStateViewer<RefactoredRow, RowAggregateEvent> storage  

            let retrieved = rowStateViewer id
            Expect.isError retrieved "should be equal"

        testCase "cannot add the same row twice - Error" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            AggregateCache<RefactoredRow>.Instance.Clear()
            storage.Reset "_01" "_stadium" |> ignore
            storage.Reset "_01" "_seatrow" |> ignore
            let refactoredApp = RefactoredApp.RefactoredApp(storage)
            let row = RefactoredRow (Guid.NewGuid())
            let rowAdded = refactoredApp.AddRowRefactored row
            Expect.isOk rowAdded "should be equal"
            let rowStateViewer = getAggregateStorageFreshStateViewer<RefactoredRow, RowAggregateEvent> storage  

            let retrieved = rowStateViewer row.Id
            Expect.isOk retrieved "should be equal"
            let rowAddedAgain = refactoredApp.AddRowRefactored row
            Expect.isError rowAddedAgain "should be equal"

        testCase "add two rows and retrieve them - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            AggregateCache<RefactoredRow>.Instance.Clear()
            storage.Reset "_01" "_stadium" |> ignore
            storage.Reset "_01" "_seatrow" |> ignore
            let refactoredApp = RefactoredApp.RefactoredApp(storage)
            let row = RefactoredRow (Guid.NewGuid())
            let rowAdded = refactoredApp.AddRowRefactored row
            Expect.isOk rowAdded "should be equal"
            let row2 = RefactoredRow (Guid.NewGuid())
            let row2Added = refactoredApp.AddRowRefactored row2
            Expect.isOk row2Added "should be equal"

            let retrievedRows = refactoredApp.GetAllRowReferences() |> Result.get    
            Expect.equal retrievedRows.Length 2 "should be equal"

        testCase "add two rows, retrieve their references and then retrive them singularly - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            AggregateCache<RefactoredRow>.Instance.Clear()
            storage.Reset "_01" "_stadium" |> ignore
            storage.Reset "_01" "_seatrow" |> ignore
            let refactoredApp = RefactoredApp.RefactoredApp(storage)
            let row = RefactoredRow (Guid.NewGuid())
            let rowAdded = refactoredApp.AddRowRefactored row
            Expect.isOk rowAdded "should be equal"
            let row2 = RefactoredRow (Guid.NewGuid())
            let row2Added = refactoredApp.AddRowRefactored row2
            Expect.isOk row2Added "should be equal"

            let retrievedRows = refactoredApp.GetAllRowReferences() |> Result.get    
            Expect.equal retrievedRows.Length 2 "should be equal"

            let firstRowRef = retrievedRows.[0]
            let retrieveSingleRow = refactoredApp.GetRowRefactored firstRowRef
            Expect.isOk retrieveSingleRow "should be equal"

        testCase "get an unexisting row - Error" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            AggregateCache<RefactoredRow>.Instance.Clear()
            storage.Reset "_01" "_stadium" |> ignore
            storage.Reset "_01" "_seatrow" |> ignore
            let refactoredApp = RefactoredApp.RefactoredApp(storage)
            let id = Guid.NewGuid()
            let retrieveSingleRow = refactoredApp.GetRowRefactored id
            Expect.isError retrieveSingleRow "should be equal"

        testCase "add row, retrieve the row and verify there are zero seats - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            AggregateCache<RefactoredRow>.Instance.Clear()
            storage.Reset "_01" "_stadium" |> ignore
            storage.Reset "_01" "_seatrow" |> ignore
            let refactoredApp = RefactoredApp.RefactoredApp(storage)
            let row = RefactoredRow (Guid.NewGuid())
            let rowAdded = refactoredApp.AddRowRefactored row
            let retrievedRow = refactoredApp.GetRowRefactored row.Id
            let seats = retrievedRow |> Result.get |> fun x -> x.GetAvailableSeats()
            Expect.equal seats.Length 0 "should be equal"

        testCase "add row, add seats to that row, retrieve the row and verify there are those seats - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            AggregateCache<RefactoredRow>.Instance.Clear()
            storage.Reset "_01" "_stadium" |> ignore
            storage.Reset "_01" "_seatrow" |> ignore
            let refactoredApp = RefactoredApp.RefactoredApp(storage)
            let row = RefactoredRow (Guid.NewGuid())
            let rowAdded = refactoredApp.AddRowRefactored row
            Expect.isOk rowAdded "should be equal"
            let seat = { id = 1; State = Free }
            let addSeat =  refactoredApp.AddSeat row.Id seat
            Expect.isOk addSeat "should be equal"

            let retrievedRow = refactoredApp.GetRowRefactored row.Id
            let seats = retrievedRow |> Result.get |> fun x -> x.GetAvailableSeats()
            Expect.equal seats.Length 1 "should be equal"

        testCase "add row, add two seats to that row, retrieve the row and verify there are those seats - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            AggregateCache<RefactoredRow>.Instance.Clear()
            storage.Reset "_01" "_stadium" |> ignore
            storage.Reset "_01" "_seatrow" |> ignore
            let refactoredApp = RefactoredApp.RefactoredApp(storage)
            let row = RefactoredRow (Guid.NewGuid())
            let rowAdded = refactoredApp.AddRowRefactored row
            let seat = { id = 1; State = Free }
            let addSeat =  refactoredApp.AddSeat row.Id seat
            Expect.isOk addSeat "should be equal"
            let seat2 = { id = 2; State = Free }
            let addSeat2 =  refactoredApp.AddSeat row.Id seat2
            let retrievedRow = refactoredApp.GetRowRefactored row.Id
            let seats = retrievedRow |> Result.get |> fun x -> x.GetAvailableSeats()
            Expect.equal seats.Length 2 "should be equal"

        testCase "add row, add a seats to that row, then book it - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            AggregateCache<RefactoredRow>.Instance.Clear()
            storage.Reset "_01" "_stadium" |> ignore
            storage.Reset "_01" "_seatrow" |> ignore
            let refactoredApp = RefactoredApp.RefactoredApp(storage)
            let row = RefactoredRow (Guid.NewGuid())
            let rowAdded = refactoredApp.AddRowRefactored row
            let seat = { id = 1; State = Free }
            let addSeat =  refactoredApp.AddSeat row.Id seat
            Expect.isOk addSeat "should be equal"
            let retrievedRow = refactoredApp.GetRowRefactored row.Id
            let seats = retrievedRow |> Result.get |> fun x -> x.GetAvailableSeats()
            Expect.equal seats.Length 1 "should be equal"
            let booking = { id = 1; seats = [1]} 
            let book = refactoredApp.BookSeats row.Id booking
            Expect.isOk book "should be equal"
            let seats = refactoredApp.GetRowRefactored row.Id |> Result.get |> fun x -> x.GetAvailableSeats()
            Expect.equal seats.Length 0 "should be equal"

        testCase "add row, add a seats to that row, then book it , try booking it again - Error" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            AggregateCache<RefactoredRow>.Instance.Clear()
            storage.Reset "_01" "_stadium" |> ignore
            storage.Reset "_01" "_seatrow" |> ignore
            let refactoredApp = RefactoredApp.RefactoredApp(storage)
            let row = RefactoredRow (Guid.NewGuid())
            let rowAdded = refactoredApp.AddRowRefactored row
            let seat = { id = 1; State = Free }
            let addSeat =  refactoredApp.AddSeat row.Id seat
            Expect.isOk addSeat "should be equal"
            let retrievedRow = refactoredApp.GetRowRefactored row.Id
            let seats = retrievedRow |> Result.get |> fun x -> x.GetAvailableSeats()
            Expect.equal seats.Length 1 "should be equal"
            let booking = { id = 1; seats = [1]} 
            let book = refactoredApp.BookSeats row.Id booking
            Expect.isOk book "should be equal"
            let seats = refactoredApp.GetRowRefactored row.Id |> Result.get |> fun x -> x.GetAvailableSeats()
            Expect.equal seats.Length 0 "should be equal"
            let bookAgain = refactoredApp.BookSeats row.Id booking
            Expect.isError bookAgain "should be equal"
            

        testCase "add three seats in a row, and book two of them - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            AggregateCache<RefactoredRow>.Instance.Clear()
            storage.Reset "_01" "_stadium" |> ignore
            storage.Reset "_01" "_seatrow" |> ignore
            let refactoredApp = RefactoredApp.RefactoredApp(storage)
            let row = RefactoredRow (Guid.NewGuid())
            let rowAdded = refactoredApp.AddRowRefactored row
            let seat1 = { id = 1; State = Free }
            let seat2 = { id = 2; State = Free }
            let seat3 = { id = 3; State = Free }
            Expect.isTrue true "true"
            let addSeat =  refactoredApp.AddSeats row.Id [seat1; seat2; seat3]
            Expect.isOk addSeat "should be equal"
            let retrievedRow = refactoredApp.GetRowRefactored row.Id
            let retrievedSeats = retrievedRow |> Result.get |> fun x -> x.Seats
            Expect.equal retrievedSeats.Length 3 "should be equal"
            let booking = { id = 1; seats = [1; 3]}
            let book = refactoredApp.BookSeats row.Id booking
            let availableSeats = refactoredApp.GetRowRefactored row.Id |> Result.get |> fun x -> x.GetAvailableSeats()
            Expect.equal availableSeats.Length 1 "should be equal"
            let retrievedSeats' = refactoredApp.GetRowRefactored row.Id |> Result.get |> fun x -> x.Seats
            let seat1 = retrievedSeats' |> List.filter (fun x -> x.id = 1) |> List.head
            Expect.equal seat1.State Booked "should be equal"
            let seat2 = retrievedSeats' |> List.filter (fun x -> x.id = 2) |> List.head
            Expect.equal seat2.State Free "should be equal"
            let seat3 = retrievedSeats' |> List.filter (fun x -> x.id = 3) |> List.head
            Expect.equal seat3.State Booked "should be equal"

        testCase "add five rows, and book violating the middle set not free constraint - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            AggregateCache<RefactoredRow>.Instance.Clear()
            storage.Reset "_01" "_stadium" |> ignore
            storage.Reset "_01" "_seatrow" |> ignore
            let refactoredApp = RefactoredApp.RefactoredApp(storage)
            let row = RefactoredRow (Guid.NewGuid())
            let rowAdded = refactoredApp.AddRowRefactored row
            let seat1 = { id = 1; State = Free }
            let seat2 = { id = 2; State = Free }
            let seat3 = { id = 3; State = Free }
            let seat4 = { id = 4; State = Free }
            let seat5 = { id = 5; State = Free }

            let addSeat =  refactoredApp.AddSeats row.Id [seat1; seat2; seat3; seat4; seat5]
            Expect.isOk addSeat "should be equal"

            let retrievedRow = refactoredApp.GetRowRefactored row.Id
            let retrievedSeats = retrievedRow |> Result.get |> fun x -> x.Seats
            Expect.equal retrievedSeats.Length 5 "should be equal"
            let booking = { id = 1; seats = [1; 2; 4; 5]}
            let book = refactoredApp.BookSeats row.Id booking
            Expect.isError book "should be equal"

        testCase "add two rows with five seats each then book seats of two rows - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            AggregateCache<RefactoredRow>.Instance.Clear()
            storage.Reset "_01" "_stadium" |> ignore
            storage.Reset "_01" "_seatrow" |> ignore
            let refactoredApp = RefactoredApp.RefactoredApp(storage)
            let row1 = RefactoredRow (Guid.NewGuid())
            let row1Added = refactoredApp.AddRowRefactored row1
            Expect.isOk row1Added "should be equal"
            let seat11 = { id = 1; State = Free }
            let seat12 = { id = 2; State = Free }
            let seat13 = { id = 3; State = Free }
            let seat14 = { id = 4; State = Free }
            let seat15 = { id = 5; State = Free }
            let addSeat1 = refactoredApp.AddSeats row1.Id [seat11; seat12; seat13; seat14; seat15]
            Expect.isOk addSeat1 "should be equal"
            let row2 = RefactoredRow (Guid.NewGuid())
            let row2Added = refactoredApp.AddRowRefactored row2
            Expect.isOk row2Added "should be equal"
            let seat21 = { id = 6; State = Free }
            let seat22 = { id = 7; State = Free }
            let seat23 = { id = 8; State = Free }
            let seat24 = { id = 9; State = Free }
            let seat25 = { id = 10; State = Free }
            let addSeat2 = refactoredApp.AddSeats row2.Id [seat21; seat22; seat23; seat24; seat25]
            Expect.isOk addSeat2 "should be equal"

            let booking1 = { id = 1; seats = [1; 2; 3; 4; 5]}
            let booking2 = { id = 2; seats = [6; 7; 8; 9; 10]}

            let twoBookings = refactoredApp.BookSeatsTwoRows' (row1.Id, booking1) (row2.Id, booking2)
            Expect.isOk twoBookings "should be equal"

            let retrieveRow1 = refactoredApp.GetRowRefactored row1.Id
            let retrieveRow2 = refactoredApp.GetRowRefactored row2.Id
            let availableSeats1 = retrieveRow1 |> Result.get |> fun x -> x.GetAvailableSeats()
            let availableSeats2 = retrieveRow2 |> Result.get |> fun x -> x.GetAvailableSeats()
            Expect.isTrue (availableSeats1.Length = 0) "should be equal"
            Expect.isTrue (availableSeats2.Length = 0) "should be equal"

        testCase "add two rows with five seats each then book one of them, then book again receiving error - Error" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            AggregateCache<RefactoredRow>.Instance.Clear()
            storage.Reset "_01" "_stadium" |> ignore
            storage.Reset "_01" "_seatrow" |> ignore
            let refactoredApp = RefactoredApp.RefactoredApp(storage)
            let row1 = RefactoredRow (Guid.NewGuid())
            let row1Added = refactoredApp.AddRowRefactored row1
            Expect.isOk row1Added "should be equal"
            let seat11 = { id = 1; State = Free }
            let seat12 = { id = 2; State = Free }
            let seat13 = { id = 3; State = Free }
            let seat14 = { id = 4; State = Free }
            let seat15 = { id = 5; State = Free }
            let addSeat1 = refactoredApp.AddSeats row1.Id [seat11; seat12; seat13; seat14; seat15]
            Expect.isOk addSeat1 "should be equal"
            let row2 = RefactoredRow (Guid.NewGuid())
            let row2Added = refactoredApp.AddRowRefactored row2
            Expect.isOk row2Added "should be equal"
            let seat21 = { id = 6; State = Free }
            let seat22 = { id = 7; State = Free }
            let seat23 = { id = 8; State = Free }
            let seat24 = { id = 9; State = Free }
            let seat25 = { id = 10; State = Free }
            let addSeat2 = refactoredApp.AddSeats row2.Id [seat21; seat22; seat23; seat24; seat25]
            Expect.isOk addSeat2 "should be equal"

            let booking0 = { id = 1; seats = [1]}
            let book = refactoredApp.BookSeats row1.Id booking0
            Expect.isOk book "should be ok"

            let booking1 = { id = 1; seats = [1; 2; 3; 4; 5]}
            let booking2 = { id = 2; seats = [6; 7; 8; 9; 10]}

            let twoBookings = refactoredApp.BookSeatsTwoRows' (row1.Id, booking1) (row2.Id, booking2)
            Expect.isError twoBookings "should be equal"

        testCase "book seats many rows  - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            AggregateCache<RefactoredRow>.Instance.Clear()
            storage.Reset "_01" "_stadium" |> ignore
            storage.Reset "_01" "_seatrow" |> ignore
            let refactoredApp = RefactoredApp.RefactoredApp(storage)
            let row1 = RefactoredRow (Guid.NewGuid())
            let row1Added = refactoredApp.AddRowRefactored row1
            Expect.isOk row1Added "should be equal"
            let seat11 = { id = 1; State = Free }
            let seat12 = { id = 2; State = Free }
            let seat13 = { id = 3; State = Free }
            let seat14 = { id = 4; State = Free }
            let seat15 = { id = 5; State = Free }
            let addSeat1 = refactoredApp.AddSeats row1.Id [seat11; seat12; seat13; seat14; seat15]
            Expect.isOk addSeat1 "should be equal"
            let row2 = RefactoredRow (Guid.NewGuid())
            let row2Added = refactoredApp.AddRowRefactored row2
            Expect.isOk row2Added "should be equal"
            let seat21 = { id = 6; State = Free }
            let seat22 = { id = 7; State = Free }
            let seat23 = { id = 8; State = Free }
            let seat24 = { id = 9; State = Free }
            let seat25 = { id = 10; State = Free }
            let addSeat2 = refactoredApp.AddSeats row2.Id [seat21; seat22; seat23; seat24; seat25]
            Expect.isOk addSeat2 "should be equal"

            let seat31 = { id = 11; State = Free }
            let seat32 = { id = 12; State = Free }
            let seat33 = { id = 13; State = Free }
            let seat34 = { id = 14; State = Free }
            let seat35 = { id = 15; State = Free }

            let row3 = RefactoredRow (Guid.NewGuid())
            let row3Added = refactoredApp.AddRowRefactored row3
            Expect.isOk row3Added "should be equal"
            let addSeats3 = refactoredApp.AddSeats row3.Id [seat31; seat32; seat33; seat34; seat35]
            Expect.isOk addSeats3 "should be equal"

            let booking1 = { id = 1; seats = [1; 2; 3; 4; 5]}
            let booking2 = { id = 2; seats = [6; 7; 8; 9; 10]}
            let booking3 = { id = 3; seats = [11; 12; 13; 14; 15]}

            let booked = refactoredApp.BookSeatsNRows [(row1.Id, booking1); (row2.Id, booking2); (row3.Id, booking3)]
            Expect.isOk booked "should be equal"

        testCase "add three roos, book seats on two rows - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            AggregateCache<RefactoredRow>.Instance.Clear()
            storage.Reset "_01" "_stadium" |> ignore
            storage.Reset "_01" "_seatrow" |> ignore
            let refactoredApp = RefactoredApp.RefactoredApp(storage)
            let row1 = RefactoredRow (Guid.NewGuid())
            let row1Added = refactoredApp.AddRowRefactored row1
            Expect.isOk row1Added "should be equal"
            let seat11 = { id = 1; State = Free }
            let seat12 = { id = 2; State = Free }
            let seat13 = { id = 3; State = Free }
            let seat14 = { id = 4; State = Free }
            let seat15 = { id = 5; State = Free }
            let addSeat1 = refactoredApp.AddSeats row1.Id [seat11; seat12; seat13; seat14; seat15]
            Expect.isOk addSeat1 "should be equal"
            let row2 = RefactoredRow (Guid.NewGuid())
            let row2Added = refactoredApp.AddRowRefactored row2
            Expect.isOk row2Added "should be equal"
            let seat21 = { id = 6; State = Free }
            let seat22 = { id = 7; State = Free }
            let seat23 = { id = 8; State = Free }
            let seat24 = { id = 9; State = Free }
            let seat25 = { id = 10; State = Free }
            let addSeat2 = refactoredApp.AddSeats row2.Id [seat21; seat22; seat23; seat24; seat25]
            Expect.isOk addSeat2 "should be equal"

            let seat31 = { id = 11; State = Free }
            let seat32 = { id = 12; State = Free }
            let seat33 = { id = 13; State = Free }
            let seat34 = { id = 14; State = Free }
            let seat35 = { id = 15; State = Free }

            let row3 = RefactoredRow (Guid.NewGuid())
            let row3Added = refactoredApp.AddRowRefactored row3
            Expect.isOk row3Added "should be equal"
            let addSeats3 = refactoredApp.AddSeats row3.Id [seat31; seat32; seat33; seat34; seat35]
            Expect.isOk addSeats3 "should be equal"

            let booking1 = { id = 1; seats = [1; 2; 3; 4]}
            let booking2 = { id = 2; seats = [6; 7; 8; 9; 10]}

            let booked = refactoredApp.BookSeatsNRows [(row1.Id, booking1); (row2.Id, booking2)]
            Expect.isOk booked "should be equal"

            let firstRow = refactoredApp.GetRowRefactored row1.Id |> Result.get
            let availableSeats = firstRow.GetAvailableSeats()
            Expect.equal availableSeats.Length 1 "should be equal"
            let secondRow = refactoredApp.GetRowRefactored row2.Id |> Result.get
            let availableSeats2 = secondRow.GetAvailableSeats()
            Expect.equal availableSeats2.Length 0 "should be equal"

        testCase "add many book events on a 15 seats row - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            AggregateCache<RefactoredRow>.Instance.Clear()
            storage.Reset "_01" "_stadium" |> ignore
            storage.Reset "_01" "_seatrow" |> ignore
            let refactoredApp = RefactoredApp.RefactoredApp(storage)
            let row1 = RefactoredRow (Guid.NewGuid())
            let row1Added = refactoredApp.AddRowRefactored row1
            Expect.isOk row1Added "should be equal"
            let seat01 = { id = 1; State = Free }
            let seat02 = { id = 2; State = Free }
            let seat03 = { id = 3; State = Free }
            let seat04 = { id = 4; State = Free }
            let seat05 = { id = 5; State = Free }
            let seat06 = { id = 6; State = Free }
            let seat07 = { id = 7; State = Free }
            let seat08 = { id = 8; State = Free }
            let seat09 = { id = 9; State = Free }
            let seat10 = { id = 10; State = Free }
            let seat11 = { id = 11; State = Free }
            let seat12 = { id = 12; State = Free }
            let seat13 = { id = 13; State = Free }
            let seat14 = { id = 14; State = Free }
            let seat15 = { id = 15; State = Free }

            let seats = 
                [   
                    seat01; seat02; seat03; seat04; seat05; seat06; seat07; 
                    seat08; seat09; seat10; seat11; seat12; seat13; seat14; seat15
                ]
            let seatAdded = refactoredApp.AddSeats row1.Id seats
            Expect.isOk seatAdded "should be equal"
            let retrievedRow = refactoredApp.GetRowRefactored row1.Id |> Result.get
            let availableSeats = retrievedRow.GetAvailableSeats()
            Expect.equal availableSeats.Length 15 "should be equal" 

            let booking1 = { id = 1; seats = [1]}
            let bookMade = refactoredApp.BookSeats row1.Id booking1
            Expect.isOk bookMade "should be equal"
            let retrievedRow = refactoredApp.GetRowRefactored row1.Id |> Result.get
            let availableSeats = retrievedRow.GetAvailableSeats()
            Expect.equal availableSeats.Length 14 "should be equal"

            let booking2 = { id = 2; seats = [2]}
            let bookmade2 = refactoredApp.BookSeats row1.Id booking2
            Expect.isOk bookmade2 "should be equal" 
            let retrievedRow = refactoredApp.GetRowRefactored row1.Id |> Result.get
            let availableSeats2 = retrievedRow.GetAvailableSeats()
            Expect.equal availableSeats2.Length 13 "should be equal"
            let boooking3 = { id = 3; seats = [3; 4]}
            let bookmade3 = refactoredApp.BookSeats row1.Id boooking3
            Expect.isOk bookmade3 "should be equal"
            let retrievedRow = refactoredApp.GetRowRefactored row1.Id |> Result.get
            Expect.equal (retrievedRow.GetAvailableSeats().Length) 11 "should be equal"
    ]
    |> testSequenced
