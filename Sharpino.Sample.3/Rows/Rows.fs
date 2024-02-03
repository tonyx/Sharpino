namespace Tonyx.SeatsBooking
open FsToolkit.ErrorHandling
open Sharpino.Utils
open Sharpino
open Sharpino.Core
open Sharpino.Storage
open Sharpino.Lib.Core.Commons
open System
open FSharp.Quotations
open MBrace.FsPickler.Json
open Newtonsoft.Json
open FSharp.Quotations.Evaluator.QuotationEvaluationExtensions
open Tonyx.SeatsBooking
open Tonyx.SeatsBooking.Seats
module rec SeatRow =
    let serializer = new Utils.JsonSerializer(Utils.serSettings) :> Utils.ISerializer
    let pickler = FsPickler.CreateJsonSerializer(indent = false)
        
   // due to serialization issues, we need to wrap the invariant in a container as a string.
   // the pickler will then be able to serialize and deserialize the invariant
    type InvariantContainer (invariant: string) =
        member this.Invariant = invariant
    type SeatsRow private (seats: List<Seat>, id: Guid, invariants: List<InvariantContainer>) =
            
        new (id: Guid) = 
            new SeatsRow ([], id, [])

        member this.Seats = seats
        member this.Invariants = invariants 
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
               
                let result = SeatsRow (potentialNewRowState, id, this.Invariants)
                
                let! checkInvariants =
                    this.Invariants
                    |> List.map (fun (inv: InvariantContainer) -> (pickler.UnPickleOfString (inv.Invariant) :> Invariant).Compile())
                    |> List.traverseResultM 
                        (fun ch -> ch result)
                return
                    result
            }
        member this.AddSeat (seat: Seat): Result<SeatsRow, string> =
            result {
                let! notAlreadyExists =
                    seats
                    |> List.tryFind (fun x -> x.Id = seat.Id)
                    |> Option.isNone
                    |> boolToResult (sprintf "Seat with id '%d' already exists" seat.Id)
                let newSeats = seat :: seats
                return SeatsRow (newSeats, id, this.Invariants)
            }
        member this.AddInvariant (invariant: InvariantContainer) =
            SeatsRow (seats, id, invariant :: this.Invariants) |> Ok

        member this.AddSeats (seats: List<Seat>): Result<SeatsRow, string> =
            let newSeats = this.Seats @ seats
            SeatsRow (newSeats, id, this.Invariants) |> Ok
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
    type Invariant = Quotations.Expr<(SeatsRow -> Result<bool, string>)>
        
        
