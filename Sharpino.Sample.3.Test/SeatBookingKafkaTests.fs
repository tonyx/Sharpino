
module SeatBookingKafkaTests

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
            let row = SeatsRow (rowId, localhostBroker)
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

            // let seatSubscriber = KafkaSubscriber.Create("localhost:9092", "_01", "_seatrow", "sharpinoTestClient") |> Result.get
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
            let brokerMessage = message |> serializer.Deserialize<BrokerMessage> |> Result.get
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
            StateCache<Stadium>.Instance.Clear()
            AggregateCache<SeatsRow>.Instance.Clear()

            let rowSubscriber = KafkaSubscriber.Create("localhost:9092", "_01", "_seatrow", "sharpinoTestClient") |> Result.get
            // let stadiumSubscriber = KafkaSubscriber.Create("localhost:9092", "_01", "_stadium", "sharpinoTestClient") |> Result.get

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
            let brokerMessage = message |> serializer.Deserialize<BrokerMessage> |> Result.get
            let event = brokerMessage.Event |> serializer.Deserialize<RowAggregateEvent> |> Result.get
            let expected =
                RowAggregateEvent.SeatAdded
                    {
                        seat with RowId = Some rowId
                    }
            Expect.equal event expected "should be equal"

            let seatsKafkaVierwer =
                    mkKafkaAggregateViewer<SeatsRow, RowAggregateEvent>
                        rowId
                        rowSubscriber
                        (CommandHandler.getAggregateStorageFreshStateViewer<SeatsRow, RowAggregateEvent> eventStore) (ApplicationInstance.ApplicationInstance.Instance.GetGuid())
             
            Expect.isTrue true "true"

    
            
            
            // let seat = { Id = 1; State = Free; RowId = None }
            // let seat
            // let rowId = Guid.NewGuid()
            // let row = SeatsRow (rowId, localhostBroker)
            // let rowStorageCreation = row.Serialize serializer
            // let stored = (eventStore :> IEventStore).SetInitialAggregateState rowId "_01" "_seatrow" rowStorageCreation
            // let seat = { Id = 1; State = Free; RowId = None } 

            // Expect.isOk stored "should be ok"


            
            // let rowStateViewer = getAggregateStorageFreshStateViewer<SeatsRow, RowAggregateEvent> eventStore
            // let gotState = rowStateViewer rowId
            // Expect.isOk gotState "should be ok"
            // let (_, state, _, _) = gotState |> Result.get
            // Expect.equal (state.Seats.Length) 0 "should be 0"
    ]
    |> testSequenced