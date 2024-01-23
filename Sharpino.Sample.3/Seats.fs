
namespace seatsLockWithSharpino

module Seats =
    type Id = int
    type SeatState =
        | Booked
        | Free
    type Seat =
        { id: Id
          State: SeatState 
        } 
    type Booking =
        { id: Id
          seatIds: List<Id>
        }
        with member 
                this.isEmpty() = 
                    this.seatIds |> List.isEmpty
    let toRow1 (booking: Booking) =
        {
            booking with
                seatIds = booking.seatIds |> List.filter (fun seatId -> seatId >= 1 && seatId <= 5)
        }

    let toRow2 (booking: Booking) =
        {
            booking with
                seatIds = booking.seatIds |> List.filter (fun seatId -> seatId >= 6 && seatId <= 10)
        }




