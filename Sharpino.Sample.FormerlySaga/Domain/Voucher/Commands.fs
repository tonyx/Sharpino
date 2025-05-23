module Sharpino.Sample.Saga.Domain.Vaucher.Commands

open Sharpino.Sample.Saga.Domain.Vaucher.Events
open Sharpino.Sample.Saga.Domain.Vaucher.Voucher
open System
open Sharpino
open Sharpino.Commons
open Sharpino.Core
open FSharpPlus
open FSharpPlus.Operators
open FsToolkit.ErrorHandling

type VoucherCommands =
    | Consume of int

    interface AggregateCommand<Voucher, VoucherEvents> with
        member
            this.Execute (x: Voucher) =
                match this with
                | Consume n->
                    x.Consume n
                    |> Result.map (fun s -> (s, [Consumed n]))

        member this.Undoer =
            None