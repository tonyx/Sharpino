
namespace seatsLockWithSharpino
open seatsLockWithSharpino.Seats
open Sharpino.Utils
open Sharpino
open FsToolkit.ErrorHandling

module Row =
    let serializer = new Utils.JsonSerializer(Utils.serSettings) :> Utils.ISerializer

    type RowContext(RowSeats: Seats.Seat list) = 
        member this.RowSeats with get() = RowSeats
        member this.IsAvailable (seatId: Seats.Id) =
            this.RowSeats
            |> List.filter (fun seat -> seat.id = seatId)
            |> List.exists (fun seat -> seat.State = Seats.SeatState.Free) 

        member this.BookSeats (booking: Seats.Booking) =
            result {
                let seats = this.RowSeats
                let! check = 
                    let seatsInvolved =
                        seats
                        |> List.filter (fun seat -> booking.seats |> List.contains seat.id)
                    seatsInvolved
                        |> List.forall (fun seat -> seat.State = Seats.SeatState.Free)
                        |> boolToResult "Seat already booked"
                
                let claimedSeats = 
                    seats
                    |> List.filter (fun seat -> booking.seats |> List.contains seat.id)
                    |> List.map (fun seat -> { seat with State = Seats.SeatState.Booked })

                let unclaimedSeats = 
                    seats
                    |> List.filter (fun seat -> not (booking.seats |> List.contains seat.id))

                let potentialNewRowState = 
                    claimedSeats @ unclaimedSeats
                    |> List.sortBy (fun seat -> seat.id)
                
                // it just checks that the middle one can't be free, but
                // actually it was supposed to be "no single seat left" anywhere A.F.A.I.K.
                let theSeatInTheMiddleCantRemainFreeIfAllTheOtherAreClaimed =
                    (potentialNewRowState.[0].State = Seats.SeatState.Booked &&
                    potentialNewRowState.[1].State = Seats.SeatState.Booked &&
                    potentialNewRowState.[2].State = Seats.SeatState.Free &&
                    potentialNewRowState.[3].State = Seats.SeatState.Booked &&
                    potentialNewRowState.[4].State = Seats.SeatState.Booked)
                    |> not
                    |> boolToResult "error: can't leave a single seat free"
                let! checkInvariant = theSeatInTheMiddleCantRemainFreeIfAllTheOtherAreClaimed
                return
                    RowContext (potentialNewRowState)
            }

        member this.GetAvailableSeats () =
            this.RowSeats
            |> List.filter (fun seat -> seat.State = Seats.SeatState.Free)
            |> List.map (_.id)

        member this.Serialize(serializer: ISerializer) =
            this
            |> serializer.Serialize