namespace Sharpino.Sample._10.Models
open System
open Sharpino
open FsToolkit.ErrorHandling
open Sharpino.Commons
open Sharpino.Core

module Counter =
    type Counter =
        {
            Id: Guid
            Value: int
        }
        with
            static member Initial =
                {
                    Id = Guid.NewGuid()
                    Value = 0
                }
            member this.Increment () =
                { this with Value = this.Value + 1 } |> Ok
            member this.Decrement () =
                { this with Value = this.Value - 1 } |> Ok
            
            member this.Serialize =
                binarySerializer.Serialize this
            static member Deserialize x =
                binarySerializer.Deserialize<Counter> x
            static member StorageName = "_counter"
            static member Version = "_01"
            static member SnapshotsInterval = 15
            
            interface Aggregate<byte[]> with
                member this.Id = this.Id
                member this.Serialize  =
                    this.Serialize
       
    




