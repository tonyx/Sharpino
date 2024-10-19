namespace Sharpino.Sample.Saga.Api
// module Sharpino.Sample.Saga.Api.SeatBooking
open Sharpino.Storage
module SeatBooking =

    // will implement stuff like: add many reservations at once
    let doNothingBroker: IEventBroker<_> =
        {
            notify = None
            notifyAggregate = None
        }
        
    type SeatBookingService(eventStore: IEventStore<string>) =
        member this.Foo() = "bar"
            