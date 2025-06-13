namespace Sharpino.Sample._9

open System
open Sharpino.Commons
open Sharpino.Core
open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Sharpino.Sample._9.Reservation

module ReservationEvents =
    type ReservationEvents =
        | ItemClosed of Guid
        interface Event<Reservation> with
            member this.Process (x: Reservation.Reservation) = 
                match this with
                | ItemClosed id ->
                    x.CloseItem id
                
        static member Deserialize x =
            jsonPSerializer.Deserialize<ReservationEvents> x
        member this.Serialize =
            jsonPSerializer.Serialize this    
    