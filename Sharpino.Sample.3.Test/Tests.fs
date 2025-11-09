module Tests

open System.Threading
open System.Threading.Tasks
open Sharpino
open Sharpino.CommandHandler
open Sharpino.EventBroker
open Sharpino.PgStorage
open Sharpino.RabbitMq
open Sharpino.Result
open Sharpino.Storage
open Sharpino.Cache
open Sharpino.Utils
open Sharpino.TestUtils
open Expecto
open DotNetEnv
open System
open Tonyx.SeatsBooking
open Tonyx.SeatsBooking.RowAggregateEvent
open Tonyx.SeatsBooking.RowConsumer
open Tonyx.SeatsBooking.SeatRow
open Tonyx.SeatsBooking.Entities
open Tonyx.SeatsBooking.Stadium
open Tonyx.SeatsBooking.StadiumEvents
open Tonyx.SeatsBooking.StorageStadiumBookingSystem
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting

Env.Load() |> ignore

let password = Environment.GetEnvironmentVariable("password")

let emptyMessageSenders =
    fun queueName ->
        fun message ->
            ValueTask.CompletedTask
            
let connection =
    "Server=127.0.0.1;"+
    "Database=es_seat_booking;" +
    "User Id=safe;"+
    $"Password={password};"
let memoryStorage = MemoryStorage.MemoryStorage()
let pgEventStore = PgEventStore(connection)
let memoryStadiumSystem = StadiumBookingSystem(memoryStorage, MessageSenders.NoSender)
let stadiumSystem = StadiumBookingSystem(pgEventStore, MessageSenders.NoSender)

let hostBuilder =
    Host.CreateDefaultBuilder()
        .ConfigureServices(fun (services: IServiceCollection) ->
            services.AddSingleton<RabbitMqReceiver>() |> ignore
            services.AddHostedService<RowConsumer>() |> ignore
        )

let host = hostBuilder.Build()
let hostTask = host.StartAsync()
let services = host.Services

let rowConsumer =
    host.Services.GetServices<IHostedService>()
    |> Seq.find (fun s -> s.GetType() = typeof<RowConsumer>)
    :?> RowConsumer

rowConsumer.SetFallbackAggregateStateRetriever (getAggregateStorageFreshStateViewer<SeatsRow, RowAggregateEvent, string> pgEventStore)
let rabbitMqSeatRowStateViewer = rowConsumer.GetAggregateState

let aggregateMessageSenders = System.Collections.Generic.Dictionary<string, MessageSender>()
let seatRowMessageSender =
    mkMessageSender "127.0.0.1" "_01_seatrow" |> Result.get

aggregateMessageSenders.Add("_01_seatrow", seatRowMessageSender)

let rabbitMQMessageSender =
    fun queueName ->
        let sender = aggregateMessageSenders.TryGetValue(queueName)
        match sender with
        | true, sender -> sender |> Ok
        | _ -> (sprintf "not found %s" queueName) |> Error

let rMessageSender =
    MessageSenders.MessageSender rabbitMQMessageSender
        
let pgReset () =
    pgEventStore.Reset Stadium.Version Stadium.StorageName
    pgEventStore.Reset SeatsRow.Version SeatsRow.StorageName
    pgEventStore.ResetAggregateStream SeatsRow.Version SeatsRow.StorageName
    StateCache2<Stadium>.Instance.Invalidate()
    AggregateCache3.Instance.Clear()
let memReset () =
    memoryStorage.Reset Stadium.Version Stadium.StorageName
    memoryStorage.Reset SeatsRow.Version SeatsRow.StorageName
    StateCache2<Stadium>.Instance.Invalidate()
    AggregateCache3.Instance.Clear()

[<Tests>]
let tests =
   
    let stadiumInstances =
        [
            #if RABBITMQ
            StadiumBookingSystem (pgEventStore, rMessageSender, getStorageFreshStateViewer<Stadium, StadiumEvent, string > pgEventStore, rabbitMqSeatRowStateViewer), pgReset, 100
            #else
            StadiumBookingSystem (pgEventStore, MessageSenders.NoSender, getStorageFreshStateViewer<Stadium, StadiumEvent, string > pgEventStore, getAggregateStorageFreshStateViewer<SeatsRow, RowAggregateEvent, string> pgEventStore), pgReset, 0
            #endif
        ]
        
    testList "samples" [
        multipleTestCase "initial state no seats X - Ok" stadiumInstances <| fun (stadiumSystem, setUp, delay) ->
            // Arrange
            setUp ()
            let service = stadiumSystem
            
            // Act
            let result = service.GetAllRowReferences()
            
            // Assert
            Expect.isOk result "should be ok"
            let rows = result.OkValue
            Expect.equal 0 rows.Length "should be 0"
            
        multipleTestCase "retrieve an unexisting row - Error" stadiumInstances <| fun (stadiumSystem, setUp, delay) ->
            // Arrange
            setUp ()
            let service = stadiumSystem
            
            // Act
            let result = service.GetRow (Guid.NewGuid())
            
            // Assert
            Expect.isError result "should be error"
       
        multipleTestCase "add a row reference and a seat to it. Retrieve the seat - Ok" stadiumInstances  <| fun (stadiumSystem, setUp, delay) ->
            // Arrange
            setUp ()
            let service = stadiumSystem
            
            // Act
            let rowId = Guid.NewGuid()
            let addRow = service.AddRow rowId
            Expect.isOk addRow "should be ok"
            
            let seat = { Id = 1; State = Free; RowId = None }
            let addSeat = service.AddSeat rowId seat
            Expect.isOk addSeat "should be ok"
            
            // Assert
            Thread.Sleep delay
            let result = service.GetRow rowId
            Expect.isOk result "should be ok"
            let row = result.OkValue
            Expect.equal row.Seats.Length 1 "should be equal"
       
        multipleTestCase "add a row reference and a seat to it. Retrieve the seat - Ok" stadiumInstances  <| fun (stadiumSystem, setUp, delay ) ->
            // Arrange
            setUp ()
            let service = stadiumSystem
            
            // Act
            let rowId = Guid.NewGuid()
            let addRow = service.AddRow rowId
            Expect.isOk addRow "should be ok"
            let seat = { Id = 1; State = Free; RowId = None }
            let addSeat = service.AddSeat rowId seat
            Expect.isOk addSeat "should be ok"
            
            // Assert
            Thread.Sleep delay
            let retrievedRow = stadiumSystem.GetRow rowId
            Expect.isOk retrievedRow "should be ok"
            let result = retrievedRow.OkValue
            Expect.equal result.Seats.Length 1 "should be 1"
       
        multipleTestCase "add a row and two seats - Ok" stadiumInstances <| fun (stadiumSystem, setUp, delay) ->
            // Arrange
            setUp ()
            let service = stadiumSystem
            let rowId = Guid.NewGuid()
            let addRow = service.AddRow rowId
            Expect.isOk addRow "should be ok"
            
            let seat = { Id = 1; State = Free; RowId = None }
            let seat2 = { Id = 2; State = Free; RowId = None }
            let addSeat = service.AddSeat rowId seat
            Expect.isOk addSeat "should be ok"
            let addSeat2 = service.AddSeat rowId seat2
            Expect.isOk addSeat2 "should be ok"
            
            // Assert
            Thread.Sleep delay
            let retrievedRow = service.GetRow rowId
            Expect.isOk retrievedRow "should be ok"
            let result = retrievedRow.OkValue
            Expect.equal result.Seats.Length 2 "should be 2"
        
        multipleTestCase "add a row reference and five seats to it one by one. Retrieve the seat - Ok" stadiumInstances <| fun (stadiumSystem, setUp, delay) ->
            // Arrange
            setUp ()
            let service = stadiumSystem
            let rowId = Guid.NewGuid()
            let addRow = service.AddRow rowId
            Expect.isOk addRow "should be ok"
            
            // Act
            let seat = { Id = 1; State = Free; RowId = None }
            let seat2 = { Id = 2; State = Free; RowId = None }
            let seat3 = { Id = 3; State = Free; RowId = None }
            let seat4 = { Id = 4; State = Free; RowId = None }
            let seat5 = { Id = 5; State = Free; RowId = None }
            let seat6 = { Id = 6; State = Free; RowId = None }
            
            let addSeat = service.AddSeat rowId seat
            Expect.isOk addSeat "should be ok"

            // Assert
            let _ = service.AddSeat rowId seat2
            let _ = service.AddSeat rowId seat3
            let _ = service.AddSeat rowId seat4
            let _ = service.AddSeat rowId seat5
            let _ = service.AddSeat rowId seat6
            
            Thread.Sleep delay
            let retrievedRow = service.GetRow rowId
            Expect.isOk retrievedRow "should be ok"
            let result = retrievedRow.OkValue
            Expect.equal result.Seats.Length 6 "should be 6"
            
        multipleTestCase "add a row reference and then some seats to it. Retrieve the seats - OK" stadiumInstances <| fun (stadiumSystem, setUp, delay)  ->
            // Arrange
            setUp ()
            let service = stadiumSystem
            let rowId = Guid.NewGuid()
            let addedRow = service.AddRow rowId
            Expect.isOk addedRow "should be ok"
            
            // act
            let seats =
                [
                    { Id = 1; State = Free; RowId = None }
                    { Id = 2; State = Free; RowId = None }
                    { Id = 3; State = Free; RowId = None }
                    { Id = 4; State = Free; RowId = None }
                    { Id = 5; State = Free; RowId = None }
                ]
            let seatAdded = service.AddSeats rowId seats
            Expect.isOk seatAdded "should be ok"

            // assert
            Thread.Sleep delay
            let retrievedRow = service.GetRow rowId
            Expect.isOk retrievedRow "should be ok"

            let okRetrievedRow = retrievedRow.OkValue
            Expect.equal okRetrievedRow.Seats.Length 5 "should be 5"
       
        multipleTestCase "add a row then, an a single seat then another seat " stadiumInstances <| fun (stadiumSystem, setUp, delay) ->
            setUp ()
            let service = stadiumSystem
            let rowId = Guid.NewGuid()
            let addedRow = service.AddRow rowId
            Expect.isOk addedRow "should be ok"
            
            let seat = { Id = 1; State = Free; RowId = None }
            let seatAdded = service.AddSeat rowId seat
            Expect.isOk seatAdded "should be ok"
            
            let seat2 = { Id = 2; State = Free; RowId = None }
            let seatAdded2 = service.AddSeat rowId seat2
            Expect.isOk seatAdded2 "should be ok"
            
        multipleTestCase "add a row then, a single seat (as a list of one element), then another seat (as a list of one element) - Ok" stadiumInstances <| fun (stadiumSystem, setUp, delay) ->
            setUp ()
            let service = stadiumSystem
            let rowId = Guid.NewGuid()
            let addedRow = service.AddRow rowId
            Expect.isOk addedRow "should be ok"
            
            let seats = [{ Id = 1; State = Free; RowId = None }]
            let seatAdded = service.AddSeats rowId seats
            Expect.isOk seatAdded "should be ok"
            
            let seats2 = [{ Id = 2; State = Free; RowId = None }]
            let seatAdded2 = service.AddSeats rowId seats2
            Expect.isOk seatAdded2 "should be ok"
        
        multipleTestCase "add a row, then attach a list of a single seat and then a list of two seats - Ok" stadiumInstances <| fun (stadiumSystem, setUp, delay) ->
            setUp ()
            let service = stadiumSystem
            let rowId = Guid.NewGuid()
            let addedRow = service.AddRow rowId
            Expect.isOk addedRow "should be ok"
            
            let seats = [{ Id = 1; State = Free; RowId = None }]
            let seatAdded = service.AddSeats rowId seats
            Expect.isOk seatAdded "should be ok"
            
            Thread.Sleep delay
            let seats2 = [
                { Id = 2; State = Free; RowId = None }
                { Id = 3; State = Free; RowId = None }
            ]
            Thread.Sleep delay
            let seatAdded2 = service.AddSeats rowId seats2
            Expect.isOk seatAdded2 "should be ok"
            Thread.Sleep delay
            let retrieveRow = service.GetRow rowId
            Expect.isOk retrieveRow "should be ok"
            let okRetrieveRow = retrieveRow.OkValue
            Expect.equal okRetrieveRow.Seats.Length 3 "should be 3"
        
        multipleTestCase "add a row, then attach a list of two seats and then a list of two seats again - Ok" stadiumInstances <| fun (stadiumSystem, setUp, delay) ->
            setUp ()
            let service = stadiumSystem
            let rowId = Guid.NewGuid()
            let addedRow = service.AddRow rowId
            Expect.isOk addedRow "should be ok"
            
            let seats = [
                { Id = 1; State = Free; RowId = None }
                { Id = 2; State = Free; RowId = None }
            ]
            Thread.Sleep delay
            let seatAdded = service.AddSeats rowId seats
            Expect.isOk seatAdded "should be ok"
            
            let seats2 = [
                { Id = 3; State = Free; RowId = None }
                { Id = 4; State = Free; RowId = None }
            ]
            Thread.Sleep delay
            let seatAdded2 = service.AddSeats rowId seats2
            Expect.isOk seatAdded2 "should be ok"
            Thread.Sleep delay
            let retrieveRow = service.GetRow rowId
            Expect.isOk retrieveRow "should be ok"
            let okRetrieveRow = retrieveRow.OkValue
            Expect.equal okRetrieveRow.Seats.Length 4 "should be 4"
        
        multipleTestCase "add a row, then attach a list of three seats and then a list of three seats again - Ok" stadiumInstances <| fun (stadiumSystem, setUp, delay) ->
            setUp ()
            let service = stadiumSystem
            let rowId = Guid.NewGuid()
            let addedRow = service.AddRow rowId
            Expect.isOk addedRow "should be ok"
            
            let seats = [
                { Id = 1; State = Free; RowId = None }
                { Id = 2; State = Free; RowId = None }
                { Id = 3; State = Free; RowId = None }
            ]
            let seatAdded = service.AddSeats rowId seats
            Expect.isOk seatAdded "should be ok"
            
            let seats2 = [
                { Id = 4; State = Free; RowId = None }
                { Id = 5; State = Free; RowId = None }
                { Id = 6; State = Free; RowId = None }
            ]
            let seatAdded2 = service.AddSeats rowId seats2
            Expect.isOk seatAdded2 "should be ok"
            Thread.Sleep delay
            let retrieveRow = service.GetRow rowId
            Expect.isOk retrieveRow "should be ok"
            let okRetrieveRow = retrieveRow.OkValue
            Expect.equal okRetrieveRow.Seats.Length 6 "should be 4"
            
        multipleTestCase "add a row, then attach a list of fours seats and then a list of fours seats again - Ok" stadiumInstances <| fun (stadiumSystem, setUp, delay) ->
            setUp ()
            let service = stadiumSystem
            let rowId = Guid.NewGuid()
            let addedRow = service.AddRow rowId
            Expect.isOk addedRow "should be ok"
            
            let seats = [
                { Id = 1; State = Free; RowId = None }
                { Id = 2; State = Free; RowId = None }
                { Id = 3; State = Free; RowId = None }
                { Id = 4; State = Free; RowId = None }
            ]
            let seatAdded = service.AddSeats rowId seats
            Expect.isOk seatAdded "should be ok"
            
            let seats2 = [
                { Id = 5; State = Free; RowId = None }
                { Id = 6; State = Free; RowId = None }
                { Id = 7; State = Free; RowId = None }
                { Id = 8; State = Free; RowId = None }
            ]
            let seatAdded2 = service.AddSeats rowId seats2
            Expect.isOk seatAdded2 "should be ok"
            Thread.Sleep delay
            let retrieveRow = service.GetRow rowId
            Expect.isOk retrieveRow "should be ok"
            let okRetrieveRow = retrieveRow.OkValue
            Expect.equal okRetrieveRow.Seats.Length 8 "should be 4"
            
        multipleTestCase "add a row, then attach a list of five seats and then a list of five seats again - Ok" stadiumInstances <| fun (stadiumSystem, setUp, delay) ->
            setUp ()
            let service = stadiumSystem
            let rowId = Guid.NewGuid()
            let addedRow = service.AddRow rowId
            Expect.isOk addedRow "should be ok"
            
            let seats = [
                { Id = 1; State = Free; RowId = None }
                { Id = 2; State = Free; RowId = None }
                { Id = 3; State = Free; RowId = None }
                { Id = 4; State = Free; RowId = None }
                { Id = 5; State = Free; RowId = None }
            ]
            let seatAdded = service.AddSeats rowId seats
            Expect.isOk seatAdded "should be ok"
            
            let seats2 = [
                { Id = 6; State = Free; RowId = None }
                { Id = 7; State = Free; RowId = None }
                { Id = 8; State = Free; RowId = None }
                { Id = 9; State = Free; RowId = None }
                { Id = 10; State = Free; RowId = None }
            ]
            let seatAdded2 = service.AddSeats rowId seats2
            Expect.isOk seatAdded2 "should be ok"
            Thread.Sleep delay
            let retrieveRow = service.GetRow rowId
            Expect.isOk retrieveRow "should be ok"
            let okRetrieveRow = retrieveRow.OkValue
            Expect.equal okRetrieveRow.Seats.Length 10 "should be 10"
            
        multipleTestCase "add a two rows and then add separately a seat for each row - Ok" stadiumInstances <| fun (stadiumSystem, setUp, delay) ->
            setUp ()
            let service = stadiumSystem
            let rowId = Guid.NewGuid()
            let addedRow = service.AddRow rowId
            Expect.isOk addedRow "should be ok"
            let rowId2 = Guid.NewGuid()
            let addedRow2 = service.AddRow rowId2
            Expect.isOk addedRow2 "should be ok"
            let seatAdded = service.AddSeat rowId { Id = 1; State = Free; RowId = None }
            Expect.isOk seatAdded "should be ok"
            Thread.Sleep delay
            let seatAdded2 = service.AddSeat rowId2 { Id = 2; State = Free; RowId = None }
            Expect.isOk seatAdded2 "should be ok"
            
            Thread.Sleep delay
            let retrieveRow = service.GetRow rowId
            let retrieveRow2 = service.GetRow rowId2
            Expect.isOk retrieveRow "should be ok"
            let okRetrieveRow = retrieveRow.OkValue
            let okRetrieveRow2 = retrieveRow2.OkValue
            Expect.equal okRetrieveRow.Seats.Length 1 "should be 1"
            Expect.equal okRetrieveRow2.Seats.Length 1 "should be 1"   
            
        multipleTestCase "add two row references add a row reference and then some seats to it. Retrieve the seats then - Ok" stadiumInstances  <| fun (stadiumSystem, setUp, delay)  ->
            // Arrange
            setUp ()
            let service = stadiumSystem
            let rowId = Guid.NewGuid()
            let addedRow = service.AddRow rowId
            Expect.isOk addedRow "should be ok"

            let rowId2 = Guid.NewGuid()
            let addedRow2 = service.AddRow rowId2
            Expect.isOk addedRow2 "should be ok"

            // Act
            let seats = [
                { Id = 1; State = Free; RowId = None }
                { Id = 2; State = Free; RowId = None }
                { Id = 3; State = Free; RowId = None }
                { Id = 4; State = Free; RowId = None }
                { Id = 5; State = Free; RowId = None }
            ]
            let seatAdded = service.AddSeats rowId seats

            Expect.isOk seatAdded "should be ok"
            let seats2 = [
                        { Id = 6; State = Free; RowId = None }
                        { Id = 7; State = Free; RowId = None }
                        { Id = 8; State = Free; RowId = None }
                        { Id = 9; State = Free; RowId = None }
                        { Id = 10; State = Free; RowId = None }
                        ]
            let seatsAdded2 = service.AddSeats rowId2 seats2
            Expect.isOk seatsAdded2 "should be ok"

            // Assert
            Thread.Sleep delay
            let retrievedRow = service.GetRow rowId
            Expect.isOk retrievedRow "should be ok"
            let okRetrievedRow = retrievedRow.OkValue
            Expect.equal 5 okRetrievedRow.Seats.Length "should be 5"

            let retrievedRow2 = service.GetRow rowId2
            Expect.isOk retrievedRow2 "should be ok"
            let okRetrievedRow2 = retrievedRow2.OkValue
            Expect.equal 5 okRetrievedRow2.Seats.Length "should be 5"
            
        multipleTestCase "can't add a seat with the same id of another seat in the same row - Ok" stadiumInstances <| fun (stadiumSystem, setUp, delay)  ->
            // Arrange
            setUp()

            let rowId = Guid.NewGuid()
            let addedRow = stadiumSystem.AddRow rowId
            Expect.isOk addedRow "should be ok"
            // when
            let seat =  { Id = 1; State = Free; RowId = None }
            let seatAdded = stadiumSystem.AddSeat rowId seat
            Expect.isOk seatAdded "should be ok"
            let seat2 = { Id = 1; State = Free; RowId = None }
            Thread.Sleep delay
            let seatAdded2 = stadiumSystem.AddSeat rowId seat2
            Expect.isError seatAdded2 "should be error"
            
        multipleTestCase "add a booking on an unexisting row - Error" stadiumInstances  <| fun (stadiumSystem, setUp, delay) ->
            // Arrange
            setUp()

            let booking = { Id = 1; SeatIds = [1]}
            let rowId = Guid.NewGuid()
            
            // Act
            let tryBooking = stadiumSystem.BookSeats rowId booking
            
            // Assert
            Expect.isError tryBooking "should be error"
            let (Error e ) = tryBooking
            Expect.equal e (sprintf "There is no aggregate of version \"_01\", name \"_seatrow\" with id %A" rowId) "should be equal"
            
        multipleTestCase "add a booking on an existing row and unexisting seat - Error" stadiumInstances <| fun (stadiumSystem, setUp, delay) ->
            // Arrange
            setUp()
            let rowId = Guid.NewGuid()

            // Act
            let addedRow = stadiumSystem.AddRow rowId
            Expect.isOk addedRow "should be ok"
            let booking = { Id = 1; SeatIds = [1]}

            // Assert
            Thread.Sleep delay
            let tryBooking = stadiumSystem.BookSeats rowId booking
            Expect.isError tryBooking "should be error"
            let (Error e ) = tryBooking
            Expect.equal e "Seat not found" "should be equal"
       
        multipleTestCase "add a booking on a valid row and valid seat - Ok" stadiumInstances <| fun (stadiumSystem, setUp, delay) ->
            // Arrange
            setUp ()

            let rowId = Guid.NewGuid()
            let addedRow = stadiumSystem.AddRow rowId
            Expect.isOk addedRow "should be ok"

            // Act
            let seat = { Id = 1; State = Free; RowId = None }
            let seatAdded = stadiumSystem.AddSeat rowId seat
            Expect.isOk seatAdded "should be ok"
            let booking = { Id = 1; SeatIds = [1]}

            // Assert
            let tryBooking = stadiumSystem.BookSeats rowId booking
            Expect.isOk tryBooking "should be ok"
            
        multipleTestCase "can't book an already booked seat - Error" stadiumInstances <| fun (stadiumSystem, setUp, delay) ->
            // Arrange
            setUp ()

            // Act
            let rowId = Guid.NewGuid()
            let addedRow = stadiumSystem.AddRow rowId
            Expect.isOk addedRow "should be ok"
            let seat = { Id = 1; State = Free; RowId = None }
            let seatAdded = stadiumSystem.AddSeat rowId seat
            Expect.isOk seatAdded "should be ok"
            let booking = { Id = 1; SeatIds = [1]}
            
            Thread.Sleep delay
            let tryBooking = stadiumSystem.BookSeats rowId booking
            Expect.isOk tryBooking "should be ok"

            // Assert
            let tryBookingAgain = stadiumSystem.BookSeats rowId booking
            Expect.isError tryBookingAgain "should be error"
            let (Error e) = tryBookingAgain
            Expect.equal e "Seat already booked" "should be equal"
            
        multipleTestCase "add many seats and book one of them - Ok" stadiumInstances  <| fun (stadiumSystem, setUp, delay) ->
            // Arrange
            setUp()

            // Act
            let rowId = Guid.NewGuid()
            let addedRow = stadiumSystem.AddRow rowId
            Expect.isOk addedRow "should be ok"
            let seats = [
                { Id = 1; State = Free; RowId = None }
                { Id = 2; State = Free; RowId = None }
            ]
            Thread.Sleep delay
            let seatsAdded = stadiumSystem.AddSeats rowId seats
            Expect.isOk seatsAdded "should be ok"

            // Assert
            let booking = { Id = 1; SeatIds = [1]}
            Thread.Sleep delay
            let tryBooking = stadiumSystem.BookSeats rowId booking
            Expect.isOk tryBooking "should be ok"
            
        multipleTestCase "violate the middle seat non empty constraint in one single booking - Ok" stadiumInstances <| fun (stadiumSystem, setUp, delay) ->
            // Arrange
            setUp()

            let rowId = Guid.NewGuid()
            let invariantId = Guid.NewGuid()
            let middleSeatInvariant: Invariant<SeatsRow>  =
                {
                    Id = invariantId
                    Expression =
                        <@
                            fun (seatsRow: SeatsRow) ->
                                let seats: List<Seat> = seatsRow.Seats
                                (
                                    seats.Length = 5 &&
                                    seats.[0].State = SeatState.Booked &&
                                    seats.[1].State = SeatState.Booked &&
                                    seats.[2].State = SeatState.Free &&
                                    seats.[3].State = SeatState.Booked &&
                                    seats.[4].State = SeatState.Booked)
                                |> not
                                |> Result.ofBool "error: can't leave a single seat free in the middle"
                        @>
                }
            let middleSeatInvariantContainer = InvariantContainer.Build middleSeatInvariant
            let addedRow = stadiumSystem.AddRow rowId
            Expect.isOk addedRow "should be ok"

            let addedRule = stadiumSystem.AddInvariant rowId middleSeatInvariantContainer
            Expect.isOk addedRule "should be ok"

            let seats = [
                { Id = 1; State = Free; RowId = None }
                { Id = 2; State = Free; RowId = None }
                { Id = 3; State = Free; RowId = None }
                { Id = 4; State = Free; RowId = None }
                { Id = 5; State = Free; RowId = None }
            ]
            let seatsAdded = stadiumSystem.AddSeats rowId seats
            Expect.isOk seatsAdded "should be ok"

            // Act
            Thread.Sleep delay
            let booking = { Id = 1; SeatIds = [1; 2; 4; 5]}
            let tryBooking = stadiumSystem.BookSeats rowId booking

            // Assert
            Expect.isError tryBooking "should be error"
            let (Error e) = tryBooking
            Expect.equal e "error: can't leave a single seat free in the middle" "should be equal"
            
        multipleTestCase "add an invariant then remove it - OK" stadiumInstances <| fun (stadiumSystem, setUp, delay) ->
            // Arrange
            setUp()

            let rowId = Guid.NewGuid()
            let invariantId = Guid.NewGuid()
            let middleSeatInvariant: Invariant<SeatsRow>  =
                {
                    Id = invariantId
                    Expression =
                        <@
                            fun (seatsRow: SeatsRow) ->
                                let seats: List<Seat> = seatsRow.Seats
                                ((
                                    seats.Length = 5 &&
                                    seats.[0].State = SeatState.Booked &&
                                    seats.[1].State = SeatState.Booked &&
                                    seats.[2].State = SeatState.Free &&
                                    seats.[3].State = SeatState.Booked &&
                                    seats.[4].State = SeatState.Booked)
                                |> not)
                                |> Result.ofBool "error: can't leave a single seat free in the middle"
                        @>
                }
            let middleSeatInvariantContainer = InvariantContainer.Build middleSeatInvariant
            let addedRow = stadiumSystem.AddRow rowId
            Expect.isOk addedRow "should be ok"

            Thread.Sleep delay
            let addedRule = stadiumSystem.AddInvariant rowId middleSeatInvariantContainer
            Expect.isOk addedRule "should be ok"

            Thread.Sleep delay
            let removedRule = stadiumSystem.RemoveInvariant rowId middleSeatInvariantContainer
            Expect.isOk removedRule "should be ok"

            let seats = [
                { Id = 1; State = Free; RowId = None }
                { Id = 2; State = Free; RowId = None }
                { Id = 3; State = Free; RowId = None }
                { Id = 4; State = Free; RowId = None }
                { Id = 5; State = Free; RowId = None }
            ]
            Thread.Sleep delay
            let seatsAdded = stadiumSystem.AddSeats rowId seats
            Expect.isOk seatsAdded "should be ok"

            // Act
            let booking = { Id = 1; SeatIds = [1; 2; 4; 5]}
            let tryBooking = stadiumSystem.BookSeats rowId booking

            // Assert
            Expect.isOk tryBooking "should be ok"
            
        multipleTestCase "if there is no invariant/contraint then can book seats leaving the only middle seat unbooked - Ok" stadiumInstances  <| fun (stadiumSystem, setUp, delay) ->
            // Arrange
            setUp()

            let rowId = Guid.NewGuid()
            let addedRow = stadiumSystem.AddRow rowId
            Expect.isOk addedRow "should be ok"
            let seats = [
                { Id = 1; State = Free; RowId = None }
                { Id = 2; State = Free; RowId = None }
                { Id = 3; State = Free; RowId = None }
                { Id = 4; State = Free; RowId = None }
                { Id = 5; State = Free; RowId = None }
            ]
            Thread.Sleep delay
            let seatsAdded = stadiumSystem.AddSeats rowId seats
            Expect.isOk seatsAdded "should be ok"
            
            // Act
            let booking = { Id = 1; SeatIds = [1;2;4;5]}
            Thread.Sleep delay
            let tryBooking = stadiumSystem.BookSeats rowId booking

            // Assert
            Expect.isOk tryBooking "should be ok"
            
        multipleTestCase "book free seats among two rows, one fails, so it makes fail them all - Error" stadiumInstances <| fun (stadiumSystem, setUp, delay) ->
            
            // Arrange
            setUp()

            let rowId1 = Guid.NewGuid()
            let rowId2 = Guid.NewGuid()
            let invariantId = Guid.NewGuid()
            let middleSeatNotFreeRule: Invariant<SeatsRow> =
                {
                    Id = invariantId
                    Expression =
                        <@
                            fun (seatsRow: SeatsRow) ->
                                let seats: List<Seat> = seatsRow.Seats
                                (
                                    seats.Length = 5 &&
                                    seats.[0].State = SeatState.Booked &&
                                    seats.[1].State = SeatState.Booked &&
                                    seats.[2].State = SeatState.Free &&
                                    seats.[3].State = SeatState.Booked &&
                                    seats.[4].State = SeatState.Booked)
                                |> not
                                |> Result.ofBool "error: can't leave a single seat free in the middle"
                        @>
                }
            let invariantContainer = InvariantContainer.Build middleSeatNotFreeRule

            Thread.Sleep delay
            let addedRow1 = stadiumSystem.AddRow rowId1
            Expect.isOk addedRow1 "should be ok"

            let addInvariantToRow1 = stadiumSystem.AddInvariant rowId1 invariantContainer
            Expect.isOk addInvariantToRow1 "should be ok"

            Thread.Sleep delay
            let addedRow2 = stadiumSystem.AddRow rowId2
            Expect.isOk addedRow2 "should be ok"

            Thread.Sleep delay
            let addInvariantToRow2 = stadiumSystem.AddInvariant rowId2 invariantContainer
            Expect.isOk addInvariantToRow2 "should be ok"
            let seats1 = [
                { Id = 1; State = Free; RowId = None }
                { Id = 2; State = Free; RowId = None }
                { Id = 3; State = Free; RowId = None }
                { Id = 4; State = Free; RowId = None }
                { Id = 5; State = Free; RowId = None }
            ]
            Thread.Sleep delay
            let seatsAdded1 = stadiumSystem.AddSeats rowId1 seats1
            Expect.isOk seatsAdded1 "should be ok"
            let seats2 = [
                { Id = 6; State = Free; RowId = None }
                { Id = 7; State = Free; RowId = None }
                { Id = 8; State = Free; RowId = None }
                { Id = 9; State = Free; RowId = None }
                { Id = 10; State = Free; RowId = None }
            ]
            let seatsAdded2 = stadiumSystem.AddSeats rowId2 seats2
            Expect.isOk seatsAdded2 "should be ok"

            // Act
            let booking1 = { Id = 1; SeatIds = [1;2;4;5]} // invariant violated
            let booking2 = { Id = 2; SeatIds = [6;7;8;9;10]}
            let tryMultiBooking = stadiumSystem.BookSeatsNRows [(rowId1, booking1); (rowId2, booking2)]
            Expect.isError tryMultiBooking "should be error"
            let (Error e) = tryMultiBooking
            Expect.equal e "error: can't leave a single seat free in the middle" "should be equal"

            // Assert
            let newBooking1 = {Id = 1; SeatIds = [1; 4; 5]}
            let newBooking2 = {Id = 2; SeatIds = [6; 7; 8; 9; 10]}
            let tryMultiBookingAgain = stadiumSystem.BookSeatsNRows [(rowId1, newBooking1); (rowId2, newBooking2)]
            Expect.isOk tryMultiBookingAgain "should be ok"
            
        multipleTestCase "if there is no invariant/contraint then can book seats leaving the only middle seat unbooked - Ok" stadiumInstances  <| fun (stadiumSystem, setUp, delay) ->
            // Arrange
            setUp()

            let rowId = Guid.NewGuid()
            let addedRow = stadiumSystem.AddRow rowId
            Expect.isOk addedRow "should be ok"
            let seats = [
                { Id = 1; State = Free; RowId = None }
                { Id = 2; State = Free; RowId = None }
                { Id = 3; State = Free; RowId = None }
                { Id = 4; State = Free; RowId = None }
                { Id = 5; State = Free; RowId = None }
            ]
            Thread.Sleep delay
            let seatsAdded = stadiumSystem.AddSeats rowId seats
            Expect.isOk seatsAdded "should be ok"
            let booking = { Id = 1; SeatIds = [1;2;4;5]}
            // Act
            let booking = { Id = 1; SeatIds = [1;2;4;5]}
            let tryBooking = stadiumSystem.BookSeats rowId booking

            // Assert
            Expect.isOk tryBooking "should be ok"
          
        multipleTestCase "book free seats among two rows, one fails, so it makes fail them all - Error" stadiumInstances <| fun (stadiumSystem, setUp, delay) ->
            // Arrange
            setUp()

            let rowId1 = Guid.NewGuid()
            let rowId2 = Guid.NewGuid()
            let invariantId = Guid.NewGuid()
            let middleSeatNotFreeRule: Invariant<SeatsRow> =
                {
                    Id = invariantId
                    Expression =
                        <@
                            fun (seatsRow: SeatsRow) ->
                                let seats: List<Seat> = seatsRow.Seats
                                (
                                    seats.Length = 5 &&
                                    seats.[0].State = SeatState.Booked &&
                                    seats.[1].State = SeatState.Booked &&
                                    seats.[2].State = SeatState.Free &&
                                    seats.[3].State = SeatState.Booked &&
                                    seats.[4].State = SeatState.Booked)
                                |> not
                                |> Result.ofBool "error: can't leave a single seat free in the middle"
                        @>
                }
            let invariantContainer = InvariantContainer.Build middleSeatNotFreeRule

            Thread.Sleep delay
            let addedRow1 = stadiumSystem.AddRow rowId1
            Expect.isOk addedRow1 "should be ok"

            let addInvariantToRow1 = stadiumSystem.AddInvariant rowId1 invariantContainer
            Expect.isOk addInvariantToRow1 "should be ok"

            Thread.Sleep delay
            let addedRow2 = stadiumSystem.AddRow rowId2
            Expect.isOk addedRow2 "should be ok"

            let addInvariantToRow2 = stadiumSystem.AddInvariant rowId2 invariantContainer
            Expect.isOk addInvariantToRow2 "should be ok"
            let seats1 = [
                { Id = 1; State = Free; RowId = None }
                { Id = 2; State = Free; RowId = None }
                { Id = 3; State = Free; RowId = None }
                { Id = 4; State = Free; RowId = None }
                { Id = 5; State = Free; RowId = None }
            ]
            Thread.Sleep delay
            let seatsAdded1 = stadiumSystem.AddSeats rowId1 seats1
            Expect.isOk seatsAdded1 "should be ok"
            let seats2 = [
                { Id = 6; State = Free; RowId = None }
                { Id = 7; State = Free; RowId = None }
                { Id = 8; State = Free; RowId = None }
                { Id = 9; State = Free; RowId = None }
                { Id = 10; State = Free; RowId = None }
            ]
            Thread.Sleep delay
            let seatsAdded2 = stadiumSystem.AddSeats rowId2 seats2
            Expect.isOk seatsAdded2 "should be ok"

            // Act
            let booking1 = { Id = 1; SeatIds = [1;2;4;5]} // invariant violated
            let booking2 = { Id = 2; SeatIds = [6;7;8;9;10]}
            let tryMultiBooking = stadiumSystem.BookSeatsNRows [(rowId1, booking1); (rowId2, booking2)]
            Expect.isError tryMultiBooking "should be error"
            let (Error e) = tryMultiBooking
            Expect.equal e "error: can't leave a single seat free in the middle" "should be equal"

            // Assert
            let newBooking1 = {Id = 1; SeatIds = [1; 4; 5]}
            let newBooking2 = {Id = 2; SeatIds = [6; 7; 8; 9; 10]}
            let tryMultiBookingAgain = stadiumSystem.BookSeatsNRows [(rowId1, newBooking1); (rowId2, newBooking2)]
            Expect.isOk tryMultiBookingAgain "should be ok"
            
        multipleTestCase "add a seats in one row and two seat in another row, then a seat again in first row - Ok" stadiumInstances <| fun (stadiumSystem, setUp, delay) ->
            // Arrange
            setUp()

            let rowId1 = Guid.NewGuid()
            let rowId2 = Guid.NewGuid()

            Thread.Sleep delay
            let addedRow1 = stadiumSystem.AddRow rowId1
            Expect.isOk addedRow1 "should be ok"

            Thread.Sleep delay
            let addedRow2 = stadiumSystem.AddRow rowId2
            Expect.isOk addedRow2 "should be ok"

            let seat1 = { Id = 1; State = Free; RowId = None }
            let seat2 = { Id = 6; State = Free; RowId = None }
            let seat3 = { Id = 7; State = Free; RowId = None }

            let seat4 = { Id = 2; State = Free; RowId = None }

            Thread.Sleep delay
            let addAllSeats = stadiumSystem.AddSeatsToRows [(rowId1, [seat1]); (rowId2, [seat2; seat3])]
            Expect.isOk addAllSeats "should be Ok"

            Thread.Sleep delay
            // Act
            let addSingleSeatAgain = stadiumSystem.AddSeat rowId1 seat4
            Expect.isOk addSingleSeatAgain "should be Ok"

            Thread.Sleep delay
            // Assert
            let retrievedRow1 = stadiumSystem.GetRow rowId1
            let retrievedRow2 = stadiumSystem.GetRow rowId2

            Expect.equal retrievedRow1.OkValue.Seats.Length 2 "should be equal"
            Expect.equal retrievedRow2.OkValue.Seats.Length 2 "should be equal"
            
        multipleTestCase "A single booking cannot book all seats involving three rows or more - Error" stadiumInstances <| fun (stadiumSystem, setUp, delay) ->
            // Arrange
            setUp()

            let rowId1 = Guid.NewGuid()
            let rowId2 = Guid.NewGuid()
            let rowId3 = Guid.NewGuid()

            let addedRow1 = stadiumSystem.AddRow rowId1
            Expect.isOk addedRow1 "should be ok"
            let addedRow2 = stadiumSystem.AddRow rowId2
            Expect.isOk addedRow2 "should be ok"
            let addRow3 = stadiumSystem.AddRow rowId3
            Expect.isOk addRow3 "should be ok"
            let seats1 = [
                { Id = 1; State = Free; RowId = None }
                { Id = 2; State = Free; RowId = None }
                { Id = 3; State = Free; RowId = None }
                { Id = 4; State = Free; RowId = None }
                { Id = 5; State = Free; RowId = None }
            ]
            let seatsAdded1 = stadiumSystem.AddSeats rowId1 seats1
            Expect.isOk seatsAdded1 "should be ok"
            let seats2 = [
                { Id = 6; State = Free; RowId = None }
                { Id = 7; State = Free; RowId = None }
                { Id = 8; State = Free; RowId = None }
                { Id = 9; State = Free; RowId = None }
                { Id = 10; State = Free; RowId = None }
            ]
            let seatsAdded2 = stadiumSystem.AddSeats rowId2 seats2
            Expect.isOk seatsAdded2 "should be ok"

            let seats3 = [
                { Id = 11; State = Free; RowId = None }
                { Id = 12; State = Free; RowId = None }
                { Id = 13; State = Free; RowId = None }
                { Id = 14; State = Free; RowId = None }
                { Id = 15; State = Free; RowId = None }
            ]
            // Act
            let seatsAdded3 = stadiumSystem.AddSeats rowId3 seats3
            Expect.isOk seatsAdded3 "should be ok"
            let booking1 = { Id = 1; SeatIds = [1;2;3;4;5]}
            let booking2 = { Id = 2; SeatIds = [6;7;8;9;10]}
            let booking3 = { Id = 3; SeatIds = [11;12;13;14;15]}

            // Assert
            let tryMultiBooking = stadiumSystem.BookSeatsNRows [(rowId1, booking1); (rowId2, booking2); (rowId3, booking3)]
            // now make a valid booking on both
            Expect.isError tryMultiBooking "should be error"
            
    ]
    |> testSequenced
