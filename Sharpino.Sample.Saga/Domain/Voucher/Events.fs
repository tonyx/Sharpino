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
    | Consumed of int

    interface Event<Voucher> with
        member
            this.Process (x: Voucher) =
                match this with
                | Consumed n -> x.Consume n

    member this.Serialize =
            jsonPSerializer.Serialize this
    static member
        Deserialize (x: string) =
                jsonPSerializer.Deserialize<VoucherEvents> x