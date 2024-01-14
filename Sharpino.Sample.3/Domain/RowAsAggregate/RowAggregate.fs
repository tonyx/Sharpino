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

    // this is my aggregate
    type RefactoredRow(seats: List<Seat>, id: Guid) =
        member this.Seats = seats
        member this.Id = id
        new (seats: List<Seat>) = 
            let id = Guid.NewGuid()
            new RefactoredRow(seats, id)
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

    type RowAggregateEvent =        
        | SeatBooked of Seats.Booking  
            interface Event<RefactoredRow> with
                member this.Process (x: RefactoredRow) =
                    match this with
                    | SeatBooked booking ->
                        x.BookSeats booking
        member this.Serialize(serializer: ISerializer) =
            this
            |> serializer.Serialize
        static member Deserialize(serializer: ISerializer) =
            serializer.Deserialize<RowAggregateEvent>

    type RowAggregateCommand =
        | BookSeats of Seats.Booking
            interface Command<RefactoredRow, RowAggregateEvent> with
                member this.Execute (x: RefactoredRow) =
                    match this with
                    | BookSeats booking ->
                        x.BookSeats booking
                        |> Result.map (fun row -> [SeatBooked booking])
                member this.Undoer =
                    None