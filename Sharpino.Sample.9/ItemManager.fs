module Sharpino.Sample._9.ItemManager

open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Sharpino.CommandHandler
open Sharpino.Core
open Sharpino.Sample._9.Commands
open Sharpino.Sample._9.Events
open Sharpino.Sample._9.Item
open Sharpino.Storage
open Sharpino
open System

let doNothingBroker  =
    {
        notify = None
        notifyAggregate = None
    }

type ItemManager(eventStore: IEventStore<string>, itemViewer: AggregateViewer<Item>, reservationViewer: AggregateViewer<Reservation.Reservation>) =
    member this.AddItem (item: Item) =
        result {
            return!
                runInit<Item, ItemEvent, string> eventStore doNothingBroker item
        }
    
    member this.GetItem (id: Guid) =
        result {
            let! (_, item) = itemViewer id
            return item
        }
    
    member this.GetReservation (id: Guid) =
        result {
            let! (_, reservation) = reservationViewer id
            return reservation
        }
        
    member this.DeleteItem (id: Guid) =
        result {
            return!
                runDelete<Item, ItemEvent, string> eventStore doNothingBroker id (fun item -> item.ReferencesCounter = 0)
        }
    member this.AddReservation (reservation: Reservation.Reservation) =
        result {
            let itemIds =
                reservation.Reservations
                |> List.filter (fun x -> match x with | Open id -> true | _ -> false)
                |>> (fun (Open x) -> x)
            let incrementCountersCommands: List<AggregateCommand<Item, ItemEvent>> =
                itemIds |> List.map (fun _ -> ItemCommands.IncrementReferenceCounter 1)
            
            let! result =   
                runInitAndNAggregateCommandsMd<Item, ItemEvent, Reservation.Reservation, string> itemIds eventStore doNothingBroker reservation "adding reservation" incrementCountersCommands
            return result    
        }
    
    member this.CloseItemInReservation reservationId itemId =
        result {
            let! (_, reservation) = reservationViewer reservationId
            let! (_, item) = itemViewer itemId
            let! itemBelongsToReservationAndIsOpen =
                reservation.Reservations
                |> List.exists (fun x ->
                    match x with
                    | Open id when id = item.Id -> true
                    | _ -> false
                )
                |>
                Result.ofBool "Item does not belong to reservation or is already closed"
        
            let decrementCounter = ItemCommands.DecrementReferenceCounter 1
            let closeItem = ReservationCommands.CloseItem item.Id
            
            return! 
                runTwoAggregateCommandsMd<Item, ItemEvent, Reservation.Reservation, ReservationEvents.ReservationEvents, string> item.Id reservationId eventStore doNothingBroker String.Empty decrementCounter closeItem
        }    
            
        
            