namespace SeatBookings.Tests
open Expecto
open Shared
open Server
open Npgsql.FSharp
open Npgsql
open System
open Sharpino
open Sharpino.CommandHandler
open Sharpino.Result
open Sharpino.Storage
open Sharpino.Cache
open Sharpino.KafkaBroker
open Sharpino.KafkaReceiver
open Sharpino.Utils
open Sharpino.TestUtils
open Sharpino.ApplicationInstance
open Tonyx.SeatsBooking
open Shared.Entities
open Tonyx.SeatsBooking.RowAggregateEvent
open Tonyx.SeatsBooking.Stadium
open Tonyx.SeatsBooking.SeatRow
open Tonyx.SeatsBooking.StadiumEvents
open Tonyx.SeatsBooking.StorageStadiumBookingSystem

module BookingTests =

    let getDeliveryResult (deliveryResult: Result<list<List<Definitions.EventId>> * list<option<List<Confluent.Kafka.DeliveryResult<'A,string>>>>,string>) =
        result {
            let! deliveryResult = deliveryResult
            let result =
                match deliveryResult with
                | (_, (Some (H::[])::_)) -> H |> Ok
                | _ -> Error "error getting delivery result"
            return! result
        }

    let eventBroker = getKafkaBroker ("localhost:9092",  eventStore)
    let stadiumSubscriber = KafkaSubscriber.Create("localhost:9092", "_01", "_stadium", "sharpinoClient") |> Result.get

    let kafkaStadiumViewer = mkKafkaViewer<Stadium, StadiumEvent> stadiumSubscriber storageStadiumViewer  (ApplicationInstance.Instance.GetGuid())
    let kafkaBasedStadiumState: StateViewer<Stadium> =
        printf "getting state\n"
        fun () ->
            kafkaStadiumViewer.RefreshLoop() |> ignore
            kafkaStadiumViewer.State()

    let seatBookings =
        let memoryStorage = MemoryStorage.MemoryStorage()
        let pgStorage = PgStorage.PgEventStore(connection)

        let doNothingBroker: IEventBroker =
            {
                notify = None
                notifyAggregate =  None
            }
        let connection =
            "Server=127.0.0.1;"+
            "Database=es_seat_booking;" +
            "User Id=safe;"+
            "Password=safe;"

        let retrieveLastAggregateVersionId version name =
            let streamName  = sprintf "aggregate_events%s%s" version name
            let query = sprintf "SELECT id, aggregate_id, aggregate_state_id FROM %s ORDER BY id DESC LIMIT 1" streamName
            connection
            |> Sql.connect
            |> Sql.query query
            |> Sql.execute (fun reader ->
                (
                    reader.int "id",
                    reader.uuid "aggregate_id",
                    reader.uuid "aggregate_state_id"
                )
            )
            |> Seq.tryHead

        let retrieveAggregateIdsAndAggregateStatesIds version name =
            let streamName  = sprintf "aggregate_events%s%s" version name
            let query = sprintf "SELECT id, aggregate_id, aggregate_state_id FROM %s" streamName
            connection
            |> Sql.connect
            |> Sql.query query
            |> Sql.execute (fun reader ->
                (
                    reader.int "id",
                    reader.uuid "aggregate_id",
                    reader.uuid "aggregate_state_id"
                )
            )
            |> Seq.toList

        let kafkaRowViewer2 =
            mkKafkaAggregateViewer2<SeatsRow, RowAggregateEvent>
                rowSubscriber' (getAggregateStorageFreshStateViewer<SeatsRow, RowAggregateEvent> eventStore) (ApplicationInstance.Instance.GetGuid())

        let rowStateViewer3: AggregateViewer<SeatsRow> =
            fun (rowId: Guid) ->
                kafkaRowViewer2.RefreshLoop() |> ignore
                kafkaRowViewer2.State rowId

        let stadiumSystem = StadiumBookingSystem(pgStorage, doNothingBroker)
        let memoryStadiumSystem = StadiumBookingSystem(memoryStorage, doNothingBroker)

        let rowSubscriber'' = KafkaAggregateSubscriber.Create2("localhost:9092", "_01", "_seatrow", "sharpinoRowClient") |> Result.get

        let kafkaRowViewer3 =
            mkKafkaAggregateViewer2<SeatsRow, RowAggregateEvent>
                rowSubscriber'' (getAggregateStorageFreshStateViewer<SeatsRow, RowAggregateEvent> eventStore) (ApplicationInstance.Instance.GetGuid())

        let rowStateViewer3: AggregateViewer<SeatsRow> =
            fun (rowId: Guid) ->
                kafkaRowViewer3.RefreshLoop() |> ignore
                kafkaRowViewer3.State rowId

        let mutable kafkaStadiumBookingSystemX = StadiumBookingSystem (eventStore, eventBroker, kafkaBasedStadiumState, rowStateViewer3)

        let setUp () =
            pgStorage.Reset "_01" "_seatrow"
            pgStorage.Reset "_01" "_stadium"
            pgStorage.ResetAggregateStream "_01" "_seatrow"
            AggregateCache<SeatsRow>.Instance.Clear()
            StateCache<Stadium>.Instance.Clear()
            ApplicationInstance.Instance.ResetGuid()

        let stadiumInstances =
            [
                stadiumSystem,0,0
                memoryStadiumSystem, 1, 1
            ]

        // everything is in progress here:
        ptestList "seat bookings" [
            multipleTestCase "initial state no seats - Ok" stadiumInstances <| fun (stadiumSystem, _, _) ->
                setUp ()

                // stadiumSubscriber.Reset()
                // when
                let rows = stadiumSystem.GetAllRowReferences ()
                // then
                Expect.isOk rows "should be ok"
                let result = rows |> Result.get
                Expect.equal result.Length 0 "should be 0"

            multipleTestCase "add a row reference to the stadium and retrieve it - Ok" stadiumInstances <| fun ( stadiumSystem,_ ,_ )  ->
                setUp()

                // given
                let unSetStrickLockVersionControl = stadiumSystem.UnSetAggregateStateControlInOptimisticLock "_01" "_seatrow"
                Expect.isOk unSetStrickLockVersionControl "should be ok"

                // when
                let rowId = Guid.NewGuid()
                let addRow = stadiumSystem.AddRowReference rowId

                // then
                Expect.isOk addRow "should be ok"

            multipleTestCase "retrieve an unexisting row - Error" stadiumInstances <| fun (stadiumSystem, _, _) ->
                setUp()

                // when
                let rowId = Guid.NewGuid()
                let row = stadiumSystem.GetRow rowId

                // then
                Expect.isError row "should be error"

            multipleTestCase "add a row reference and a seat to it. Retrieve the seat - Ok" stadiumInstances  <| fun (stadiumSystem,_,_ ) ->
                setUp()

                // when
                let rowId = Guid.NewGuid()
                let addRow = stadiumSystem.AddRowReference rowId
                Expect.isOk addRow "should be ok"
                let seat = { Id = 1; State = Free; RowId = None }
                let addSeat = stadiumSystem.AddSeat rowId seat
                Expect.isOk addSeat "should be ok"
                let deliveryResult = getDeliveryResult addSeat
                let _ =
                    match deliveryResult with
                    |  Ok deliveryResult -> kafkaRowViewer3.Assign (deliveryResult.Offset, deliveryResult.Partition)
                    | _ -> ()
                Expect.isOk addSeat "should be ok"

                // then

                let retrievedRow = stadiumSystem.GetRow rowId
                Expect.isOk retrievedRow "should be ok"
                let result = retrievedRow.OkValue
                Expect.equal result.Seats.Length 1 "should be 1"

            testCase "add a row reference and a seat to it. Retrieve the seat directly using subscriber. 2 - Ok"  <| fun _ ->
                setUp()

                let rowSubscriber' = KafkaAggregateSubscriber.Create2("localhost:9092", "_01", "_seatrow", "sharpinoRowClient") |> Result.get

                let kafkaRowViewer2 =
                    mkKafkaAggregateViewer2<SeatsRow, RowAggregateEvent>
                        rowSubscriber' (getAggregateStorageFreshStateViewer<SeatsRow, RowAggregateEvent> eventStore) (ApplicationInstance.Instance.GetGuid())

                let rowStateViewer2: AggregateViewer<SeatsRow> =
                    fun (rowId: Guid) ->
                        kafkaRowViewer2.RefreshLoop() |> ignore
                        kafkaRowViewer2.State rowId
                let kafkaStadiumBookingSystem = StadiumBookingSystem (eventStore, eventBroker, kafkaBasedStadiumState, rowStateViewer2)

                // when
                let rowId = Guid.NewGuid()
                let addRow = kafkaStadiumBookingSystem.AddRowReference rowId
                Expect.isOk addRow "should be ok"

                let seat = { Id = 1; State = Free; RowId = None }
                let addSeat = kafkaStadiumBookingSystem.AddSeat rowId seat
                Expect.isOk addSeat "should be ok"
                let deliveryResult = addSeat.OkValue |> snd |> List.head |> Option.get |> List.head
                rowSubscriber'.Assign (deliveryResult.Offset, deliveryResult.Partition)

                let deliveryResult = addSeat.OkValue |> snd |> List.head |> Option.get |> List.head
                kafkaRowViewer2.Assign (deliveryResult.Offset, deliveryResult.Partition)

                // let _ = kafkaRowViewer2.Refresh()

                // then

                let retrievedRow = kafkaStadiumBookingSystem.GetRow rowId
                Expect.isOk retrievedRow "should be ok"
                let result = retrievedRow.OkValue
                Expect.equal result.Seats.Length 1 "should be 1"


            pmultipleTestCase "add a row reference and five seats to it one by one. Retrieve the seat - Ok" stadiumInstances <| fun (stadiumSystem, _, _) ->
                setUp()

                // given

                // when
                let rowId = Guid.NewGuid()
                let addRow = stadiumSystem.AddRowReference rowId
                Expect.isOk addRow "should be ok"
                let seat = { Id = 1; State = Free; RowId = None }
                let seat2 = { Id = 2; State = Free; RowId = None }
                let seat3 = { Id = 3; State = Free; RowId = None }
                let seat4 = { Id = 4; State = Free; RowId = None }
                let seat5 = { Id = 5; State = Free; RowId = None }
                let seat6 = { Id = 6; State = Free; RowId = None }

                let addSeat = stadiumSystem.AddSeat rowId seat
                Expect.isOk addSeat "should be ok"
                let deliveryResult = getDeliveryResult addSeat
                let _ =
                    match deliveryResult with
                    |  Ok deliveryResult -> kafkaRowViewer3.Assign (deliveryResult.Offset, deliveryResult.Partition)
                    | _ -> ()

                let _ = stadiumSystem.AddSeat rowId seat2
                let _ = stadiumSystem.AddSeat rowId seat3
                let _ = stadiumSystem.AddSeat rowId seat4
                let _ = stadiumSystem.AddSeat rowId seat5
                let _ = stadiumSystem.AddSeat rowId seat6


                // then
                let retrievedRow = stadiumSystem.GetRow rowId
                Expect.isOk retrievedRow "should be ok"
                let result = retrievedRow.OkValue
                Expect.equal result.Seats.Length 6 "should be 6"

            pmultipleTestCase "add a row reference and then some seats to it. Retrieve the seats - OK" stadiumInstances <| fun (stadiumSystem, _, _)  ->
                setUp()
                // given

                // when
                let rowId = Guid.NewGuid()
                let addedRow = stadiumSystem.AddRowReference rowId
                Expect.isOk addedRow "should be ok"
                let seats =
                    [
                        { Id = 1; State = Free; RowId = None  }
                        { Id = 2; State = Free; RowId = None }
                        { Id = 3; State = Free; RowId = None }
                        { Id = 4; State = Free; RowId = None }
                        { Id = 5; State = Free; RowId = None }
                    ]
                let seatAdded = stadiumSystem.AddSeats rowId seats
                Expect.isOk seatAdded "should be ok"
                let deliveryResult = getDeliveryResult seatAdded

                let _ =
                    match deliveryResult with
                    |  Ok deliveryResult -> kafkaRowViewer3.Assign (deliveryResult.Offset, deliveryResult.Partition)
                    | _ -> ()

                let retrievedRow = stadiumSystem.GetRow rowId
                Expect.isOk retrievedRow "should be ok"

                let okRetrievedRow = retrievedRow.OkValue
                Expect.equal okRetrievedRow.Seats.Length 5 "should be 5"

            multipleTestCase "add two row references add a row reference and then some seats to it. Retrieve the seats then - Ok" stadiumInstances  <| fun (stadiumSystem, _, _)  ->
                setUp()

                let rowId = Guid.NewGuid()
                let addedRow = stadiumSystem.AddRowReference rowId
                Expect.isOk addedRow "should be ok"

                let rowId2 = Guid.NewGuid()
                let addedRow2 = stadiumSystem.AddRowReference rowId2

                Expect.isOk addedRow2 "should be ok"

                // when
                let seats = [
                    { Id = 1; State = Free; RowId = None }
                    { Id = 2; State = Free; RowId = None }
                    { Id = 3; State = Free; RowId = None }
                    { Id = 4; State = Free; RowId = None }
                    { Id = 5; State = Free; RowId = None }
                ]
                let seatAdded = stadiumSystem.AddSeats rowId seats
                let deliveryResult = getDeliveryResult seatAdded

                let _ =
                    match deliveryResult with
                    |  Ok deliveryResult -> kafkaRowViewer3.Assign (deliveryResult.Offset, deliveryResult.Partition)
                    | _ -> ()

                Expect.isOk seatAdded "should be ok"
                let seats2 = [
                            { Id = 6; State = Free; RowId = None }
                            { Id = 7; State = Free; RowId = None }
                            { Id = 8; State = Free; RowId = None }
                            { Id = 9; State = Free; RowId = None }
                            { Id = 10; State = Free; RowId = None }
                            ]
                let seatsAdded2 = stadiumSystem.AddSeats rowId2 seats2
                Expect.isOk seatsAdded2 "should be ok"

                // then
                let retrievedRow = stadiumSystem.GetRow rowId
                Expect.isOk retrievedRow "should be ok"
                let okRetrievedRow = retrievedRow.OkValue
                Expect.equal 5 okRetrievedRow.Seats.Length "should be 1"

                let retrievedRow2 = stadiumSystem.GetRow rowId2
                Expect.isOk retrievedRow2 "should be ok"
                let okRetrievedRow2 = retrievedRow2.OkValue
                Expect.equal 5 okRetrievedRow2.Seats.Length "should be 1"

            multipleTestCase "can't add a seat with the same id of another seat in the same row - Ok" stadiumInstances <| fun (stadiumSystem, _, _)  ->
                setUp()
                // given

                let rowId = Guid.NewGuid()
                let addedRow = stadiumSystem.AddRowReference rowId
                Expect.isOk addedRow "should be ok"
                // when
                let seat =  { Id = 1; State = Free; RowId = None }
                let seatAdded = stadiumSystem.AddSeat rowId seat
                Expect.isOk seatAdded "should be ok"
                let seat2 = { Id = 1; State = Free; RowId = None }
                let seatAdded2 = stadiumSystem.AddSeat rowId seat2
                Expect.isError seatAdded2 "should be error"

            multipleTestCase "add a booking on an unexisting row - Error" stadiumInstances  <| fun (stadiumSystem, _, _) ->
                setUp()
                // given

                let booking = { Id = 1; SeatIds = [1]}
                let rowId = Guid.NewGuid()
                let tryBooking = stadiumSystem.BookSeats rowId booking
                Expect.isError tryBooking "should be error"
                let (Error e ) = tryBooking
                Expect.equal e (sprintf "There is no aggregate of version \"_01\", name \"_seatrow\" with id %A" rowId) "should be equal"

            multipleTestCase "add a booking on an existing row and unexisting seat - Error" stadiumInstances <| fun (stadiumSystem, _, _) ->
                setUp()
                // given
                let rowId = Guid.NewGuid()

                // when
                let addedRow = stadiumSystem.AddRowReference rowId
                Expect.isOk addedRow "should be ok"
                let booking = { Id = 1; SeatIds = [1]}

                // then
                let tryBooking = stadiumSystem.BookSeats rowId booking
                Expect.isError tryBooking "should be error"
                let (Error e ) = tryBooking
                Expect.equal e "Seat not found" "should be equal"

            multipleTestCase "add a booking on a valid row and valid seat - Ok" stadiumInstances <| fun (stadiumSystem, _, _) ->
                setUp()

                // given

                let rowId = Guid.NewGuid()
                let addedRow = stadiumSystem.AddRowReference rowId
                Expect.isOk addedRow "should be ok"

                // when
                let seat = { Id = 1; State = Free; RowId = None }
                let seatAdded = stadiumSystem.AddSeat rowId seat
                Expect.isOk seatAdded "should be ok"
                let booking = { Id = 1; SeatIds = [1]}

                // then
                let tryBooking = stadiumSystem.BookSeats rowId booking
                Expect.isOk tryBooking "should be ok"

            multipleTestCase "can't book an already booked seat - Error" stadiumInstances <| fun (stadiumSystem, _, _) ->
                setUp()

                // given
                let stadiumSystem = StadiumBookingSystem(pgStorage, doNothingBroker)

                // when
                let rowId = Guid.NewGuid()
                let addedRow = stadiumSystem.AddRowReference rowId
                Expect.isOk addedRow "should be ok"
                let seat = { Id = 1; State = Free; RowId = None }
                let seatAdded = stadiumSystem.AddSeat rowId seat
                Expect.isOk seatAdded "should be ok"
                let booking = { Id = 1; SeatIds = [1]}
                let tryBooking = stadiumSystem.BookSeats rowId booking
                Expect.isOk tryBooking "should be ok"

                // then
                let tryBookingAgain = stadiumSystem.BookSeats rowId booking
                Expect.isError tryBookingAgain "should be error"
                let (Error e) = tryBookingAgain
                Expect.equal e "Seat already booked" "should be equal"

            multipleTestCase "add many seats and book one of them - Ok" stadiumInstances  <| fun (stadiumSystem, _, _) ->
                setUp()

                // given

                // when
                let rowId = Guid.NewGuid()
                let addedRow = stadiumSystem.AddRowReference rowId
                Expect.isOk addedRow "should be ok"
                let seats = [
                    { Id = 1; State = Free; RowId = None }
                    { Id = 2; State = Free; RowId = None }
                ]
                let seatsAdded = stadiumSystem.AddSeats rowId seats
                Expect.isOk seatsAdded "should be ok"

                // then
                let booking = { Id = 1; SeatIds = [1]}
                let tryBooking = stadiumSystem.BookSeats rowId booking
                Expect.isOk tryBooking "should be ok"

            pmultipleTestCase "violate the middle seat non empty constraint in one single booking - Ok" stadiumInstances <| fun (stadiumSystem, _, _) ->
                setUp()

                // given
                let rowId = Guid.NewGuid()
                let middleSeatInvariant: Invariant<SeatsRow>  =
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
                let middleSeatInvariantContainer = InvariantContainer.Build middleSeatInvariant
                let addedRow = stadiumSystem.AddRowReference rowId
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
                let booking = { Id = 1; SeatIds = [1;2;4;5]}

                // when
                let booking = { Id = 1; SeatIds = [1;2;4;5]}
                let tryBooking = stadiumSystem.BookSeats rowId booking

                // then
                Expect.isError tryBooking "should be error"
                let (Error e) = tryBooking
                Expect.equal e "error: can't leave a single seat free in the middle" "should be equal"

            multipleTestCase "if there is no invariant/contraint then can book seats leaving the only middle seat unbooked - Ok" stadiumInstances  <| fun (stadiumSystem, _, _) ->
                setUp()


                let rowId = Guid.NewGuid()
                let addedRow = stadiumSystem.AddRowReference rowId
                Expect.isOk addedRow "should be ok"
                let seats = [
                    { Id = 1; State = Free; RowId = None }
                    { Id = 2; State = Free; RowId = None }
                    { Id = 3; State = Free; RowId = None }
                    { Id = 4; State = Free; RowId = None }
                    { Id = 5; State = Free; RowId = None }
                ]
                let seatsAdded = stadiumSystem.AddSeats rowId seats
                Expect.isOk seatsAdded "should be ok"
                let booking = { Id = 1; SeatIds = [1;2;4;5]}
                // when
                let booking = { Id = 1; SeatIds = [1;2;4;5]}
                let tryBooking = stadiumSystem.BookSeats rowId booking

                // then
                Expect.isOk tryBooking "should be ok"

            multipleTestCase "book free seats among two rows, one fails, so it makes fail them all - Error" stadiumInstances <| fun (stadiumSystem, _, _) ->
                setUp()
                // given

                let rowId1 = Guid.NewGuid()
                let rowId2 = Guid.NewGuid()
                let middleSeatNotFreeRule: Invariant<SeatsRow> =
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
                let invariantContainer = InvariantContainer.Build middleSeatNotFreeRule

                let addedRow1 = stadiumSystem.AddRowReference rowId1
                Expect.isOk addedRow1 "should be ok"

                let addInvariantToRow1 = stadiumSystem.AddInvariant rowId1 invariantContainer
                Expect.isOk addInvariantToRow1 "should be ok"

                let addedRow2 = stadiumSystem.AddRowReference rowId2
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

                let booking1 = { Id = 1; SeatIds = [1;2;4;5]} // invariant violated
                let booking2 = { Id = 2; SeatIds = [6;7;8;9;10]}
                let tryMultiBooking = stadiumSystem.BookSeatsNRows [(rowId1, booking1); (rowId2, booking2)]
                Expect.isError tryMultiBooking "should be error"
                let (Error e) = tryMultiBooking
                Expect.equal e "error: can't leave a single seat free in the middle" "should be equal"

                // now make a valid booking on both
                let newBooking1 = {Id = 1; SeatIds = [1; 4; 5]}
                let newBooking2 = {Id = 2; SeatIds = [6; 7; 8; 9; 10]}
                let tryMultiBookingAgain = stadiumSystem.BookSeatsNRows [(rowId1, newBooking1); (rowId2, newBooking2)]
                Expect.isOk tryMultiBookingAgain "should be ok"

            multipleTestCase "add a seats in one row and two seat in another row - Ok" stadiumInstances <| fun (stadiumSystem, _, _) ->
                // given
                setUp()

                let rowId1 = Guid.NewGuid()
                let rowId2 = Guid.NewGuid()

                let addedRow1 = stadiumSystem.AddRowReference rowId1
                Expect.isOk addedRow1 "should be ok"

                let addedRow2 = stadiumSystem.AddRowReference rowId2
                Expect.isOk addedRow2 "should be ok"

                let seat1 = { Id = 1; State = Free; RowId = None }
                let seat2 = { Id = 6; State = Free; RowId = None }
                let seat3 = { Id = 7; State = Free; RowId = None }

                // when
                let addAllSeats = stadiumSystem.AddSeatsToRows [(rowId1, [seat1]); (rowId2, [seat2; seat3])]

                let deliveryResult = getDeliveryResult addAllSeats

                // let _ =
                //     match deliveryResult with
                //     |  Ok deliveryResult -> kafkaRowViewer3.Assign (deliveryResult.Offset, deliveryResult.Partition)
                //     | _ -> ()
                // Expect.isOk addAllSeats "should be ok"

                // then
                let retrievedRow1 = stadiumSystem.GetRow rowId1
                let retrievedRow2 = stadiumSystem.GetRow rowId2

                Expect.equal retrievedRow1.OkValue.Seats.Length 1 "should be equal"
                Expect.equal retrievedRow2.OkValue.Seats.Length 2 "should be equal"

            multipleTestCase "add a seats in one row and two seat in another row, then a seat again in first row - Ok" stadiumInstances <| fun (stadiumSystem, _, _) ->


                setUp()

                let rowId1 = Guid.NewGuid()
                let rowId2 = Guid.NewGuid()

                let addedRow1 = stadiumSystem.AddRowReference rowId1
                Expect.isOk addedRow1 "should be ok"

                let addedRow2 = stadiumSystem.AddRowReference rowId2
                Expect.isOk addedRow2 "should be ok"

                let seat1 = { Id = 1; State = Free; RowId = None }
                let seat2 = { Id = 6; State = Free; RowId = None }
                let seat3 = { Id = 7; State = Free; RowId = None }

                let seat4 = { Id = 2; State = Free; RowId = None }

                let addAllSeats = stadiumSystem.AddSeatsToRows [(rowId1, [seat1]); (rowId2, [seat2; seat3])]
                Expect.isOk addAllSeats "should be Ok"

                // when
                let addSingleSeatAgain = stadiumSystem.AddSeat rowId1 seat4
                Expect.isOk addSingleSeatAgain "should be Ok"

                // then
                let retrievedRow1 = stadiumSystem.GetRow rowId1
                let retrievedRow2 = stadiumSystem.GetRow rowId2

                Expect.equal retrievedRow1.OkValue.Seats.Length 2 "should be equal"
                Expect.equal retrievedRow2.OkValue.Seats.Length 2 "should be equal"

            multipleTestCase "A single booking cannot book all seats involving three rows or more - Error" stadiumInstances <| fun (stadiumSystem, _, _) ->
                setUp()
                // given

                let rowId1 = Guid.NewGuid()
                let rowId2 = Guid.NewGuid()
                let rowId3 = Guid.NewGuid()

                let addedRow1 = stadiumSystem.AddRowReference rowId1
                Expect.isOk addedRow1 "should be ok"
                let addedRow2 = stadiumSystem.AddRowReference rowId2
                Expect.isOk addedRow2 "should be ok"
                let addRow3 = stadiumSystem.AddRowReference rowId3
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
                let seatsAdded3 = stadiumSystem.AddSeats rowId3 seats3
                Expect.isOk seatsAdded3 "should be ok"
                let booking1 = { Id = 1; SeatIds = [1;2;3;4;5]}
                let booking2 = { Id = 2; SeatIds = [6;7;8;9;10]}
                let booking3 = { Id = 3; SeatIds = [11;12;13;14;15]}

                let tryMultiBooking = stadiumSystem.BookSeatsNRows [(rowId1, booking1); (rowId2, booking2); (rowId3, booking3)]
                // now make a valid booking on both
                Expect.isError tryMultiBooking "should be error"

            testCase "the classic optimistic lock is unset, so I can store events with the same version Id in the aggregate events table, and events will be processed - OK"  <| fun _ ->
                setUp()
                // given
                let stadiumSystem = StadiumBookingSystem(pgStorage, doNothingBroker)
                let rowId = Guid.NewGuid()
                let addRow = stadiumSystem.AddRowReference rowId
                Expect.isOk addRow "should be ok"
                let addSeat = stadiumSystem.AddSeat rowId { Id = 1; State = Free; RowId = None }
                Expect.isOk addSeat "should be ok"
                let unsetLockConstraint = stadiumSystem.UnSetAggregateStateControlInOptimisticLock "_01" "_seatrow"
                Expect.isOk unsetLockConstraint "should be ok"

                let retrieved = retrieveLastAggregateVersionId "_01" "_seatrow"
                let (_, aggregateId, aggregateVersionId) = retrieved |> Option.get

                // when
                let addAnotherSeatEvent = RowAggregateEvent.SeatAdded { Id = 2; State = Free; RowId = None } |> serializer.Serialize
                let stored =
                    (pgStorage :> IEventStore).AddAggregateEvents "_01" "_seatrow" aggregateId aggregateVersionId [addAnotherSeatEvent]

                Expect.isOk stored "should be ok"

                // then
                let row = stadiumSystem.GetRow rowId  |> Result.get
                let seats = row.Seats

                Expect.equal seats.Length 2 "should be equal"

            testCase "the classic optimistic lock is set, so I can not store events with the same version Id in the aggregate events table, and so conflicting event can't be processed - OK"  <| fun _ ->
                setUp()

                // given
                let stadiumSystem = StadiumBookingSystem(pgStorage, doNothingBroker)
                let rowId = Guid.NewGuid()
                let addRow = stadiumSystem.AddRowReference rowId
                Expect.isOk addRow "should be ok"
                let addSeat = stadiumSystem.AddSeat rowId { Id = 1; State = Free; RowId = None }
                Expect.isOk addSeat "should be ok"
                let unsetLockConstraint = stadiumSystem.SetAggregateStateControlInOptimisticLock "_01" "_seatrow"
                Expect.isOk unsetLockConstraint "should be ok"

                let retrieved = retrieveLastAggregateVersionId "_01" "_seatrow"
                let (_, aggregateId, aggregateVersionId) = retrieved |> Option.get

                // when
                let addAnotherSeatEvent = RowAggregateEvent.SeatAdded { Id = 2; State = Free; RowId = None } |> serializer.Serialize
                let stored =
                    (pgStorage :> IEventStore).AddAggregateEvents "_01" "_seatrow" aggregateId aggregateVersionId [addAnotherSeatEvent]
                Expect.isError stored "should be error"

                // then
                let row = stadiumSystem.GetRow rowId  |> Result.get
                let seats = row.Seats
                Expect.equal seats.Length 1 "should be equal"

            // todo: cases that show that the classic optimistic lock ensures that multiaggregate events are preserved and that in the enhanced optimistic lock it is possible that one of tham fails and the other doesn't

        ]
        |> testSequenced