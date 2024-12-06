module Sharpino.Sample.Saga.Domain.Vaucher.Voucher

open System
open FsToolkit.ErrorHandling
open Sharpino
open Sharpino.Commons

open Sharpino.Core
open Sharpino.Sample.Saga.Commons.Commons

type Voucher = {
    Id: VoucherId
    Capacity: int
}

with
    // member this.IsConsumed () = this.Consumed
    member this.Consume (n: int) =
        ResultCE.result
            {
                do!
                    this.Capacity - n >= 0
                    |> Result.ofBool "cannot assign vouchers to seats that are more than the voucher capacity" 
               
                return
                    {
                        this with
                            Capacity = this.Capacity - n
                    }
            }

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
