namespace Tonyx.SeatsBooking.Shared

open System
open MBrace.FsPickler.Json
open Newtonsoft.Json
open Sharpino
open Sharpino.Core
open Sharpino.Lib.Core.Commons
open Sharpino.Utils

module Route =
    
    let builder typeName methodName =
        sprintf "/api/%s/%s" typeName methodName
        
module Entities =
    
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
                    
    let serializer = Utils.JsonSerializer(Utils.serSettings) :> Utils.ISerializer
    let pickler = FsPickler.CreateJsonSerializer(indent = false)
    
    type InvariantContainer (invariant: string) =
        member this.Invariant = invariant
        member this.UnPickled () =
            pickler.UnPickleOfString invariant // this.Invariant

    type SeatsRow (seats: List<Seat>, id: Guid, invariants: List<InvariantContainer>) =
        let stateId = Guid.NewGuid()
        [<JsonConstructor>]
        new  (id: Guid) = 
            SeatsRow ([], id, [])
    
        member this.StateId = stateId
        member this.Seats = seats
        member this.Invariants = invariants 
        member this.Id = id
        member this.Serialize (serializer: ISerializer) =
            this
            |> serializer.Serialize
                    
        interface Aggregate with
            member this.StateId = this.StateId
            member this.Id = this.Id
            member this.Serialize serializer = 
                this.Serialize serializer
            member this.Lock = this
        interface Entity with
            member this.Id = this.Id
                    
        static member Deserialize (serializer: ISerializer, json: string) =
            serializer.Deserialize<SeatsRow> json
        static member Version = "_01"
        static member StorageName = "_seatrow"
                    
module Services =
    open Entities
    type IRestStadiumBookingSystem =
        {
            AddRowReference : Guid -> Async<Result<unit, string>>
            BookSeats : Guid * Booking -> Async<Result<unit, string>>
            BookSeatsNRows : List<Guid * Booking> -> Async<Result<unit, string>>
            GetRow : Guid -> Async<Result<SeatsRow,string>>
            AddSeat: Guid * Seat -> Async<Result<unit, string>>
            AddSeats: Guid * List<Seat> -> Async<Result<unit, string>>
            GetAllRowReferences: unit -> Async<Result<List<Guid>,string>>
            AddInvariant: Guid * InvariantContainer -> Async<Result<unit, string>>
        }
    