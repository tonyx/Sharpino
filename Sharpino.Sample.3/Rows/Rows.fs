namespace Tonyx.SeatsBooking
open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Sharpino.Utils
open Sharpino
open Sharpino.Core
open Sharpino.Storage
open Sharpino.Lib.Core.Commons
open Tonyx.SeatsBooking.Shared.Entities
open System
open FSharp.Quotations
open MBrace.FsPickler.Json
open Newtonsoft.Json
open FSharp.Quotations.Evaluator.QuotationEvaluationExtensions
open Tonyx.SeatsBooking
open Tonyx.SeatsBooking.Seats
module rec SeatRow =
    // let serializer = Utils.JsonSerializer(Utils.serSettings) :> Utils.ISerializer
    // let pickler = FsPickler.CreateJsonSerializer(indent = false)
    let checkInvariants (row: SeatsRow) =
        row.Invariants
        |>> (fun (inv: InvariantContainer) -> ((inv.UnPickled ()) :> Invariant<SeatsRow>).Compile())
        |> List.traverseResultM 
            (fun ch -> ch row)
            
   // due to serialization issues, we need to wrap the invariant in a container as a string.
   // the pickler will then be able to serialize and deserialize the invariant
    // type Invariant = Quotations.Expr<(SeatsRow -> Result<bool, string>)>
    // type Invariant = Quotations.Expr<(SeatsRow -> Result<bool, string>)>
    type Invariant<'A> = Quotations.Expr<('A -> Result<bool, string>)>
    
    // type InvariantContainer (invariant: string) =
    //     member this.Invariant = invariant
    //     member this.UnPickled () =
    //         pickler.UnPickleOfString invariant // this.Invariant
    
    // type SeatsRow private (seats: List<Seat>, id: Guid, invariants: List<InvariantContainer>) =
    //     let stateId = Guid.NewGuid()    
    //     new (id: Guid) = 
    //         SeatsRow ([], id, [])
    //
    //     member this.StateId = stateId
    //     member this.Seats = seats
    //     member this.Invariants = invariants 
    //     member this.Id = id

        
    type SeatsRow with    
        member this.Serialize (serializer: ISerializer) =
            this
            |> serializer.Serialize

        member this.IsAvailable (seatId: Id) =
            this.Seats
            |> List.filter (fun seat -> seat.Id = seatId)
            |> List.exists (fun seat -> seat.State = SeatState.Free)
            
        member this.BookSeats (booking: Booking) =
            result {
                let! checkSeatsAreFree = 
                    this.Seats
                    |> List.filter (fun seat -> booking.SeatIds |> List.contains seat.Id)
                    |> List.forall (fun seat -> seat.State = SeatState.Free)
                    |> boolToResult "Seat already booked"
                    
                let! checkSeatsExist =
                    let thisSeatsIds = this.Seats |>> _.Id
                    booking.SeatIds
                    |> List.forall (fun seatId -> thisSeatsIds |> List.contains seatId)
                    |> boolToResult "Seat not found"
                
                let claimedSeats = 
                    this.Seats
                    |> List.filter (fun seat -> booking.SeatIds |> List.contains seat.Id)
                    |>> (fun seat -> { seat with State = SeatState.Booked })

                let unclaimedSeats = 
                    this.Seats
                    |> List.filter (fun seat -> not (booking.SeatIds |> List.contains seat.Id))

                let potentialNewRowState = 
                    claimedSeats @ unclaimedSeats
                    |> List.sortBy _.Id
               
                let result = SeatsRow (potentialNewRowState, this.Id, this.Invariants)
                let! checkInvariant =  result |> checkInvariants
                return
                    result
            }
        member this.AddSeat (seat: Seat): Result<SeatsRow, string> =
            result {
                let! notAlreadyExists =
                    this.Seats
                    |> List.tryFind (fun x -> x.Id = seat.Id)
                    |> Option.isNone
                    |> boolToResult (sprintf "Seat with id '%d' already exists" seat.Id)
                let newSeats = seat :: this.Seats
                let result = SeatsRow (newSeats, this.Id, this.Invariants)
                let! checkInvariants = result |> checkInvariants
                return result
            }
        member this.AddInvariant (invariant: InvariantContainer) =
            SeatsRow (this.Seats, this.Id, invariant :: this.Invariants) |> Ok

        member this.AddSeats (seats: List<Seat>): Result<SeatsRow, string> =
            let newSeats = this.Seats @ seats
            result {
                let result = SeatsRow (newSeats, this.Id, this.Invariants)
                let! checkInvariants =  result |> checkInvariants
                return result
            }
        member this.GetAvailableSeats () =
            this.Seats
            |> List.filter (fun seat -> seat.State = SeatState.Free)
            |> List.map _.Id

        // static member Deserialize (serializer: ISerializer, json: string) =
        //     serializer.Deserialize<SeatsRow> json

        // static member Version = "_01"
        // static member StorageName = "_seatrow"
            
        // interface Aggregate with
        //     member this.StateId = this.StateId
        //     member this.Id = this.Id
        //     member this.Serialize serializer = 
        //         this.Serialize serializer
        //     member this.Lock = this
        // interface Entity with
        //     member this.Id = this.Id
        
        
