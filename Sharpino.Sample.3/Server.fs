namespace Tonyx.SeatsBooking
open Sharpino.PgStorage
open Tonyx.SeatsBooking.IStadiumBookingSystem
open Tonyx.SeatsBooking.Shared.Services
open Tonyx.SeatsBooking.Seats
open Tonyx.SeatsBooking.SeatRow
open Tonyx.SeatsBooking.Stadium
open Tonyx.SeatsBooking.StadiumEvents
open Tonyx.SeatsBooking.StadiumCommands
open Tonyx.SeatsBooking.RowAggregateEvent
open Tonyx.SeatsBooking.RowAggregateCommand
open Tonyx.SeatsBooking
open Sharpino.CommandHandler
open Sharpino.Definitions
open System
open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Sharpino.Storage
open Sharpino.Core
open Sharpino.Utils
open Fable.Remoting.Server
open Fable.Remoting.Giraffe
open Saturn
open Fable.Remoting.Server
open Fable.Remoting.Giraffe
open Saturn
open Tonyx.SeatsBooking.Shared
open Tonyx.SeatsBooking.StorageStadiumBookingSystem
open log4net

module Server =
    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    // log4net.Config.BasicConfigurator.Configure() |> ignore
    log.Debug "starting server"
    
    let connection =
        "Server=127.0.0.1;"+
        "Database=es_seat_booking;" +
        "User Id=safe;"+
        "Password=safe;"
        
    let eventStore = (PgEventStore connection) :> IEventStore
    let doNothingBroker: IEventBroker =
        {
            notify = None
            notifyAggregate = None
        }
    let stadiumBookingSystem = StadiumBookingSystem(eventStore, doNothingBroker) :> IStadiumBookingSystem
    
    let restStadiumBookingSystem: IRestStadiumBookingSystem =
        {
            AddRowReference =
                fun rowReference ->
                    async {
                        return stadiumBookingSystem.AddRowReference(rowReference) |> Result.map (fun _ -> ())
                    }
            BookSeats =
                fun (rowReference, seats) ->
                    async {
                        return stadiumBookingSystem.BookSeats rowReference seats |> Result.map (fun _ -> ())
                    }
            BookSeatsNRows =
                fun rowReferenceAndSeats ->
                    async {
                        return stadiumBookingSystem.BookSeatsNRows rowReferenceAndSeats |> Result.map (fun _ -> ())
                    }    
            GetRow =
                fun rowReference ->
                    async {
                        return stadiumBookingSystem.GetRow rowReference 
                    }
            AddSeat =
                fun (rowId, seat) ->
                    async {
                        return stadiumBookingSystem.AddSeat rowId seat |> Result.map (fun _ -> ())
                    }
            AddSeats =
                fun (guid, seats) ->
                    async {
                        return stadiumBookingSystem.AddSeats guid seats |> Result.map (fun _ -> ())
                    }
            GetAllRowReferences =
                fun () ->
                    async {
                        return stadiumBookingSystem.GetAllRowReferences () 
                    }
            AddInvariant =
                fun (rowId, invariant) ->
                    async {
                        return stadiumBookingSystem.AddInvariant rowId invariant |> Result.map (fun _ -> ())
                    }
        }
    let webApp =
        Remoting.createApi ()
        |> Remoting.withErrorHandler (fun ex routeInfo -> Propagate ex.Message)
        |> Remoting.withRouteBuilder Route.builder
        |> Remoting.fromValue restStadiumBookingSystem
        |> Remoting.buildHttpHandler
    
    let appl =
        application {
            use_router webApp
            memory_cache
            use_static "public"
            use_gzip
        }
    
    [<EntryPoint>]
    let main _ =
        run appl
        0
