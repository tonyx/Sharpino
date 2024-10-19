module Tests
open Sharpino.Sample.Saga.Domain.Seat.Row
open Sharpino.Sample.Saga.Domain.Seat.Events
open Sharpino.Sample.Saga.Domain.Seat.Commands
open Expecto

[<Tests>]
let tests =
    testList "samples" [
        testCase "universe exists (╭ರᴥ•́)" <| fun _ ->
            let subject = true
            Expect.isTrue subject "I compute, therefore I am."
  ]
