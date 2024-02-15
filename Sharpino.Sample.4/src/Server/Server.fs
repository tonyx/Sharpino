module Server
open Fable.Remoting.Server
open Fable.Remoting.Giraffe
open Saturn
open Shared
open FSharpPlus
open Farmer
open Shared.Services
open Sharpino.PgStorage
open Sharpino.Storage
open Tonyx.SeatsBooking.StorageStadiumBookingSystem

let connection =
    "Server=127.0.0.1;"+
    "Database=es_seat_booking;" +
    "User Id=safe;"+
    "Password=safe;"
let eventStore = PgEventStore connection
let doNothingBroker: IEventBroker =
    {
        notify = None
        notifyAggregate = None
    }

let stadiumBookingSystem = StadiumBookingSystem (eventStore, doNothingBroker)

let seatBookingSystemApi: IRestStadiumBookingSystem = {
    AddRowReference = fun () -> async {
        let id = System.Guid.NewGuid()
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