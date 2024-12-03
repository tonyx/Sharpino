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
    | Assign of Guid
    | UnAssign

    interface AggregateCommand<Voucher, VoucherEvents> with
        member
            this.Execute (x: Voucher) =
                match this with
                | Assign rowId ->
                    x.Assign rowId
                    |> Result.map (fun s -> (s, [Assigned rowId]))
                | UnAssign ->
                    x.UnAssign ()
                    |> Result.map (fun s -> (s, [UnAssigned]))

        member this.Undoer =
            None
            
            // match this with
            // | Assign _ ->
            //     Some (fun (voucher: Voucher) (viewer: AggregateViewer<Voucher>) ->
            //         result {
            //             let! (i, _) = viewer (voucher.Id)
            //             return
            //                 fun () ->
            //                     result {
            //                         let! (j, state) = viewer (voucher.Id)
            //                         let! isGreater =
            //                             (j >= i)
            //                             |> Result.ofBool "concurrency error"
            //                         let result =
            //                             state.UnAssign ()
            //                             |> Result.map (fun _ -> [UnAssigned])
            //                         return! result    
            //                     }
            //             }
            //         )
            //     
            // | UnAssign ->
            //     Some (fun (voucher: Voucher) (viewer: AggregateViewer<Voucher>) ->
            //         result {
            //             let! (i, state) = viewer (voucher.Id)
            //             let!
            //                 rowId =
            //                     voucher.RowId
            //                     |> Result.ofOption "row not assigned"
            //             return
            //                 fun () ->
            //                     result {
            //                         let! (j, _) = viewer (voucher.Id)
            //                         let! isGreater =
            //                             (j >= i)
            //                             |> Result.ofBool "concurrency error"
            //                         let result =
            //                             state.Assign rowId
            //                             |> Result.map (fun _ -> [Assigned rowId])
            //                         return! result    
            //                     }
            //         }
            //     )