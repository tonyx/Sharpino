module Sharpino.Sample.Saga.Domain.Vaucher.Voucher

open System
open Sharpino
open Sharpino.Commons

open Sharpino.Core

type Voucher = {
    Id: Guid
    NumberOfSeats: int
    RowId: Option<Guid>
}

with
    member this.IsAssigned = this.RowId.IsSome
    member this.Assign (rowId: Guid) =
        { this with RowId = Some rowId } |> Ok

    member this.UnAssign () =
        { this with RowId = None } |> Ok 

    static member Deserialize (x: string) =
        jsonPSerializer.Deserialize<Voucher> x
    static member StorageName = "_voucher"
    static member Version = "_01"
    static member SnapshotsInterval = 15
    member this.Serialize =
        jsonPSerializer.Serialize this

    interface Aggregate<string> with
        member this.Id = this.Id
        member this.Serialize = this.Serialize
