module Server.Tests

open Expecto
open Tonyx.SeatsBooking.Stadium
open Tonyx.SeatsBooking.SeatRow
open Tonyx.SeatsBooking.StorageStadiumBookingSystem
open SeatBookings.Tests.BookingTests

open Shared
open Server

let server =
    testList "Server" [
        testCase "Adding valid Todo"
        <| fun _ ->
            Expect.isTrue true "true"
    ]


let all = testList "All"
              [ Shared.Tests.shared
                server
                seatBookings
                ]
[<EntryPoint>]
let main _ = runTestsWithCLIArgs [] [||] all