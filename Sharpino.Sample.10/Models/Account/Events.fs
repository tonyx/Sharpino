namespace Sharpino.Sample._10.Models
open System
open FsToolkit.ErrorHandling
open Sharpino.Commons
open Sharpino.Core
open Sharpino.Sample._10.Models.Account

module AccountEvents =
    type AccountEvents =
        | AmountAdded of int
        | AmountRemoved of int
        interface Event<Account> with
            member this.Process (account: Account) =
                match this with
                | AmountAdded amount -> account.AddAmount amount
                | AmountRemoved amount -> account.AddAmount (-amount)
        static member Deserialize x =
            binarySerializer.Deserialize<AccountEvents> x
        member this.Serialize =
            binarySerializer.Serialize this
    
    
