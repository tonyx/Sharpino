module Sharpino.Sample.Saga.Domain.Vaucher.Events

open System
open Sharpino
open Sharpino.Commons

open Sharpino.Core
open FSharpPlus
open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Sharpino.Sample.Saga.Domain.Vaucher.Voucher

type VoucherEvents =
    | Assigned of Guid
    | UnAssigned  

    interface Event<Voucher> with
        member
            this.Process (x: Voucher) =
                match this with
                | Assigned rowId -> x.Assign rowId
                | UnAssigned -> x.UnAssign ()

    member this.Serialize =
            jsonPSerializer.Serialize this
    static member
        Deserialize (x: string) =
                jsonPSerializer.Deserialize<VoucherEvents> x