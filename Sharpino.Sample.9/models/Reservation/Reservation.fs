namespace Sharpino.Sample._9
open System
open Sharpino.Commons
open Sharpino.Core
open FSharpPlus.Operators
open FsToolkit.ErrorHandling

type ItemReservation =
    | Open of Guid
    | Closed

module Reservation =
    let maxItems = 10
    
    type Reservation = {
        Id: Guid
        Reservations: List<ItemReservation>
    }
    with
        static member MkReservation (itemReservations: List<Guid>) =
            match itemReservations.Length with
            | 0 -> Error "No items"
            | n when n > maxItems -> Error "Too many items"
            | _ ->
                {
                    Id = Guid.NewGuid()
                    Reservations =
                        itemReservations
                        |>> Open
                }
                |> Ok

        member this.CloseItem (itemId: Guid) =
            result
                {
                    let! itemExists = 
                        this.Reservations
                        |> List.tryFind (fun x -> match x with | Open id -> id = itemId | Closed -> false)
                        |> Result.ofOption (sprintf "Item with id '%A' does not exist" itemId)
                  
                    let newReservations =
                        this.Reservations
                        |>> (fun x -> match x with | Open id -> if id = itemId then Closed else x) 
                     
                    return { this with Reservations = newReservations }
                }
                
        member this.Ping () =
            this |> Ok
                
        static member Version = "_01"
        static member StorageName = "_reservations"
        static member SnapshotsInterval = 10
        member this.Serialize =
            this
            |> jsonPSerializer.Serialize
        static member Deserialize x =
            jsonPSerializer.Deserialize<Reservation> x
        
        interface Aggregate<string> with
            member this.Id =
                this.Id
            member this.Serialize  =
                this.Serialize
        
        
            
