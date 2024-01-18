module Tests

open seatsLockWithSharpino.Row1Context
open seatsLockWithSharpino.RowAggregate
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
            (storage :> IEventStore).AddEvents Row1Context.Row1.Version Row1Context.Row1.StorageName [bookingEvent1; bookingEvent2; bookingEvent3]
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
            (storage :> IEventStore).AddEvents Row1Context.Row1.Version Row1Context.Row1.StorageName [serializedEvent]
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
            (storage :> IEventStore).AddEvents Row1Context.Row1.Version Row1Context.Row1.StorageName [serializedEvent]
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
            
            (storage :> IEventStore).AddEvents Row1Context.Row1.Version Row1Context.Row1.StorageName [booking1; booking2]
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
            
            (storage :> IEventStore).AddEvents Row1Context.Row1.Version Row1Context.Row1.StorageName [booking1; booking2]
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
            (storage :> IEventStore).AddEvents Row1Context.Row1.Version Row1Context.Row1.StorageName [bookingEvent1; bookingEvent2; bookingEvent3]
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
    
// here I will test the refactored version that is based on proper instances (no static stuff)
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
            let row = RefactoredRow ()
            let rowWithSeat = row.AddSeat seat |> Result.get
            let seats = rowWithSeat.Seats
            Expect.equal seats.Length 1 "should be equal"

        testCase "add twice the same seat - Error" <| fun _ ->
            let seat = { id = 1; State = Free }
            let row = RefactoredRow ()
            let rowWithSeat = row.AddSeat seat |> Result.get
            let seats = rowWithSeat.Seats
            Expect.equal seats.Length 1 "should be equal"
            let addSeatAgain = rowWithSeat.AddSeat seat
            Expect.isError addSeatAgain "should be equal"
            
        testCase "add a single seat 2 -  Ok" <|  fun _ ->
            let seat = { id = 1; State = Free }
            let row = RefactoredRow ()
            let rowWithSeat = row.AddSeat seat |> Result.get
            let seats = rowWithSeat.GetAvailableSeats()
            Expect.equal seats.Length 1 "should be equal"

        testCase "add a group of one seat -  Ok" <|  fun _ ->
            let seat = { id = 1; State = Free }
            let row = RefactoredRow ()
            let rowWithSeat = row.AddSeats [seat] |> Result.get
            let seats = rowWithSeat.Seats
            Expect.equal seats.Length 1 "should be equal"

        testCase "add a group of one seat 2 -  Ok" <|  fun _ ->
            let seat = { id = 1; State = Free }
            let row = RefactoredRow ()
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
            let row  = RefactoredRow ()
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

            let row = RefactoredRow ()
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
            let row  = RefactoredRow ()
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
            let row = RefactoredRow ()
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
            let row = RefactoredRow ()
            let row' = row.AddSeats seats |> Result.get
            let booking1 = { id = 1; seats = [1; 2] }
            let booking2 = { id = 2; seats = [4; 5] }
            let bookingEvent1 = RowAggregateEvent.SeatBooked booking1
            let bookingEvent2 = RowAggregateEvent.SeatBooked booking2
            let rowAfterBooking = evolve<RefactoredRow, RowAggregateEvent> row'  [bookingEvent1; bookingEvent2]
            Expect.isOk rowAfterBooking "should be equal"

        testCase "add and retrieve events in refactored aggregate way - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCacheRefactored<RefactoredRow>.Instance.Clear()
            storage.Reset "_01" "_seatrow" |> ignore
            let seats = 
                [ { id = 1; State = Free }
                  { id = 2; State = Free }
                  { id = 3; State = Free }
                  { id = 4; State = Free }
                  { id = 5; State = Free }
                ]
            let refactoredRow = RefactoredRow ()
            let refactoredRow' = refactoredRow.AddSeats seats |> Result.get
            let booking = { id = 1; seats = [1; 2] }
            let bookingEvent = (RowAggregateEvent.SeatBooked booking).Serialize serializer

            let eventsAdded = 
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    refactoredRow'.Id
                    [bookingEvent]
            Expect.isOk eventsAdded "should be equal"

        testCase "add events in refactored way and retrieve it - Ok " <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            storage.Reset "_01" "_seatrow" |> ignore
            StateCacheRefactored<RefactoredRow>.Instance.Clear()
            let seats = 
                [ { id = 1; State = Free }
                  { id = 2; State = Free }
                  { id = 3; State = Free }
                  { id = 4; State = Free }
                  { id = 5; State = Free }
                ]
            let refactoredRow = RefactoredRow ()
            let refactoredRow' = refactoredRow.AddSeats seats |> Result.get
            let booking = { id = 1; seats = [1; 2] }
            let bookingEvent = (RowAggregateEvent.SeatBooked booking).Serialize serializer

            let eventsAdded = 
                storage.AddEventsRefactored
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
            StateCacheRefactored<RefactoredRow>.Instance.Clear()

            let refactoredRow = RefactoredRow ()
            let seat = { id = 1; State = Free }
            let seatAdded = (RowAggregateEvent.SeatAdded seat).Serialize serializer
            let eventAdded = 
                storage.AddEventsRefactored
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
            StateCacheRefactored<RefactoredRow>.Instance.Clear()
            let refactoredRow = RefactoredRow ()
            let seats = 
                [ { id = 1; State = Free }
                  { id = 2; State = Free }
                ]
            let seatsAdded = (RowAggregateEvent.SeatsAdded seats).Serialize serializer
            let eventAdded = 
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    refactoredRow.Id
                    [seatsAdded]
            Expect.isOk eventAdded "should be equal"

        testCase "write and retrieve series of events about two different aggregates of the same stream - OK" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            storage.Reset "_01" "_seatrow" |> ignore
            StateCacheRefactored<RefactoredRow>.Instance.Clear()
            let seats = 
                [ { id = 1; State = Free }
                  { id = 2; State = Free }
                  { id = 3; State = Free }
                  { id = 4; State = Free }
                  { id = 5; State = Free }
                ]
            let refactoredRowX = RefactoredRow ()
            let refactoredRow' = refactoredRowX.AddSeats seats |> Result.get

            let seats2 = 
                [ { id = 6; State = Free }
                  { id = 7; State = Free }
                  { id = 8; State = Free }
                  { id = 9; State = Free }
                  { id = 10; State = Free }
                ]

            let refactoredRow2X = RefactoredRow ()
            let refactoredRow2 = refactoredRow2X.AddSeats seats2 |> Result.get

            let booking = { id = 1; seats = [1; 2] }
            let booking2 = { id = 2; seats = [6; 7] }
            let bookingEvent = (RowAggregateEvent.SeatBooked booking).Serialize serializer
            let bookingEvent2 = (RowAggregateEvent.SeatBooked booking2).Serialize serializer

            let eventsAdded = 
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    refactoredRow'.Id
                    [bookingEvent]

            Expect.isOk eventsAdded "should be equal"

            let eventsAdded2 =
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    refactoredRow2.Id
                    [bookingEvent2]
            Expect.isOk eventsAdded2 "should be equal"

            let version = 
                    RefactoredRow.Version
            let name = 
                    RefactoredRow.StorageName

            let retrievedEvents1 = storage.GetEventsAfterIdRefactored version name refactoredRow'.Id 0
            Expect.isOk retrievedEvents1 "should be equal"
            Expect.equal (retrievedEvents1.OkValue |> List.length) 1 "should be equal"

            let retrievedEvents2 = storage.GetEventsAfterIdRefactored version name refactoredRow2.Id 0
            Expect.isOk retrievedEvents2 "should be equal"
            Expect.equal (retrievedEvents2.OkValue |> List.length) 1 "should be equal"

        testCase "add row to the storage, add seat events and retrieve them - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            storage.Reset "_01" "_seatrow" |> ignore
            StateCacheRefactored<RefactoredRow>.Instance.Clear()
            let row = RefactoredRow ()
            let id = row.Id
            let seatAdded = (RowAggregateEvent.SeatAdded { id = 1; State = Free })
            let seatAddedSerialized = seatAdded.Serialize serializer
            let storedEvent = 
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    id
                    [seatAddedSerialized]
            Expect.isOk storedEvent "should be equal"

            let retrievedEvents = storage.GetEventsAfterIdRefactored RefactoredRow.Version RefactoredRow.StorageName id 0
            Expect.isOk retrievedEvents "should be equal"
            let result = retrievedEvents.OkValue
            Expect.equal result.Length 1 "should be equal"

        testCase "add seat on a row and process the related event - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            storage.Reset "_01" "_seatrow" |> ignore
            StateCacheRefactored<RefactoredRow>.Instance.Clear()
            let row = RefactoredRow ()
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
            StateCacheRefactored<RefactoredRow>.Instance.Clear()
            let row = RefactoredRow ()
            let seat = { id = 1; State = Free }
            let seatAdded = SeatAdded seat
            let storedEvent = 
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seatAdded.Serialize serializer]

            Expect.isOk storedEvent "should be equal"

            let retrievedEvents = storage.GetEventsAfterIdRefactored RefactoredRow.Version RefactoredRow.StorageName row.Id 0
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
            StateCacheRefactored<RefactoredRow>.Instance.Clear()
            let row = RefactoredRow ()
            let seat = { id = 1; State = Free }
            let seatAdded = SeatAdded seat
            let storedEvent = 
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seatAdded.Serialize serializer]

            Expect.isOk storedEvent "should be equal"

            let storeTheEventAgain =
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seatAdded.Serialize serializer]
            Expect.isOk storeTheEventAgain "should be equal"


            let retrievedEvents = storage.GetEventsAfterIdRefactored RefactoredRow.Version RefactoredRow.StorageName row.Id 0
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
            StateCacheRefactored<RefactoredRow>.Instance.Clear()
            let row = RefactoredRow ()
            let seat1 = { id = 1; State = Free }
            let seat2 = { id = 2; State = Free }
            let seat1Added = SeatAdded seat1
            let seat2Added = SeatAdded seat2
            let storeFirstEvent = 
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat1Added.Serialize serializer]

            Expect.isOk storeFirstEvent "should be equal"

            let storeSecondEvent = 
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat2Added.Serialize serializer]
            Expect.isOk storeSecondEvent "should be equal"

            let retrievedEvents = storage.GetEventsAfterIdRefactored RefactoredRow.Version RefactoredRow.StorageName row.Id 0
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
            StateCacheRefactored<RefactoredRow>.Instance.Clear()
            let row = RefactoredRow ()
            let seat1 = { id = 1; State = Free }
            let seat2 = { id = 2; State = Free }
            let seat1Added = SeatAdded seat1
            let seat2Added = SeatAdded seat2
            let storeFirstEvent = 
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat1Added.Serialize serializer]

            Expect.isOk storeFirstEvent "should be equal"

            let storeSecondEvent = 
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat2Added.Serialize serializer]
            Expect.isOk storeSecondEvent "should be equal"

            let storeSecondEventAgain = 
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat2Added.Serialize serializer]
            Expect.isOk storeSecondEventAgain "should be equal"

            let retrievedEvents = storage.GetEventsAfterIdRefactored RefactoredRow.Version RefactoredRow.StorageName row.Id 0
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
            StateCacheRefactored<RefactoredRow>.Instance.Clear()
            let row = RefactoredRow ()
            let seat1 = { id = 1; State = Free }
            let seat2 = { id = 2; State = Free }
            let seat1Added = SeatAdded seat1
            let seat2Added = SeatAdded seat2
            let storeFirstEvent = 
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat1Added.Serialize serializer]

            Expect.isOk storeFirstEvent "should be equal"

            let storeFirstEventAgain = 
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat1Added.Serialize serializer]
            Expect.isOk storeFirstEventAgain "should be equal"

            let storeSecondEvent = 
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat2Added.Serialize serializer]
            Expect.isOk storeSecondEvent "should be equal"

            let retrievedEvents = storage.GetEventsAfterIdRefactored RefactoredRow.Version RefactoredRow.StorageName row.Id 0
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
            StateCacheRefactored<RefactoredRow>.Instance.Clear()
            let row = RefactoredRow ()
            let seat1 = { id = 1; State = Free }
            let seat2 = { id = 2; State = Free }
            let seat1Added = SeatAdded seat1
            let seat2Added = SeatAdded seat2
            let storeFirstEvent = 
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat1Added.Serialize serializer]

            Expect.isOk storeFirstEvent "should be equal"

            let storeFirstEventAgain = 
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat1Added.Serialize serializer]
            Expect.isOk storeFirstEventAgain "should be equal"

            let storeSecondEvent = 
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat2Added.Serialize serializer]
            Expect.isOk storeSecondEvent "should be equal"

            let retrievedEvents = storage.GetEventsAfterIdRefactored RefactoredRow.Version RefactoredRow.StorageName row.Id 0
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
            StateCacheRefactored<RefactoredRow>.Instance.Clear()
            let row = RefactoredRow ()
            let seat1 = { id = 1; State = Free }
            let seat2 = { id = 2; State = Free }
            let seat1Added = SeatAdded seat1
            let seat2Added = SeatAdded seat2
            let storeFirstEvent = 
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat1Added.Serialize serializer]

            Expect.isOk storeFirstEvent "should be equal"

            let storeFirstEventAgain = 
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat1Added.Serialize serializer]
            Expect.isOk storeFirstEventAgain "should be equal"

            let storeSecondEvent = 
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat2Added.Serialize serializer]
            Expect.isOk storeSecondEvent "should be equal"

            let storeSecondEventAgain =
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat2Added.Serialize serializer]

            let retrievedEvents = storage.GetEventsAfterIdRefactored RefactoredRow.Version RefactoredRow.StorageName row.Id 0
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
            StateCacheRefactored<RefactoredRow>.Instance.Clear()
            let row = RefactoredRow ()
            let seat1 = { id = 1; State = Free }
            let seat2 = { id = 2; State = Free }
            let seat1Added = SeatAdded seat1
            let seat2Added = SeatAdded seat2
            let storeFirstEvent = 
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat1Added.Serialize serializer]

            Expect.isOk storeFirstEvent "should be equal"

            let storeFirstEventAgain = 
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat1Added.Serialize serializer]
            Expect.isOk storeFirstEventAgain "should be equal"

            let storeSecondEvent = 
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat2Added.Serialize serializer]
            Expect.isOk storeSecondEvent "should be equal"

            let storeSecondEventAgain =
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat2Added.Serialize serializer]

            let retrievedEvents = storage.GetEventsAfterIdRefactored RefactoredRow.Version RefactoredRow.StorageName row.Id 0
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
            StateCacheRefactored<RefactoredRow>.Instance.Clear()
            let row = RefactoredRow ()
            let seat1 = { id = 1; State = Free }
            let seat2 = { id = 2; State = Free }
            let seat3 = { id = 3; State = Free }
            let seat1Added = SeatAdded seat1
            let seat2Added = SeatAdded seat2
            let seat3Added = SeatAdded seat3
            let storeFirstEvent = 
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat1Added.Serialize serializer]

            Expect.isOk storeFirstEvent "should be equal"

            let storeFirstEventAgain = 
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat1Added.Serialize serializer]
            Expect.isOk storeFirstEventAgain "should be equal"

            let storeSecondEvent = 
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat2Added.Serialize serializer]
            Expect.isOk storeSecondEvent "should be equal"

            let storeSecondEventAgain =
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat2Added.Serialize serializer]
            Expect.isOk storeSecondEventAgain "should be equal"

            let storeThirdSeat =
                storage.AddEventsRefactored
                    RefactoredRow.Version
                    RefactoredRow.StorageName
                    row.Id
                    [seat3Added.Serialize serializer]

            Expect.isOk storeThirdSeat "should be equal"

            let retrievedEvents = storage.GetEventsAfterIdRefactored RefactoredRow.Version RefactoredRow.StorageName row.Id 0
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
    testList "test building the current state  " [
        testCase "no row aggreagate with that specific id (don't know yet)- Ok" <|  fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            let anyGuid = Guid.NewGuid()
            // let state = getFreshStateRefactored<RefactoredRow, RowAggregateEvent> anyGuid storage

            Expect.isTrue true "true should be true"
    ]


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
            let row = RefactoredRow ()
            let rowAdded = refactoredApp.AddRow row
            Expect.isOk rowAdded "should be equal"

        testCase "add a row and retrieve it - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            (storage :> IEventStore).Reset "_01" "_stadium" |> ignore
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            let refactoredApp = RefactoredApp.RefactoredApp(storage)
            let row = RefactoredRow ()
            let rowAdded = refactoredApp.AddRow row
            Expect.isOk rowAdded "should be equal"
            let rows = refactoredApp.GetAllRows() |> Result.get
            Expect.equal rows.Length 1 "should be equal"
            printf "row: %A" rows.[0].Id

        testCase "add a row and retrieve it, verify the number of seats - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            (storage :> IEventStore).Reset "_01" "_stadium" |> ignore
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            let refactoredApp = RefactoredApp.RefactoredApp(storage)
            let row = RefactoredRow ()
            let rowAdded = refactoredApp.AddRow row
            Expect.isOk rowAdded "should be equal"
            let rows = refactoredApp.GetAllRows() |> Result.get
            Expect.equal rows.Length 1 "should be equal"
            let seats = rows.[0].GetAvailableSeats()
            Expect.equal seats.Length 0 "should be equal"

        testCase "get a row by id - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            (storage :> IEventStore).Reset "_01" "_stadium" |> ignore
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            let refactoredApp = RefactoredApp.RefactoredApp(storage)
            let row = RefactoredRow ()
            let rowAdded = refactoredApp.AddRow row
            Expect.isOk rowAdded "should be equal"
            let rows = refactoredApp.GetAllRows() |> Result.get
            Expect.equal rows.Length 1 "should be equal"
            let id = rows.[0].Id
            let retrievedRow = refactoredApp.GetRow id
            Expect.isOk retrievedRow "should be equal"

        testCase "add a row and a seat to that row - Ok" <| fun _ ->
            let storage = PgStorage.PgEventStore(connection) :> IEventStore
            StateCache<StadiumContext.StadiumContext>.Instance.Clear()
            storage.Reset "_01" "_stadium" |> ignore
            storage.Reset "_01" "_seatrow" |> ignore
            let refactoredApp = RefactoredApp.RefactoredApp(storage)
            let row = RefactoredRow ()
            let rowAdded = refactoredApp.AddRow row
            Expect.isOk rowAdded "should be equal"
            let rows = refactoredApp.GetAllRows() |> Result.get
            Expect.equal rows.Length 1 "should be equal"
            let id = rows.[0].Id
            let retrievedRow = refactoredApp.GetRow id |> Result.get
            let seats = retrievedRow.GetAvailableSeats()
            Expect.equal seats.Length 0 "should be equal"

        // note: I am going to write stuff about single event sourced aggregate, not about the stadium
        // testCase "add a row that has a seat  - Ok" <| fun _ ->
        //     let storage = PgStorage.PgEventStore(connection) :> IEventStore
        //     StateCache<StadiumContext.StadiumContext>.Instance.Clear()
        //     storage.Reset "_01" "_stadium" |> ignore
        //     storage.Reset "_01" "_seatrow" |> ignore
        //     let refactoredApp = RefactoredApp.RefactoredApp(storage)
        //     let row = RefactoredRow ()

        //     let seat = { id = 1; State = Free }
        //     let RowWithSeat = row.AddSeat seat |> Result.get
        //     Expect.equal RowWithSeat.Seats.Length 1 "should be equal"

        //     let rowAdded = refactoredApp.AddRow RowWithSeat
        //     Expect.isOk rowAdded "should be equal"
        //     let rows = refactoredApp.GetAllRows() |> Result.get
        //     Expect.equal rows.Length 1 "should be equal"
        //     let id = rows.[0].Id
        //     let retrievedRow = refactoredApp.GetRow id |> Result.get
        //     let seats = retrievedRow.GetAvailableSeats()
        //     Expect.equal seats.Length 1 "should be equal"
    ]
    |> testSequenced
