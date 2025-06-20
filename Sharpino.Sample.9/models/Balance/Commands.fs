namespace Sharpino.Sample._9

open System
open Sharpino
open Sharpino.Commons
open Sharpino.Core
open Sharpino.Sample._9.Balance
open Sharpino.Sample._9.BalanceEvents

module BalanceCommands =
    type BalanceCommands =
        | AddAmount of decimal
        | PayCourseCreationFee of Guid
        | PayCourseCancellationFee of Guid
        interface AggregateCommand<Balance, BalanceEvents> with
            member this.Execute (balance: Balance) =
                match this with
                | AddAmount amount ->
                    balance.AddAmount amount
                    |> Result.map (fun i -> (i, [AmountAdded amount]))
                | PayCourseCreationFee id ->
                    balance.PayCourseCreationFee id
                    |> Result.map (fun i -> (i, [CourseCreationFeePaid id]))
                | PayCourseCancellationFee id ->
                    balance.PayCourseCancellationFee id
                    |> Result.map (fun i -> (i, [CourseCancellationFeePaid id]))
            member this.Undoer =
                None

