namespace Tonyx.SeatsBooking
open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Sharpino.Utils
open Shared.Entities
open Sharpino
open Sharpino.Core
open Sharpino.Definitions
open Sharpino.Storage
open Sharpino.Lib.Core.Commons
open Shared.Entities
open System
open FSharp.Quotations
open MBrace.FsPickler.Json
open Newtonsoft.Json
open FSharp.Quotations.Evaluator.QuotationEvaluationExtensions
open Utils

module rec SeatRow =
    let pickler = FsPickler.CreateJsonSerializer(indent = false)
    let checkInvariants (row: SeatsRow) =
        row.Invariants
        |>> (fun (inv: InvariantContainer) -> (inv.UnPickled ()).Expression.Compile())
        |> List.traverseResultM
            (fun ch -> ch row)

    type Invariant<'A> =
        {
            Id: Guid
            Expression: Quotations.Expr<('A -> Result<unit, string>)>
        }

    type InvariantContainer (invariant: string) =
        member this.Invariant = invariant
        member this.UnPickled () =
            pickler.UnPickleOfString invariant
        static member Build (invariant: Invariant<SeatsRow>) =
            let pickled = pickler.PickleToString (invariant: Invariant<SeatsRow>)
            InvariantContainer pickled

    type SeatsRow private (seats: List<Seat>, id: Guid, invariants: List<InvariantContainer>) =
        let stateId = Guid.NewGuid()
        new (id: Guid) =
            SeatsRow ([], id, [])

        member this.StateId = stateId
        member this.Seats = seats
        member this.Invariants = invariants
        member this.Id = id

        member this.Serialize =
            this
            |> globalSerializer.Serialize

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
                    |> Result.ofBool "Seat already booked"

                let! checkSeatsExist =
                    let thisSeatsIds = this.Seats |>> _.Id
                    booking.SeatIds
                    |> List.forall (fun seatId -> thisSeatsIds |> List.contains seatId)
                    |> Result.ofBool "Seat not found"

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
                    |> Result.ofBool (sprintf "Seat with id '%d' already exists" seat.Id)
                let newSeats = seat :: this.Seats
                let result = SeatsRow (newSeats, this.Id, this.Invariants)
                let! checkInvariants = result |> checkInvariants
                return result
            }
        member this.RemoveSeat (seat: Seat): Result<SeatsRow, string> =
            result {
                let! belongsToMe =
                    (seat.RowId.IsSome && seat.RowId.Value = this.Id)
                    |> Result.ofBool "Seat does not belong to this row"
                let! exists =
                    this.Seats
                    |> List.tryFind (fun x -> x.Id = seat.Id)
                    |> Option.isSome
                    |> Result.ofBool (sprintf "Seat with id '%d' does not exist" seat.Id)
                let newSeats = this.Seats |> List.filter (fun x -> x.Id <> seat.Id)
                let result = SeatsRow (newSeats, this.Id, this.Invariants)
                let! checkInvariants = result |> checkInvariants
                return result
            }
        member this.AddInvariant (invariant: InvariantContainer) =
            result {
                let! exists =
                    this.Invariants
                    |> List.tryFind (fun x -> x.UnPickled().Id = invariant.UnPickled().Id)
                    |> Option.isNone
                    |> Result.ofBool "Invariant already exists"
                return SeatsRow (this.Seats, this.Id, invariant :: this.Invariants)
            }

        member this.RemoveInvariant (invariant: InvariantContainer) =
            result
                {
                    let! exists =
                        this.Invariants
                        |> List.tryFind (fun x -> x.UnPickled().Id = invariant.UnPickled().Id)
                        |> Option.isSome
                        |> Result.ofBool "Invariant does not exist"
                    let result = SeatsRow (this.Seats, this.Id, this.Invariants |> List.filter (fun x -> x.UnPickled().Id <> invariant.UnPickled().Id))
                    return result
                }

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

        member this.ToSeatsRowTO () =
            let result: SeatsRowTO =
                {
                    Id = this.Id
                    Seats = this.Seats
                }
            result

        static member Deserialize (json: string) =
            globalSerializer.Deserialize<SeatsRow> json

        static member Version = "_01"
        static member StorageName = "_seatrow"
        static member SnapshotsInterval = 6

        interface Aggregate<string> with
            member this.Id = this.Id
            member this.Serialize  =
                this.Serialize

        interface Entity with
            member this.Id = this.Id

