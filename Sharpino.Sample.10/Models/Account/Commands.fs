
namespace Sharpino.Sample._10.Models
open System
open FsToolkit.ErrorHandling
open Sharpino.Commons
open Sharpino.Core
open Sharpino.Sample._10.Models.Account
open Sharpino.Sample._10.Models.AccountEvents

module AccountCommands =
    type AccountCommands =
        | AddAmount of int
        | RemoveAmount of int
        interface AggregateCommand<Account, AccountEvents> with
            member this.Execute (account: Account) =
                match this with
                | AddAmount amount ->
                    account.AddAmount amount
                    |> Result.map (fun i -> (i, [AmountAdded amount]))
                | RemoveAmount amount ->
                    account.RemoveAmount amount
                    |> Result.map (fun i -> (i, [AmountRemoved amount]))
                        
            member this.Undoer =
                None
