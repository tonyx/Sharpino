module Sharpino.Sample.Saga.Domain.Seat.Row
open System
open Sharpino
open Sharpino.Commons

open Sharpino.Core
open FSharpPlus
open FSharpPlus.Operators
open FsToolkit.ErrorHandling

type Row = {
    size: int
    currentReservations: int
    Id: Guid
}

with
    member this.IsFull = this.currentReservations >= this.size 
    member this.AddReservation() =
        if this.IsFull then
            Error "row is full"
        else
            Ok { this with currentReservations = this.currentReservations + 1 }
    
    member this.AddReservations(n: int) =
        if this.currentReservations + n > this.size then
            Error "row is full"
        else
            Ok { this with currentReservations = this.currentReservations + n }
    
    member this.FreeReservation() =
        if this.currentReservations <= 0 then
            Error "row is free"
        else
            Ok { this with currentReservations = this.currentReservations - 1 }

    static member Deserialize(x: string) =
        jsonPSerializer.Deserialize<Row> x

    static member StorageName = "_seat"
    static member Version = "_01"
    static member SnapshotsInterval = 15

    member this.Serialize =
        jsonPSerializer.Serialize this
    
    interface Aggregate<string> with
        member this.Id = this.Id
        member this.Serialize = this.Serialize
