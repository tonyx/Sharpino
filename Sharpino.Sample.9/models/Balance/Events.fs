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
        | CourseCreationFounded of Guid
        | CourseDeletionFounded of Guid
        interface Event<Balance> with
            member this.Process (balance: Balance) =
                match this with
                | AmountAdded amount -> balance.AddAmount amount
                | CourseCreationFounded id -> balance.FoundCourseCreation id
                | CourseDeletionFounded id -> balance.FoundCourseCancellation id
       
        static member Deserialize x =
            jsonPSerializer.Deserialize<BalanceEvents> x
        member this.Serialize =
            jsonPSerializer.Serialize this    
    