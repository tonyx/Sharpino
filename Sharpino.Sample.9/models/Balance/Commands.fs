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
        | FoundCourseCreation of Guid
        | FoundCourseCancellation of Guid
        interface AggregateCommand<Balance, BalanceEvents> with
            member this.Execute (balance: Balance) =
                match this with
                | AddAmount amount ->
                    balance.AddAmount amount
                    |> Result.map (fun i -> (i, [AmountAdded amount]))
                | FoundCourseCreation id ->
                    balance.FoundCourseCreation id
                    |> Result.map (fun i -> (i, [CourseCreationFounded id]))
                | FoundCourseCancellation id ->
                    balance.FoundCourseCancellation id
                    |> Result.map (fun i -> (i, [CourseDeletionFounded id]))
            member this.Undoer =
                None

