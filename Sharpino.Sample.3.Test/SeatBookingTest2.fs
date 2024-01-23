
module SeatBookingTests2

open seatsLockWithSharpino.Row1Context
open seatsLockWithSharpino.RefactoredRow
open seatsLockWithSharpino.Seats
open seatsLockWithSharpino.RefactoredApp
open seatsLockWithSharpino
open seatsLockWithSharpino.StadiumContext
open seatsLockWithSharpino.Row
open seatsLockWithSharpino.Row1
open seatsLockWithSharpino.Row2
open FsToolkit.ErrorHandling
open Expecto
open Sharpino
open Sharpino.PgStorage
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
open Sharpino.CommandHandler

[<Tests>]
let aggregateRowRefactoredTests =
    let serializer = new Utils.JsonSerializer(Utils.serSettings) :> Utils.ISerializer
    let setUp () =
        AggregateCache<SeatsRow>.Instance.Clear()
        StateCache<StadiumContext>.Instance.Clear()
    let connection = 
        "Server=127.0.0.1;"+
        "Database=es_seat_booking;" +
        "User Id=safe;"+
        "Password=safe;"
    ftestList "seatBookingTests" [
        testCase "initially the stadium has no row references - Ok" <| fun _ ->
            setUp()
            let storage = PgEventStore connection
            (storage :> IEventStore).Reset "_01" "_seatrow"
            (storage :> IEventStore).Reset StadiumContext.Version StadiumContext.StorageName
            let stadiumBookingSystem = StadiumBookingSystem  storage
            let retrievedRows = stadiumBookingSystem.GetAllRowReferences()
            Expect.isOk retrievedRows "should be ok"
            let result = retrievedRows.OkValue
            Expect.equal 0 result.Length "should be 0"
    ]
        