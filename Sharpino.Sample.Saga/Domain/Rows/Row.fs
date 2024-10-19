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
    currentBookings: int
    Id: Guid
}

with
    member this.IsFull = this.currentBookings >= this.size 
    member this.AddBooking() =
        if this.IsFull then
            Error "row is full"
        else
            Ok { this with currentBookings = this.currentBookings + 1 }
    
    member this.AddBookings (n: int) =
        if this.currentBookings + n > this.size then
            Error "row is full"
        else
            Ok { this with currentBookings = this.currentBookings + n }
    
    member this.FreeBookings() =
        if this.currentBookings <= 0 then
            Error "row is free"
        else
            Ok { this with currentBookings = this.currentBookings - 1 }

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
