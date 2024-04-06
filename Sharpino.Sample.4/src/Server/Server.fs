module Server
open Fable.Remoting.Server
open Fable.Remoting.Giraffe
open Farmer.Builders
open Saturn
open System
open Shared
open FSharpPlus
open Farmer
open System.Collections
open Shared.Services
open Sharpino
open Sharpino.MemoryStorage
open Sharpino.PgStorage
open Sharpino.CommandHandler
open Sharpino.Storage
open Sharpino.KafkaBroker
open Sharpino.KafkaReceiver
open Sharpino.ApplicationInstance
open Tonyx.SeatsBooking
open Tonyx.SeatsBooking.RowAggregateEvent
open Tonyx.SeatsBooking.SeatRow
open Tonyx.SeatsBooking.StorageStadiumBookingSystem
open Tonyx.SeatsBooking.Stadium
open Tonyx.SeatsBooking.StadiumEvents

let connection =
    "Server=127.0.0.1;"+
    "Database=es_seat_booking;" +
    "User Id=safe;"+
    "Password=safe;"
let eventStore = PgEventStore connection
let memoryStore = MemoryStorage()
let doNothingBroker: IEventBroker =
    {
        notify = None
        notifyAggregate = None
    }

let stadiumSubscriber = KafkaSubscriber.Create("localhost:9092", "_01", "_stadium", "sharpinoClient") |> Result.get
let rowSubscriber = KafkaSubscriber.Create("localhost:9092", "_01", "_seatrow", "sharpinoRowClient") |> Result.get
let storageStadiumViewer = getStorageFreshStateViewer<Stadium, StadiumEvent > eventStore
let kafkaStadiumViewer = mkKafkaViewer<Stadium, StadiumEvent> stadiumSubscriber storageStadiumViewer  (ApplicationInstance.Instance.GetGuid())

let stadiumBookingSystem = StadiumBookingSystem (memoryStore, doNothingBroker)

let seatBookingSystemApi: IRestStadiumBookingSystem = {
    AddRowReference = fun () -> async {
        let added = stadiumBookingSystem.AddRowReference ()
        match added with
        | Ok _ -> return Ok ()
        | Error e -> return Error e
    }
    BookSeats = fun (rowId, booking) -> async {
        let booked = stadiumBookingSystem.BookSeats rowId booking
        match booked with
        | Ok _ -> return Ok ()
        | Error e -> return Error e
    }
    BookSeatsNRows = fun xs -> async {
        let booked = stadiumBookingSystem.BookSeatsNRows xs
        match booked with
        | Ok _ -> return Ok ()
        | Error e -> return Error e
    }
    AddSeat = fun (rowId, seat) -> async {
        let added = stadiumBookingSystem.AddSeat rowId seat
        match added with
        | Ok _ -> return Ok ()
        | Error e -> return Error e
    }
    RemoveSeat = fun seat -> async {
        let removed = stadiumBookingSystem.RemoveSeat seat
        match removed with
        | Ok _ -> return Ok ()
        | Error e -> return Error e
    }
    AddSeats = fun (rowId, seats) -> async {
        let added = stadiumBookingSystem.AddSeats rowId seats
        match added with
        | Ok _ -> return Ok ()
        | Error e -> return Error e
    }
    GetAllRowReferences =
        fun () -> async {
            let rowReferences = stadiumBookingSystem.GetAllRowReferences()
            return rowReferences
        }
    GetAllRowTOs =
        fun () -> async {
            let rowTOs = stadiumBookingSystem.GetAllRowsSeatsTo()
            return rowTOs
        }
    Authenticate =
        fun token  -> async {
            printf "Authenticated\n"
            printf "token:\n"
            printf "%s\n" token
            return Ok ()
        }
}

let webApp =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.fromValue seatBookingSystemApi
    |> Remoting.buildHttpHandler

let app = application {
    use_router webApp
    memory_cache
    use_static "public"
    use_gzip
}

[<EntryPoint>]
let main _ =
    run app
    0