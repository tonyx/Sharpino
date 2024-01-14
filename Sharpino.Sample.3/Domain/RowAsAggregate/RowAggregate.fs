namespace seatsLockWithSharpino
// module Sharpino.Sample._3.Domain.RowAsAggregate.RowAggregate
open FsToolkit.ErrorHandling
open Sharpino.Utils
open Sharpino
open Sharpino.Core
open System
open Seats
module RowAggregate =
    let serializer = new Utils.JsonSerializer(Utils.serSettings) :> Utils.ISerializer
    
    let private emptyAll (seats: List<Seat>) = 
        seats 
        |> List.map 
            (fun x -> { 
                        x with 
                            State = SeatState.Free }
            )



    // make this private
    type RefactoredRow (seats: List<Seat>, id: Guid) =
        let seats = seats
        let id = id

        new () = 
            let id = Guid.NewGuid ()
            new RefactoredRow ([], id)

        private new (seats: List<Seat>) = 
            let id = Guid.NewGuid ()
            new RefactoredRow (seats, id)

        member this.Seats = seats
        member this.Id = id

        member this.IsAvailable (seatId: Seats.Id) =
            this.Seats
            |> List.filter (fun seat -> seat.id = seatId)
            |> List.exists (fun seat -> seat.State = Seats.SeatState.Free)
            
        member this.BookSeats (booking: Booking) =
            result {
                let! check = 
                    let seatsInvolved =
                        seats
                        |> List.filter (fun seat -> booking.seats |> List.contains seat.id)
                    seatsInvolved
                        |> List.forall (fun seat -> seat.State = SeatState.Free)
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
                    |> List.sortBy _.id
                
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
                    RefactoredRow (potentialNewRowState, id)
            }
        member this.AddSeat (seat: Seat): Result<RefactoredRow, string> =
            let newSeats = seat :: seats
            RefactoredRow (newSeats, id) |> Ok

        member this.AddSeats (seats: List<Seat>): Result<RefactoredRow, string> =
            let newSeats = this.Seats @ seats
            RefactoredRow (newSeats, id) |> Ok
        member this.GetAvailableSeats () =
            seats
            |> List.filter (fun seat -> seat.State = Seats.SeatState.Free)
            |> List.map _.id

        static member Deserialize (json: string) =
            serializer.Deserialize<RefactoredRow> json
            
        interface Aggregate with
            member this.Id = this.Id
            member this.Version = "_01"
            member this.StorageName = "_seatrow"
            member this.Serialize serializer = 
                this
                |> serializer.Serialize
            member this.Zero () =   
                new RefactoredRow (emptyAll this.Seats, this.Id)
            member this.Lock = this

    type RowAggregateEvent =        
        | SeatBooked of Seats.Booking  
        | SeatAdded of Seats.Seat
        | SeatsAdded of Seats.Seat list
            interface Event<RefactoredRow> with
                member this.Process (x: RefactoredRow) =
                    match this with
                    | SeatBooked booking ->
                        x.BookSeats booking
                    | SeatAdded seat ->
                        x.AddSeat seat
                    | SeatsAdded seats ->
                        x.AddSeats seats

        member this.Serialize(serializer: ISerializer) =
            this
            |> serializer.Serialize
        static member Deserialize(serializer: ISerializer) =
            serializer.Deserialize<RowAggregateEvent>

    type RowAggregateCommand =
        | BookSeats of Seats.Booking
        | AddSeat of Seats.Seat
        | AddSeats of List<Seats.Seat>
            interface Command<RefactoredRow, RowAggregateEvent> with
                member this.Execute (x: RefactoredRow) =
                    match this with
                    | BookSeats booking ->
                        x.BookSeats booking
                        |> Result.map (fun row -> [SeatBooked booking])
                    | AddSeat seat ->
                        x.AddSeat seat
                        |> Result.map (fun row -> [SeatAdded seat])
                    | AddSeats seats ->
                        x.AddSeats seats
                        |> Result.map (fun row -> [SeatsAdded seats])
                member this.Undoer =
                    None