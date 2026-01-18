namespace Sharpino.Sample._13.Models
open System
open FSharpPlus.Operators
open System.Text.Json
open Sharpino.Commons
open Sharpino
open Sharpino.Core
open Sharpino.Sample._13.Commons
open FsToolkit.ErrorHandling

module Reservation =
    type ReservationForNickNames = {
        Id: Guid
        Claims: List<UserId * string>
    }
    with
        static member MkReservationForNickNames () = {
            Id = Guid.NewGuid()
            Claims = []
        }
        member this.AddClaim (reservation: UserId * string) =
            result
                {
                    do!
                        (this.Claims |>> snd)
                        |> List.exists (fun r -> r = (reservation |> snd))
                        |> not
                        |> Result.ofBool (sprintf "Reservation for name %s exists" (reservation |> snd))
                    return
                        {
                            this with
                                Claims = this.Claims @ [reservation]
                        }
                } 
        member this.RemoveClaim (reservation: UserId * string) =
            result
                {
                    return
                        {
                            this with
                                Claims = this.Claims |> List.filter (fun r -> r <> reservation)
                        }
                }
                
        member this.Claim (reservation: UserId * string) =
            if this.Claims |> List.exists (fun r -> r = reservation) then
                this.RemoveClaim reservation
            else
                Error "claim not found"
         
        static member Version = "_01"
        static member StorageName = "_ReservationForNickNames"
        static member SnapshotsInterval = 15
        
        static member Deserialize (x: string): Result<ReservationForNickNames, string> =
            try
                JsonSerializer.Deserialize<ReservationForNickNames>(x, jsonOptions) |> Ok
            with
            | ex -> Error ex.Message
        
        member this.Serialize =
            JsonSerializer.Serialize(this, jsonOptions)
        

