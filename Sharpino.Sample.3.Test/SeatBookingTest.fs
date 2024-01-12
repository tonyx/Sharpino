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
open Sharpino.MemoryStorage
open seatsLockWithSharpino.App
open Sharpino.Storage
open Sharpino.Cache
open Sharpino.Core

[<Tests>]
let hackingEventInStorageTest =
    let serializer = new Utils.JsonSerializer(Utils.serSettings) :> Utils.ISerializer
    testList "hacks the events in the storage to make sure that invalid events will be skipped, and concurrency cannot end up in invariant rule violation " [
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
    let serializer = new Utils.JsonSerializer(Utils.serSettings) :> Utils.ISerializer
    ftestList "test the evolve about refactored aggregate  " [
        testCase "available seats  are all seats - OK" <| fun _ ->
            let seats = 
                [ { id = 1; State = Free }
                  { id = 2; State = Free }
                  { id = 3; State = Free }
                  { id = 4; State = Free }
                  { id = 5; State = Free }
                ]
            let row  = RefactoredRow seats
            let result = row.GetAvailableSeats()
            Expect.equal result.Length 5 "should be equal"
        
        testCase "book one seat - Ok" <| fun _ ->
            let seats = 
                [ { id = 1; State = Free }
                  { id = 2; State = Free }
                  { id = 3; State = Free }
                  { id = 4; State = Free }
                  { id = 5; State = Free }
                ]
            let row = RefactoredRow seats
            let booking = { id = 1; seats = [1] }
            let booked = row.BookSeats booking |> Result.get
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
            let row  = RefactoredRow seats
            let booking = { id = 1; seats = [1] }
            let bookingEvent = RowAggregateEvent.SeatBooked booking
            let rowAfterBooking =  evolveUNforgivingErrors<RefactoredRow, RowAggregateEvent.RowAggregateEvent> row  [bookingEvent]
            Expect.isOk rowAfterBooking "should be equal"
            
            let availableSeats = (rowAfterBooking.OkValue).GetAvailableSeats()
            Expect.equal availableSeats.Length 4 "should be equal"
        
        ftestCase "create a booking event that violates the invariant rule and process it - Error" <| fun _ ->
            let seats = 
                [ { id = 1; State = Free }
                  { id = 2; State = Free }
                  { id = 3; State = Free }
                  { id = 4; State = Free }
                  { id = 5; State = Free }
                ]
            let row  = RefactoredRow seats
            let booking = { id = 1; seats = [1;2;4;5] }
            let bookingEvent = RowAggregateEvent.SeatBooked booking
            let rowAfterBooking =  evolveUNforgivingErrors<RefactoredRow, RowAggregateEvent.RowAggregateEvent> row  [bookingEvent]
            Expect.isError rowAfterBooking "should be equal"

        // ftestCase "create two booking events that violates the invariant rule and another that process it - Error" <| fun _ ->
        //     let seats = 
        //         [ { id = 1; State = Free }
        //           { id = 2; State = Free }
        //           { id = 3; State = Free }
        //           { id = 4; State = Free }
        //           { id = 5; State = Free }
        //         ]
        //     let row  = RefactoredRow seats
        //     let booking = { id = 1; seats = [1;2;4;5] }
        //     let bookingEvent = RowAggregateEvent.SeatBooked booking
        //     let rowAfterBooking =  evolveUNforgivingErrors<RefactoredRow, RowAggregateEvent.RowAggregateEvent> row  [bookingEvent]
        //     Expect.isError rowAfterBooking "should be equal"
            
        ftestCase "create two bookings. Use the forgiving strategy. One will violate the invariant rule so just one is considered - Ok" <| fun _ ->
            let seats = 
                [ { id = 1; State = Free }
                  { id = 2; State = Free }
                  { id = 3; State = Free }
                  { id = 4; State = Free }
                  { id = 5; State = Free }
                ]
            let row  = RefactoredRow seats
            let booking1 = { id = 1; seats = [1; 2] }
            let booking2 = { id = 2; seats = [4; 5] }
            let bookingEvent1 = RowAggregateEvent.SeatBooked booking1
            let bookingEvent2 = RowAggregateEvent.SeatBooked booking2
            let rowAfterBooking = evolve<RefactoredRow, RowAggregateEvent.RowAggregateEvent> row  [bookingEvent1; bookingEvent2]
            Expect.isOk rowAfterBooking "should be equal"
            
    ]        
