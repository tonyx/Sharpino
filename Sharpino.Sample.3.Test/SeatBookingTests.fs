
module SeatBookingTests

open Tonyx.SeatsBooking.Seats
open Tonyx.SeatsBooking.NewRow
open Tonyx.SeatsBooking
open Tonyx.SeatsBooking.App
open Tonyx.SeatsBooking.Stadium
open Tonyx.SeatsBooking.StadiumEvents
open Tonyx.SeatsBooking.RowAggregateEvent
open Expecto
open Sharpino
open Sharpino.PgStorage
open Sharpino.MemoryStorage
open Sharpino.Storage
open Sharpino.Cache
open Sharpino.CommandHandler
open Sharpino.TestUtils
open System
open Tonyx.SeatsBooking.StadiumkafkaBookingSystem

[<Tests>]
let storageEventsTests =
    let doNothingBroker: IEventBroker =
        {
            notify = None
            notifyAggregate = None 
        }
    let connection = 
        "Server=127.0.0.1;"+
        "Database=es_seat_booking;" +
        "User Id=safe;"+
        "Password=safe;"
    let serializer = new Utils.JsonSerializer(Utils.serSettings) :> Utils.ISerializer
    testList "memoryStorageEventsTests" [
        testCase "memory storage: add row references to stadium - Ok" <| fun _ ->
            let eventStore = MemoryStorage()
            eventStore.Reset "_01" "_seatrow"
            eventStore.Reset "_01" "_stadium"
            let rowId = Guid.NewGuid()
            
            let stadiumAddedRowEvent = (StadiumEvent.RowReferenceAdded rowId).Serialize serializer
            (eventStore :> IEventStore).AddEvents Stadium.Version  Stadium.StorageName [stadiumAddedRowEvent]
            
            let row = SeatsRow (rowId, doNothingBroker)
            let serializedRow = row.Serialize serializer
            (eventStore :> IEventStore).SetInitialAggregateState rowId "_01" "_seatrow"  serializedRow
           
            let stadiumBookingSystem = StadiumBookingSystem eventStore
            let retrievedRows = stadiumBookingSystem.GetAllRowReferences()
            Expect.equal retrievedRows.OkValue.Length 1 "should be 1"
            
            let rowStateViewer = getAggregateStorageFreshStateViewer<SeatsRow, RowAggregateEvent> eventStore
            let gotState = rowStateViewer rowId
            Expect.isOk gotState "should be ok"
            
            let (_, state, _ , _) = rowStateViewer rowId |> Result.get
            Expect.equal (state.Seats.Length) 0 "should be 0"
            
        testCase "memory storage: create a row with no seats and retrieve it - Ok" <| fun _ ->
            let eventStore = MemoryStorage()
            eventStore.Reset "_01" "_seatrow"
            eventStore.Reset "_01" "_stadium"
            let rowId = Guid.NewGuid()
            let row = SeatsRow (rowId, doNothingBroker)
            let rowStorageCreation = row.Serialize serializer
            let stored = (eventStore :> IEventStore).SetInitialAggregateState rowId "_01" "_seatrow" rowStorageCreation
            Expect.isOk stored "should be ok"
            
            let rowStateViewer = getAggregateStorageFreshStateViewer<SeatsRow, RowAggregateEvent> eventStore
            let gotState = rowStateViewer rowId
            Expect.isOk gotState "should be ok"
            let (_, state, _, _) = gotState |> Result.get
            Expect.equal (state.Seats.Length) 0 "should be 0"
            
        testCase "memory storage: create a row then add a seat to it - Ok" <| fun _ ->
            let eventStore = MemoryStorage()
            eventStore.Reset "_01" "_seatrow"
            eventStore.Reset "_01" "_stadium"
            let rowId = Guid.NewGuid()
            let row = SeatsRow (rowId, doNothingBroker)
            let rowStorageCreation = row.Serialize serializer
            let stored = (eventStore :> IEventStore).SetInitialAggregateState rowId "_01" "_seatrow" rowStorageCreation
            Expect.isOk stored "should be ok"
            
            let rowStateViewer = getAggregateStorageFreshStateViewer<SeatsRow, RowAggregateEvent> eventStore
            let gotState = rowStateViewer rowId
            Expect.isOk gotState "should be ok"
            let (_, state, _, _) = gotState |> Result.get
            Expect.equal (state.Seats.Length) 0 "should be 0"
           
            let seat = { Id = 1; State = Free; RowId = None } 
            let seatAdded = (RowAggregateEvent.SeatAdded seat).Serialize serializer
            let added = (eventStore :> IEventStore).AddAggregateEvents "_01" "_seatrow" rowId [seatAdded]
            Expect.isOk added "should be ok"
            
            let rowStateViewer = getAggregateStorageFreshStateViewer<SeatsRow, RowAggregateEvent> eventStore
            let gotState = rowStateViewer rowId
            Expect.isOk gotState "should be ok"
            let (_, state, _, _) = gotState |> Result.get
            Expect.equal (state.Seats.Length) 1 "should be 0"
    ]
    |> testSequenced

[<Tests>]
let aggregateRowRefactoredTests =
    let setUp () =
        AggregateCache<SeatsRow>.Instance.Clear()
        StateCache<Stadium>.Instance.Clear()
    let connection = 
        "Server=127.0.0.1;"+
        "Database=es_seat_booking;" +
        "User Id=safe;"+
        "Password=safe;"
    let eventStore = fun () -> (PgEventStore connection) :> IEventStore
    let memoryStoreBuilder = fun () -> MemoryStorage() :> IEventStore

    let doNothingBroker: IEventBroker =
        {
            notify = None
            notifyAggregate = None 
        }
    let localHostBroker = KafkaBroker.getKafkaBroker ("localhost:9092", eventStore ())

    let storageFreshStateViewer = getStorageFreshStateViewer<Stadium, StadiumEvent> (eventStore ())
    let aggregateStorageFreshStateViewer = getAggregateStorageFreshStateViewer<SeatsRow, RowAggregateEvent> (eventStore ())

    // let sourceOfTruthBasedStadium = StadiumBookingSystem (eventStore (), doNothingBroker, storageFreshStateViewer, aggregateStorageFreshStateViewer)
    
 
    let stores =
        [
            (eventStore, "", ()); 
            // (memoryStoreBuilder, "pgEventStore", ()) 
        ]
    testList ("pgEventStore seatBookingTests - ") [
        // testCase "initially the stadium has no row references - Ok" <| fun _ ->
        multipleTestCase "initially the stadium has no row references - Ok" stores <| fun (eventStore, _, _) ->
            setUp()
            let eventStore: IEventStore = eventStore ()
            eventStore.Reset "_01" "_seatrow"
            eventStore.Reset Stadium.Version Stadium.StorageName
                    
            // let stadiumBookingSystem = StadiumBookingSystem eventStore
            // let stadiumBookingSystem = StadiumBookingSystem (eventStore, localHostBroker, storageFreshStateViewer, aggregateStorageFreshStateViewer)
            let stadiumBookingSystem = StadiumKafkaBookingSystem(eventStore, localHostBroker)

            let retrievedRows = stadiumBookingSystem.GetAllRowReferences()
            Expect.isOk retrievedRows "should be ok"
            let result = retrievedRows.OkValue
            Expect.equal 0 result.Length "should be 0"
            
        multipleTestCase "add a row reference to the stadium and retrieve it - Ok" stores <| fun (eventStore, _, _) ->
            setUp()
            // let eventStore = PgEventStore connection
            let eventStore = eventStore ()
            eventStore.Reset "_01" "_seatrow"
            eventStore.Reset Stadium.Version Stadium.StorageName
                    
            // let stadiumBookingSystem = StadiumBookingSystem eventStore
            let stadiumBookingSystem = StadiumKafkaBookingSystem(eventStore, localHostBroker)
                    
            let rowId = Guid.NewGuid()
            let addedRow = stadiumBookingSystem.AddRowReference rowId
            Expect.isOk addedRow "should be ok"
            let retrievedRow = stadiumBookingSystem.GetRow rowId
            Expect.isOk retrievedRow "should be ok"
                    
        // FOUCUS
        multipleTestCase "retrieve an unexisting row - Error" stores <| fun (eventsStore, _, _) ->
            setUp()
            let eventStore = eventStore ()
            // let eventStore = PgEventStore connection
            eventStore.Reset "_01" "_seatrow"
            eventStore.Reset Stadium.Version Stadium.StorageName
                    
            let stadiumBookingSystem = StadiumBookingSystem eventStore
            // let stadiumBookingSystem = StadiumKafkaBookingSystem(eventStore, localHostBroker)
                    
            let rowId = Guid.NewGuid()
            let retrievedRow = stadiumBookingSystem.GetRow rowId
            Expect.isError retrievedRow "should be error"
                    
        multipleTestCase "add a row reference and then seat to it. retrieve the seat then - Ok" stores <| fun (eventStore, _, _) ->
            setUp()
            // let eventStore = PgEventStore connection
            let eventStore = eventStore ()
                    
            eventStore.Reset "_01" "_seatrow"
            eventStore.Reset Stadium.Version Stadium.StorageName
                    
            // let stadiumBookingSystem = StadiumBookingSystem eventStore
            let stadiumBookingSystem = StadiumKafkaBookingSystem(eventStore, localHostBroker)
            let rowId = Guid.NewGuid()
            let addedRow = stadiumBookingSystem.AddRowReference rowId
            Expect.isOk addedRow "should be ok"
                    
            let seat = { Id = 1; State = Free; RowId = None}
            let seatAdded = stadiumBookingSystem.AddSeat rowId seat
            Expect.isOk seatAdded "should be ok"
                    
            let retrievedRow = stadiumBookingSystem.GetRow rowId 
            Expect.isOk retrievedRow "should be ok"
                   
            let okRetrievedRow = retrievedRow.OkValue
            Expect.equal okRetrievedRow.Seats.Length 1 "should be 1"
                    
        multipleTestCase "add a row reference and then some seats to it. Retrieve the seats then - Ok" stores <| fun (eventStore, _, _) ->
            setUp()
            // let eventStore = PgEventStore connection
            let eventStore = eventStore ()
            eventStore.Reset "_01" "_seatrow"
            eventStore.Reset Stadium.Version Stadium.StorageName
                    
            // let stadiumBookingSystem = StadiumBookingSystem eventStore
            let stadiumBookingSystem = StadiumKafkaBookingSystem(eventStore, localHostBroker)
            let rowId = Guid.NewGuid()
            let addedRow = stadiumBookingSystem.AddRowReference rowId
            Expect.isOk addedRow "should be ok"
                    
            let seats =  [ 
                            { Id = 1; State = Free; RowId = None  }
                            { Id = 2; State = Free; RowId = None }
                            { Id = 3; State = Free; RowId = None }
                            { Id = 4; State = Free; RowId = None }
                            { Id = 5; State = Free; RowId = None }
                            ]
                    
            let seatAdded = stadiumBookingSystem.AddSeats rowId seats
            Expect.isOk seatAdded "should be ok"
                    
            let retrievedRow = stadiumBookingSystem.GetRow rowId 
            Expect.isOk retrievedRow "should be ok"
                    
            let okRetrievedRow = retrievedRow.OkValue
            Expect.equal 5 okRetrievedRow.Seats.Length "should be 1"
                    
        multipleTestCase "add two row references add a row reference and then some seats to it. Retrieve the seats then - Ok" stores <| fun (eventStore, _, _) ->
            setUp()
            // let eventStore = PgEventStore connection
            let eventStore = eventStore ()
            eventStore.Reset "_01" "_seatrow"
            eventStore.Reset Stadium.Version Stadium.StorageName
                    
            // let stadiumBookingSystem = StadiumBookingSystem eventStore
            let stadiumBookingSystem = StadiumKafkaBookingSystem(eventStore, localHostBroker)
            let rowId = Guid.NewGuid()
            let addedRow = stadiumBookingSystem.AddRowReference rowId
            Expect.isOk addedRow "should be ok"
                     
            let rowId2 = Guid.NewGuid()
            let addedRow2 = stadiumBookingSystem.AddRowReference rowId2
                    
            Expect.isOk addedRow2 "should be ok"
                    
            let seats = [   
                            { Id = 1; State = Free; RowId = None }
                            { Id = 2; State = Free; RowId = None }
                            { Id = 3; State = Free; RowId = None }
                            { Id = 4; State = Free; RowId = None }
                            { Id = 5; State = Free; RowId = None }
                            ]
                    
            let seatAdded = stadiumBookingSystem.AddSeats rowId seats
            Expect.isOk seatAdded "should be ok"
                    
            let seats2 = [  
                            { Id = 6; State = Free; RowId = None }
                            { Id = 7; State = Free; RowId = None }
                            { Id = 8; State = Free; RowId = None }
                            { Id = 9; State = Free; RowId = None }
                            { Id = 10; State = Free; RowId = None }
                            ]
                   
            let seatsAdded2 = stadiumBookingSystem.AddSeats rowId2 seats2
            Expect.isOk seatsAdded2 "should be ok"
                    
            let retrievedRow = stadiumBookingSystem.GetRow rowId 
            Expect.isOk retrievedRow "should be ok"
                    
            let okRetrievedRow = retrievedRow.OkValue
            Expect.equal 5 okRetrievedRow.Seats.Length "should be 1"
                    
            let retrievedRow2 = stadiumBookingSystem.GetRow rowId2
            Expect.isOk retrievedRow2 "should be ok"
            let okRetrievedRow2 = retrievedRow2.OkValue
            Expect.equal 5 okRetrievedRow2.Seats.Length "should be 1"
                    
        multipleTestCase "can't add a seat with the same id of another seat in the same row - Ok" stores <| fun (eventStore, _, _) ->
            setUp()
            // let eventStore = PgEventStore connection
            let eventStore = eventStore ()
            eventStore.Reset "_01" "_seatrow"
            eventStore.Reset Stadium.Version Stadium.StorageName
                    
            // let stadiumBookingSystem = StadiumBookingSystem eventStore
            let stadiumBookingSystem = StadiumKafkaBookingSystem(eventStore, localHostBroker)
            let rowId = Guid.NewGuid()
            let addedRow = stadiumBookingSystem.AddRowReference rowId
            Expect.isOk addedRow "should be ok"
            let seat =  { Id = 1; State = Free; RowId = None }
            let seatAdded = stadiumBookingSystem.AddSeat rowId seat
            Expect.isOk seatAdded "should be ok"
                    
            let seat2 = { Id = 1; State = Free; RowId = None }
            let seatAdded2 = stadiumBookingSystem.AddSeat rowId seat2
            Expect.isError seatAdded2 "should be error"
                    

        // FOCUS
        pmultipleTestCase "add a booking on an unexisting row - Error" stores <| fun (eventStore, _, _) ->
            setUp()
            // let eventStore = PgEventStore connection
            let eventStore = eventStore ()
                   
            eventStore.Reset "_01" "_seatrow"
            eventStore.Reset Stadium.Version Stadium.StorageName
                    
            let stadiumBookingSystem = StadiumBookingSystem eventStore
            // let stadiumBookingSystem = StadiumKafkaBookingSystem(eventStore, localHostBroker)
            let booking = { Id = 1; SeatIds = [1]}
            let rowId = Guid.NewGuid()
            let tryBooking = stadiumBookingSystem.BookSeats rowId booking
            Expect.isError tryBooking "should be error"
            let (Error e ) = tryBooking
            Expect.equal e (sprintf "There is no aggregate of version \"_01\", name \"_seatrow\" with id %A" rowId) "should be equal"
                    
        multipleTestCase "add a booking on an existing row and unexisting seat - Error" stores <| fun (eventStore, _, _) ->
            setUp()
            // let eventStore = PgEventStore connection
            let eventStore = eventStore ()
                  
            eventStore.Reset "_01" "_seatrow"
            eventStore.Reset Stadium.Version Stadium.StorageName
            let rowId = Guid.NewGuid()
            // let stadiumBookingSystem = StadiumBookingSystem eventStore
            let stadiumBookingSystem = StadiumKafkaBookingSystem(eventStore, localHostBroker)
            let addedRow = stadiumBookingSystem.AddRowReference rowId
            Expect.isOk addedRow "should be ok"
            let booking = { Id = 1; SeatIds = [1]}
            let tryBooking = stadiumBookingSystem.BookSeats rowId booking
            Expect.isError tryBooking "should be error"
            let (Error e ) = tryBooking
            Expect.equal e "Seat not found" "should be equal"
                
        multipleTestCase "add a booking on a valid row and valid seat - Ok" stores <| fun (eventStore, _, _) ->
            setUp()
            // let eventStore = PgEventStore connection
            let eventStore = eventStore ()
                 
            eventStore.Reset "_01" "_seatrow"
            eventStore.Reset Stadium.Version Stadium.StorageName
            let rowId = Guid.NewGuid()
            // let stadiumBookingSystem = StadiumBookingSystem eventStore
            let stadiumBookingSystem = StadiumKafkaBookingSystem(eventStore, localHostBroker)
            let addedRow = stadiumBookingSystem.AddRowReference rowId
            Expect.isOk addedRow "should be ok"
            let seat = { Id = 1; State = Free; RowId = None }
            let seatAdded = stadiumBookingSystem.AddSeat rowId seat
            let booking = { Id = 1; SeatIds = [1]}
            let tryBooking = stadiumBookingSystem.BookSeats rowId booking
            Expect.isOk tryBooking "should be ok"
                    
        multipleTestCase "can't book an already booked seat - Error" stores <| fun (eventStore, _, _) ->
            setUp()
            // let eventStore = PgEventStore connection
            let eventStore = eventStore ()
                
            eventStore.Reset "_01" "_seatrow"
            eventStore.Reset Stadium.Version Stadium.StorageName
            let rowId = Guid.NewGuid()
            // let stadiumBookingSystem = StadiumBookingSystem eventStore
            let stadiumBookingSystem = StadiumKafkaBookingSystem(eventStore, localHostBroker)
            let addedRow = stadiumBookingSystem.AddRowReference rowId
            Expect.isOk addedRow "should be ok"
            let seat = { Id = 1; State = Free; RowId = None }
            let seatAdded = stadiumBookingSystem.AddSeat rowId seat
            let booking = { Id = 1; SeatIds = [1]}
            let tryBooking = stadiumBookingSystem.BookSeats rowId booking
            Expect.isOk tryBooking "should be ok"
                    
            let tryBookingAgain = stadiumBookingSystem.BookSeats rowId booking
            Expect.isError tryBookingAgain "should be error"
            let (Error e) = tryBookingAgain
            Expect.equal e "Seat already booked" "should be equal"
                    
        multipleTestCase "add many seats and book one of them - Ok" stores <| fun (eventStore, _, _) ->
            setUp()
            // let eventStore = PgEventStore connection
            let eventStore = eventStore ()
               
            eventStore.Reset "_01" "_seatrow"
            eventStore.Reset Stadium.Version Stadium.StorageName
            let rowId = Guid.NewGuid()
            // let stadiumBookingSystem = StadiumBookingSystem eventStore
            let stadiumBookingSystem = StadiumKafkaBookingSystem(eventStore, localHostBroker)
            let addedRow = stadiumBookingSystem.AddRowReference rowId
            Expect.isOk addedRow "should be ok"
            let seats = [
                { Id = 1; State = Free; RowId = None }
                { Id = 2; State = Free; RowId = None }
            ]
            let seatsAdded = stadiumBookingSystem.AddSeats rowId seats
            Expect.isOk seatsAdded
            let booking = { Id = 1; SeatIds = [1]}
            let tryBooking = stadiumBookingSystem.BookSeats rowId booking
            Expect.isOk tryBooking "should be ok"
                    
        multipleTestCase "violate the middle seat non empty constraint in one single booking - Ok"  stores <| fun (eventStore, _, _) ->
            setUp()
            // let eventStore = PgEventStore connection
            let eventStore = eventStore ()
              
            eventStore.Reset "_01" "_seatrow"
            eventStore.Reset Stadium.Version Stadium.StorageName
            let rowId = Guid.NewGuid()
            // let stadiumBookingSystem = StadiumBookingSystem eventStore
            let stadiumBookingSystem = StadiumKafkaBookingSystem(eventStore, localHostBroker)
            let addedRow = stadiumBookingSystem.AddRowReference rowId
            Expect.isOk addedRow "should be ok"
            let seats = [
                { Id = 1; State = Free; RowId = None }
                { Id = 2; State = Free; RowId = None }
                { Id = 3; State = Free; RowId = None }
                { Id = 4; State = Free; RowId = None }
                { Id = 5; State = Free; RowId = None }
            ]
            let seatsAdded = stadiumBookingSystem.AddSeats rowId seats
            Expect.isOk seatsAdded "should be ok"
            let booking = { Id = 1; SeatIds = [1;2;4;5]}
            let tryBooking = stadiumBookingSystem.BookSeats rowId booking
            Expect.isError tryBooking "should be error"
            let (Error e) = tryBooking
            Expect.equal e "error: can't leave a single seat free in the middle" "should be equal"
                    
        multipleTestCase "book free seats among two rows - Ok" stores <| fun (eventStore, _, _) ->
            setUp()
            // let eventStore = PgEventStore connection
            let eventStore = eventStore ()
             
            eventStore.Reset "_01" "_seatrow"
            eventStore.Reset Stadium.Version Stadium.StorageName
            let rowId1 = Guid.NewGuid()
            let rowId2 = Guid.NewGuid()
            // let stadiumBookingSystem = StadiumBookingSystem eventStore
            let stadiumBookingSystem = StadiumKafkaBookingSystem(eventStore, localHostBroker)
            let addedRow1 = stadiumBookingSystem.AddRowReference rowId1
            Expect.isOk addedRow1 "should be ok"
            let addedRow2 = stadiumBookingSystem.AddRowReference rowId2
            Expect.isOk addedRow2 "should be ok"
            let seats1 = [
                { Id = 1; State = Free; RowId = None }
                { Id = 2; State = Free; RowId = None }
                { Id = 3; State = Free; RowId = None }
                { Id = 4; State = Free; RowId = None }
                { Id = 5; State = Free; RowId = None }
            ]
            let seatsAdded1 = stadiumBookingSystem.AddSeats rowId1 seats1
            Expect.isOk seatsAdded1 "should be ok"
            let seats2 = [
                { Id = 6; State = Free; RowId = None }
                { Id = 7; State = Free; RowId = None }
                { Id = 8; State = Free; RowId = None }
                { Id = 9; State = Free; RowId = None }
                { Id = 10; State = Free; RowId = None }
            ]
            let seatsAdded2 = stadiumBookingSystem.AddSeats rowId2 seats2
            Expect.isOk seatsAdded2 "should be ok"
                    
            let booking1 = { Id = 1; SeatIds = [1;2;3;4;5]}
            let booking2 = { Id = 2; SeatIds = [6;7;8;9;10]}
            let tryMultiBooking = stadiumBookingSystem.BookSeatsNRows [(rowId1, booking1); (rowId2, booking2)]
            Expect.isOk tryMultiBooking "should be ok"
                   
            let newBooking = { Id = 1; SeatIds = [1] }
            let tryBookingAgain = stadiumBookingSystem.BookSeats rowId1 newBooking
            Expect.isError tryBookingAgain "should be error"
                    
        multipleTestCase "book free seats among two rows, one fails, so it makes fail them all - Error" stores <| fun (eventStore, _, _) ->
            setUp()
            // let eventStore = PgEventStore connection
            let eventStore = eventStore ()
            
            eventStore.Reset "_01" "_seatrow"
            eventStore.Reset Stadium.Version Stadium.StorageName
            let rowId1 = Guid.NewGuid()
            let rowId2 = Guid.NewGuid()
            // let stadiumBookingSystem = StadiumBookingSystem eventStore
            let stadiumBookingSystem = StadiumKafkaBookingSystem(eventStore, localHostBroker)
            let addedRow1 = stadiumBookingSystem.AddRowReference rowId1
            Expect.isOk addedRow1 "should be ok"
            let addedRow2 = stadiumBookingSystem.AddRowReference rowId2
            Expect.isOk addedRow2 "should be ok"
            let seats1 = [
                { Id = 1; State = Free; RowId = None }
                { Id = 2; State = Free; RowId = None }
                { Id = 3; State = Free; RowId = None }
                { Id = 4; State = Free; RowId = None }
                { Id = 5; State = Free; RowId = None }
            ]
            let seatsAdded1 = stadiumBookingSystem.AddSeats rowId1 seats1
            Expect.isOk seatsAdded1 "should be ok"
            let seats2 = [
                { Id = 6; State = Free; RowId = None }
                { Id = 7; State = Free; RowId = None }
                { Id = 8; State = Free; RowId = None }
                { Id = 9; State = Free; RowId = None }
                { Id = 10; State = Free; RowId = None }
            ]
            let seatsAdded2 = stadiumBookingSystem.AddSeats rowId2 seats2
            Expect.isOk seatsAdded2 "should be ok"
                    
            let booking1 = { Id = 1; SeatIds = [1;2;4;5]} // invariant violated
            let booking2 = { Id = 2; SeatIds = [6;7;8;9;10]}
            let tryMultiBooking = stadiumBookingSystem.BookSeatsNRows [(rowId1, booking1); (rowId2, booking2)]
            Expect.isError tryMultiBooking "should be error"
            let (Error e) = tryMultiBooking
            Expect.equal e "error: can't leave a single seat free in the middle" "should be equal"
                    
            // now make a valid booking on both
            let newBooking1 = {Id = 1; SeatIds = [1]}
            let newBooking2 = {Id = 2; SeatIds = [6]}
            let tryMultiBookingAgain = stadiumBookingSystem.BookSeatsNRows [(rowId1, newBooking1); (rowId2, newBooking2)]
            Expect.isOk tryMultiBookingAgain "should be ok"
    ] 
    |> testSequenced
        
