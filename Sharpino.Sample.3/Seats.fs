
namespace Tonyx.SeatsBooking

module Seats =
    type Id = int
    type SeatState =
        | Booked
        | Free
    type Seat =
        { Id: Id
          State: SeatState
          RowId: Option<System.Guid>
        } 
    type Booking =
        { Id: Id
          SeatIds: List<Id>
        }
        with member 
                this.isEmpty() = 
                    this.SeatIds |> List.isEmpty
    let toRow1 (booking: Booking) =
        {
            booking with
                SeatIds = booking.SeatIds |> List.filter (fun seatId -> seatId >= 1 && seatId <= 5)
        }

    let toRow2 (booking: Booking) =
        {
            booking with
                SeatIds = booking.SeatIds |> List.filter (fun seatId -> seatId >= 6 && seatId <= 10)
        }




