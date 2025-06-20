namespace Sharpino.Sample._9

open System
open Sharpino.Commons
open Sharpino.Core  
open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Sharpino.Sample._9.Balance

module BalanceEvents =
    type BalanceEvents =
        | AmountAdded of decimal
        | CourseCreationFeePaid of Guid
        | CourseCancellationFeePaid of Guid
        interface Event<Balance> with
            member this.Process (balance: Balance) =
                match this with
                | AmountAdded amount -> balance.AddAmount amount
                | CourseCreationFeePaid id -> balance.PayCourseCreationFee id
                | CourseCancellationFeePaid id -> balance.PayCourseCancellationFee id
       
        static member Deserialize x =
            jsonPSerializer.Deserialize<BalanceEvents> x
        member this.Serialize =
            jsonPSerializer.Serialize this    
    