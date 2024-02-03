
module SeatBookingKafkaTests

open Tonyx.SeatsBooking.Seats
open Tonyx.SeatsBooking.SeatRow
open Tonyx.SeatsBooking
// open Tonyx.SeatsBooking.StadiumBookingSystem
open Tonyx.SeatsBooking.StorageStadiumBookingSystem
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
open System
open Sharpino.KafkaReceiver
open Sharpino.KafkaBroker

[<Tests>]
let storageEventsTests =
    let connection = 
        "Server=127.0.0.1;"+
        "Database=es_seat_booking;" +
        "User Id=safe;"+
        "Password=safe;"
        
    let pgStorage = PgStorage.PgEventStore(connection)
    let localhostBroker = KafkaBroker.getKafkaBroker("localhost:9092", pgStorage)
    
    testList "seatBookingKafkaTests" [
        testCase "postgres storage: create a row with no seats and retrieve it - Ok" <| fun _ ->
            let eventStore = pgStorage
            eventStore.Reset "_01" "_seatrow"
            eventStore.Reset "_01" "_stadium"
            
            StateCache<Stadium>.Instance.Clear()
            AggregateCache<SeatsRow>.Instance.Clear()
            
            let rowId = Guid.NewGuid()
            let row = SeatsRow rowId
            let rowStorageCreation = row.Serialize serializer
            let stored = (eventStore :> IEventStore).SetInitialAggregateState rowId "_01" "_seatrow" rowStorageCreation
            Expect.isOk stored "should be ok"
            
            let rowStateViewer = getAggregateStorageFreshStateViewer<SeatsRow, RowAggregateEvent> eventStore
            let gotState = rowStateViewer rowId
            Expect.isOk gotState "should be ok"
            let (_, state, _, _) = gotState |> Result.get
            Expect.equal (state.Seats.Length) 0 "should be 0"

        testCase "postgres storage: initialize a row, and receive the corresponding event - Ok" <| fun _ ->
            let eventStore = pgStorage
            eventStore.Reset "_01" "_seatrow"
            eventStore.Reset "_01" "_stadium"
            StateCache<Stadium>.Instance.Clear()
            AggregateCache<SeatsRow>.Instance.Clear()

            let stadiumSubscriber = KafkaSubscriber.Create("localhost:9092", "_01", "_stadium", "sharpinoTestClient") |> Result.get

            let stadiumBookingSystem = StadiumBookingSystem(eventStore, localhostBroker)
            let rowId = Guid.NewGuid()

            let addRowReference = stadiumBookingSystem.AddRowReference rowId
            let deliveryResult = addRowReference.OkValue |> snd |> List.head |> Option.get |> List.head
            let partition = deliveryResult.Partition
            let position = deliveryResult.Offset

            stadiumSubscriber.Assign(position, partition)

            Expect.isOk addRowReference "should be ok"

            let consumeResult = stadiumSubscriber.Consume()
            let message = consumeResult.Message.Value
            
            let brokerMessage = message |> serializer.Deserialize<BrokerMessage> |> Result.get
            let event = brokerMessage.Event |> serializer.Deserialize<StadiumEvent> |> Result.get
            let expected = StadiumEvents.StadiumEvent.RowReferenceAdded rowId

            Expect.equal event expected "should be equal"

        testCase "Add a seat to a row and retrieve the related event from kafka - Ok" <| fun _ ->
            let eventStore = pgStorage
            eventStore.Reset "_01" "_seatrow"
            eventStore.Reset "_01" "_stadium"
            StateCache<Stadium>.Instance.Clear()
            AggregateCache<SeatsRow>.Instance.Clear()

            let rowSubscriber = KafkaSubscriber.Create("localhost:9092", "_01", "_seatrow", "sharpinoTestClient") |> Result.get
            let stadiumSubscriber = KafkaSubscriber.Create("localhost:9092", "_01", "_stadium", "sharpinoTestClient") |> Result.get

            let stadiumBookingSystem = StadiumBookingSystem(eventStore, localhostBroker)
            let rowId = Guid.NewGuid()

            let addRowReference = stadiumBookingSystem.AddRowReference rowId
            Expect.isOk addRowReference "should be ok"
           
            let seat = { Id = 1; State = Free; RowId = None } 
            let addSeat = stadiumBookingSystem.AddSeat rowId seat
            Expect.isOk addSeat "should be ok"
            
            let seatAdded = addSeat.OkValue |> snd |> List.head |> Option.get |> List.head
            
            let partition = seatAdded.Partition
            let position = seatAdded.Offset
            rowSubscriber.Assign(position, partition)
            let consumeResult = rowSubscriber.Consume()
            let message = consumeResult.Message.Value
            
            let brokerMessage = message |> serializer.Deserialize<BrokerAggregateMessage> |> Result.get
            let event = brokerMessage.Event |> serializer.Deserialize<RowAggregateEvent> |> Result.get
            let expected =
                RowAggregateEvent.SeatAdded
                    {
                        seat with RowId = Some rowId
                    }
            Expect.equal event expected "should be equal"

        testCase "Add a seat to a row and get the state using kafka viewer - Ok" <| fun _ ->
            let eventStore = pgStorage
            eventStore.Reset "_01" "_seatrow"
            eventStore.Reset "_01" "_stadium"
            eventStore.ResetAggregateStream "_01" "_seatrow"
            StateCache<Stadium>.Instance.Clear()
            AggregateCache<SeatsRow>.Instance.Clear()

            let rowSubscriber = KafkaSubscriber.Create("localhost:9092", "_01", "_seatrow", "sharpinoTestClient") |> Result.get

            let stadiumBookingSystem = StadiumBookingSystem(eventStore, localhostBroker)
            let rowId = Guid.NewGuid()

            let addRowReference = stadiumBookingSystem.AddRowReference rowId
            Expect.isOk addRowReference "should be ok"
           
            let seat = { Id = 1; State = Free; RowId = None } 
            let addSeat = stadiumBookingSystem.AddSeat rowId seat
            Expect.isOk addSeat "should be ok"
            let seatAdded = addSeat.OkValue |> snd |> List.head |> Option.get |> List.head
            let partition = seatAdded.Partition
            let position = seatAdded.Offset
            rowSubscriber.Assign(position, partition)
            let consumeResult = rowSubscriber.Consume()
            let message = consumeResult.Message.Value
            let brokerMessage = message |> serializer.Deserialize<BrokerAggregateMessage> |> Result.get
            let event = brokerMessage.Event |> serializer.Deserialize<RowAggregateEvent> |> Result.get
            let expected =
                RowAggregateEvent.SeatAdded
                    {
                        seat with RowId = Some rowId
                    }
            Expect.equal event expected "should be equal"

            let seatsKafkaViewer =
                    mkKafkaAggregateViewer<SeatsRow, RowAggregateEvent>
                        rowId
                        rowSubscriber
                        (CommandHandler.getAggregateStorageFreshStateViewer<SeatsRow, RowAggregateEvent> eventStore) (ApplicationInstance.ApplicationInstance.Instance.GetGuid())
             
            let (_, state, _, _) = seatsKafkaViewer.State() |> Result.get
            Expect.equal state.Seats.Length 1 "should be equal"
            
        testCase "Add many seats to a row and get the state using kafka viewer - Ok" <| fun _ ->
            let eventStore = pgStorage
            eventStore.Reset "_01" "_seatrow"
            eventStore.Reset "_01" "_stadium"
            eventStore.ResetAggregateStream "_01" "_seatrow"
            
            StateCache<Stadium>.Instance.Clear()
            AggregateCache<SeatsRow>.Instance.Clear()

            let rowSubscriber = KafkaSubscriber.Create("localhost:9092", "_01", "_seatrow", "sharpinoTestClient") |> Result.get

            let stadiumBookingSystem = StadiumBookingSystem(eventStore, localhostBroker)
            let rowId = Guid.NewGuid()

            let addRowReference = stadiumBookingSystem.AddRowReference rowId
            Expect.isOk addRowReference "should be ok"
           
            let seat = { Id = 1; State = Free; RowId = None } 
            let addSeat = stadiumBookingSystem.AddSeat rowId seat
            Expect.isOk addSeat "should be ok"
            let seatAdded = addSeat.OkValue |> snd |> List.head |> Option.get |> List.head
            let partition = seatAdded.Partition
            let position = seatAdded.Offset
            rowSubscriber.Assign(position, partition)
            let consumeResult = rowSubscriber.Consume()
            let message = consumeResult.Message.Value
            let brokerMessage = message |> serializer.Deserialize<BrokerAggregateMessage> |> Result.get
            let event = brokerMessage.Event |> serializer.Deserialize<RowAggregateEvent> |> Result.get
            let expected =
                RowAggregateEvent.SeatAdded
                    {
                        seat with RowId = Some rowId
                    }
            Expect.equal event expected "should be equal"

            let seatsKafkaViewer =
                    mkKafkaAggregateViewer<SeatsRow, RowAggregateEvent>
                        rowId
                        rowSubscriber
                        (CommandHandler.getAggregateStorageFreshStateViewer<SeatsRow, RowAggregateEvent> eventStore) (ApplicationInstance.ApplicationInstance.Instance.GetGuid())
             
            let (_, state, _, _) = seatsKafkaViewer.State() |> Result.get
            Expect.equal state.Seats.Length 1 "should be equal"
            
            let seat2 = { Id = 2; State = Free; RowId = None }
            let addSeat2 = stadiumBookingSystem.AddSeat rowId seat2
            Expect.isOk addSeat2 "should be ok"
            let seat3 = { Id = 3; State = Free; RowId = None }
            let addSeat3 = stadiumBookingSystem.AddSeat rowId seat3
            Expect.isOk addSeat3 "should be ok"
            
            seatsKafkaViewer.RefreshLoop() |> ignore
            
            let (_, state, _, _) = seatsKafkaViewer.State() |> Result.get
            Expect.equal state.Seats.Length 3 "should be equal"
            
        testCase "Add many rows and many seats using kafka viewer - Ok" <| fun _ ->
            let eventStore = pgStorage
            eventStore.Reset "_01" "_seatrow"
            eventStore.Reset "_01" "_stadium"
            eventStore.ResetAggregateStream "_01" "_seatrow"
            
            StateCache<Stadium>.Instance.Clear()
            AggregateCache<SeatsRow>.Instance.Clear()

            let rowSubscriber = KafkaSubscriber.Create("localhost:9092", "_01", "_seatrow", "sharpinoTestClient") |> Result.get

            let stadiumBookingSystem = StadiumBookingSystem(eventStore, localhostBroker)
            let rowId = Guid.NewGuid()

            let addRowReference = stadiumBookingSystem.AddRowReference rowId
            Expect.isOk addRowReference "should be ok"
            
            let rowId2 = Guid.NewGuid()
            let addRowReference2 = stadiumBookingSystem.AddRowReference rowId2
            Expect.isOk addRowReference2 "should be ok"
           
            let seat = { Id = 1; State = Free; RowId = None } 
            let addSeat = stadiumBookingSystem.AddSeat rowId seat
            Expect.isOk addSeat "should be ok"
            let seatAdded = addSeat.OkValue |> snd |> List.head |> Option.get |> List.head
            let partition = seatAdded.Partition
            let position = seatAdded.Offset
            rowSubscriber.Assign(position, partition)
            let consumeResult = rowSubscriber.Consume()
            let message = consumeResult.Message.Value
            let brokerMessage = message |> serializer.Deserialize<BrokerAggregateMessage> |> Result.get
            let event = brokerMessage.Event |> serializer.Deserialize<RowAggregateEvent> |> Result.get
            let expected =
                RowAggregateEvent.SeatAdded
                    {
                        seat with RowId = Some rowId
                    }
            Expect.equal event expected "should be equal"
            
            let seat12 = { Id = 11; State = Free; RowId = None }
            let addSeat2 = stadiumBookingSystem.AddSeat rowId2 seat12
            Expect.isOk addSeat "should be ok"
            
            let seatsKafkaViewer =
                    mkKafkaAggregateViewer<SeatsRow, RowAggregateEvent>
                        rowId
                        rowSubscriber
                        (CommandHandler.getAggregateStorageFreshStateViewer<SeatsRow, RowAggregateEvent> eventStore) (ApplicationInstance.ApplicationInstance.Instance.GetGuid())
             
            let seats2KafkaViewer =
                    mkKafkaAggregateViewer<SeatsRow, RowAggregateEvent>
                        rowId2
                        rowSubscriber
                        (CommandHandler.getAggregateStorageFreshStateViewer<SeatsRow, RowAggregateEvent> eventStore) (ApplicationInstance.ApplicationInstance.Instance.GetGuid()) 
            
            let (_, state, _, _) = seatsKafkaViewer.State() |> Result.get
            Expect.equal state.Seats.Length 1 "should be equal"
            
            let (_, stateRow2, _, _) = seats2KafkaViewer.State() |> Result.get
            Expect.equal stateRow2.Seats.Length 1 "should be equal"
            
            let seat2 = { Id = 2; State = Free; RowId = None }
            let addSeat2 = stadiumBookingSystem.AddSeat rowId seat2
            Expect.isOk addSeat2 "should be ok"
            let seat3 = { Id = 3; State = Free; RowId = None }
            let addSeat3 = stadiumBookingSystem.AddSeat rowId seat3
            Expect.isOk addSeat3 "should be ok"
            
            seatsKafkaViewer.RefreshLoop() |> ignore
            
            let (_, state, _, _) = seatsKafkaViewer.State() |> Result.get
            Expect.equal state.Seats.Length 3 "should be equal"
            
            let seat22 = { Id = 22; State = Free; RowId = None }
            let addSeat22 = stadiumBookingSystem.AddSeat rowId2 seat22
            Expect.isOk addSeat22 "should be ok"
            
            let seat23 = { Id = 23; State = Free; RowId = None }
            let addSeat23 = stadiumBookingSystem.AddSeat rowId2 seat23
            Expect.isOk addSeat23 "should be ok"
            
            seats2KafkaViewer.RefreshLoop() |> ignore
            let (_, stateRow2, _, _) = seats2KafkaViewer.State() |> Result.get
            Expect.equal stateRow2.Seats.Length 3 "should be equal"
            
            
            
    ]
    |> testSequenced