namespace Sharpino.Sample._10.Models

open System
open FsToolkit.ErrorHandling
open Sharpino.Commons
open Sharpino.Core

module Account =
    type Account = {
        Id: Guid
        Name: string
        Amount: int
    }
    with
        static member
            MkAccount (name: string, amount: int) =
                {
                    Id = Guid.NewGuid()
                    Name = name
                    Amount = amount
                }
        member this.AddAmount (amount: int) =
            result {
                return { this with Amount = this.Amount + amount }
            }
        member this.RemoveAmount (amount: int) =
            result {
                return { this with Amount = this.Amount - amount }
            }    
        member this.Serialize =
            binarySerializer.Serialize this
        static member Deserialize x =
            binarySerializer.Deserialize<Account> x
        static member StorageName = "_account"
        static member Version = "_01"
        static member SnapshotsInterval = 15
        
        interface Aggregate<byte[]> with
            member this.Id = this.Id
            member this.Serialize  =
                this.Serialize
