
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
          seats: List<Id>
        }
    let toRow1 (booking: Booking) =
        {
            booking with
                seats = booking.seats |> List.filter (fun seatId -> seatId >= 1 && seatId <= 5)
        }

    let toRow2 (booking: Booking) =
        {
            booking with
                seats = booking.seats |> List.filter (fun seatId -> seatId >= 6 && seatId <= 10)
        }




