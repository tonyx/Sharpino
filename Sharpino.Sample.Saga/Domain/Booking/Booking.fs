module Sharpino.Sample.Saga.Domain.Booking.Booking

open System
open Sharpino
open Sharpino.Commons

open Sharpino.Core
open Sharpino.Sample.Saga.Commons.Commons

type Booking = {
    Id: BookingId
    ClaimedSeats: int
    RowId: Option<RowId>
}

with
    member this.IsAssigned = this.RowId.IsSome
    
    member this.Assign (rowId: RowId) =
        { this with RowId = Some rowId } |> Ok
    member this.UnAssign () =
        { this with RowId = None } |> Ok
        
    static member Deserialize (x: string) =
        jsonPSerializer.Deserialize<Booking> x
    static member StorageName = "_booking"
    static member Version = "_01"
    static member SnapshotsInterval = 15
    member this.Serialize =
        jsonPSerializer.Serialize this
        
    interface Aggregate<string> with
        member this.Id = this.Id
        member this.Serialize = this.Serialize
        