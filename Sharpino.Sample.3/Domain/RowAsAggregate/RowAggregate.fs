namespace seatsLockWithSharpino
// module Sharpino.Sample._3.Domain.RowAsAggregate.RowAggregate
open FsToolkit.ErrorHandling
open Sharpino.Utils
open Sharpino
open Sharpino.Core
open Sharpino.Lib.Core.Commons
open System
open Seats
module RefactoredRow =
    let serializer = new Utils.JsonSerializer(Utils.serSettings) :> Utils.ISerializer

    type SeatsRow private (seats: List<Seat>, id: Guid) =
        let seats = seats
        let id = id

        new (id: Guid) = 
            new SeatsRow ([], id)

        member this.Seats = seats
        member this.Id = id

        member this.Serialize (serializer: ISerializer) =
            this
            |> serializer.Serialize

        member this.IsAvailable (seatId: Seats.Id) =
            this.Seats
            |> List.filter (fun seat -> seat.Id = seatId)
            |> List.exists (fun seat -> seat.State = Seats.SeatState.Free)
            
        member this.BookSeats (booking: Booking) =
            result {
                let! checkSeatsAreFree = 
                    seats
                    |> List.filter (fun seat -> booking.SeatIds |> List.contains seat.Id)
                    |> List.forall (fun seat -> seat.State = SeatState.Free)
                    |> boolToResult "Seat already booked"
                        
                let! checkSeatsExist =
                    let thisSeatsIds = this.Seats |> List.map _.Id
                    booking.SeatIds
                    |> List.forall (fun seatId -> thisSeatsIds |> List.contains seatId)
                    |> boolToResult "Seat not found"
                
                let claimedSeats = 
                    seats
                    |> List.filter (fun seat -> booking.SeatIds |> List.contains seat.Id)
                    |> List.map (fun seat -> { seat with State = Seats.SeatState.Booked })

                let unclaimedSeats = 
                    seats
                    |> List.filter (fun seat -> not (booking.SeatIds |> List.contains seat.Id))

                let potentialNewRowState = 
                    claimedSeats @ unclaimedSeats
                    |> List.sortBy _.Id
                
                // it just checks that the middle one can't be free, but
                // actually it was supposed to be "no single seat left" anywhere A.F.A.I.K.
                let theSeatInTheMiddleCantRemainFreeIfAllTheOtherAreClaimed =
                    (potentialNewRowState.Length = 5 &&
                    potentialNewRowState.[0].State = Seats.SeatState.Booked &&
                    potentialNewRowState.[1].State = Seats.SeatState.Booked &&
                    potentialNewRowState.[2].State = Seats.SeatState.Free &&
                    potentialNewRowState.[3].State = Seats.SeatState.Booked &&
                    potentialNewRowState.[4].State = Seats.SeatState.Booked)
                    |> not
                    |> boolToResult "error: can't leave a single seat free in the middle"
                let! checkInvariant = theSeatInTheMiddleCantRemainFreeIfAllTheOtherAreClaimed
                return
                    SeatsRow (potentialNewRowState, id)
            }
        member this.AddSeat (seat: Seat): Result<SeatsRow, string> =
            result {
                let! notAlreadyExists =
                    seats
                    |> List.tryFind (fun x -> x.Id = seat.Id)
                    |> Option.isNone
                    |> boolToResult (sprintf "Seat with id '%d' already exists" seat.Id)
                let newSeats = seat :: seats
                return SeatsRow (newSeats, id)
            }

        member this.AddSeats (seats: List<Seat>): Result<SeatsRow, string> =
            let newSeats = this.Seats @ seats
            SeatsRow (newSeats, id) |> Ok
        member this.GetAvailableSeats () =
            seats
            |> List.filter (fun seat -> seat.State = Seats.SeatState.Free)
            |> List.map _.Id

        static member Deserialize (serializer: ISerializer, json: string) =
            serializer.Deserialize<SeatsRow> json

        static member Version = "_01"
        static member StorageName = "_seatrow"
            
        interface Aggregate with
            member this.Id = this.Id
            member this.Serialize serializer = 
                this.Serialize serializer
            member this.Lock = this
        interface Entity with
            member this.Id = this.Id

    type RowAggregateEvent =        
        | SeatBooked of Seats.Booking  
        | SeatAdded of Seats.Seat
        | SeatsAdded of Seats.Seat list
            interface Event<SeatsRow> with
                member this.Process (x: SeatsRow) =
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
        static member Deserialize(serializer: ISerializer, x: string) =
            serializer.Deserialize<RowAggregateEvent> x

    type RowAggregateCommand =
        | BookSeats of Seats.Booking
        | AddSeat of Seats.Seat
        | AddSeats of List<Seats.Seat>
            interface Command<SeatsRow, RowAggregateEvent> with
                member this.Execute (x: SeatsRow) =
                    match this with
                    | BookSeats booking ->
                        x.BookSeats booking
                        |> Result.map (fun _ -> [SeatBooked booking])
                    | AddSeat seat ->
                        x.AddSeat seat
                        |> Result.map (fun _ -> [SeatAdded seat])
                    | AddSeats seats ->
                        x.AddSeats seats
                        |> Result.map (fun _ -> [SeatsAdded seats])
                member this.Undoer =
                    None