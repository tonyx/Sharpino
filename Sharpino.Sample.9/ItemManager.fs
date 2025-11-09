module Sharpino.Sample._9.ItemManager

open System.Threading.Tasks
open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Sharpino.CommandHandler
open Sharpino.Core
open Sharpino.EventBroker
open Sharpino.Sample._9.Commands
open Sharpino.Sample._9.Events
open Sharpino.Sample._9.Item
open Sharpino.Storage
open Sharpino
open System


type ItemManager (eventStore: IEventStore<string>, itemViewer: AggregateViewer<Item>, reservationViewer: AggregateViewer<Reservation.Reservation>, messageSender: MessageSenders ) =
    new (eventStore: IEventStore<string>, itemViewer: AggregateViewer<Item>, reservationViewer: AggregateViewer<Reservation.Reservation>) =
        ItemManager (eventStore, itemViewer, reservationViewer, MessageSenders.NoSender)
        
    member this.AddItem (item: Item) =
        result {
            return!
                runInit<Item, ItemEvent, string> eventStore messageSender item
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
                runDelete<Item, ItemEvent, string> eventStore messageSender id (fun item -> item.ReferencesCounter = 0)
        }
        
    member this.AddReservation (reservation: Reservation.Reservation) =
        result {
            let itemIds =
                reservation.Reservations
                |> List.filter _.IsOpen
                |>> (fun (Open x) -> x)
            let incrementCountersCommands: List<AggregateCommand<Item, ItemEvent>> =
                itemIds |> List.map (fun _ -> ItemCommands.IncrementReferenceCounter 1)
            
            let! result =   
                runInitAndNAggregateCommandsMd<Item, ItemEvent, Reservation.Reservation, string>
                    itemIds eventStore messageSender reservation "adding reservation" incrementCountersCommands
            return result    
        }
    
    member this.CloseItemInReservation reservationId itemId =
        result {
            let! (_, reservation) = reservationViewer reservationId
            let! (_, item) = itemViewer itemId
            let! itemBelongsToReservationAndIsOpen =
                reservation.Reservations
                |> List.exists _.IsOpen
                |>
                Result.ofBool "Item does not belong to reservation or is already closed"
        
            let decrementCounter = ItemCommands.DecrementReferenceCounter 1
            let closeItem = ReservationCommands.CloseItem item.Id
            
            return! 
                runTwoAggregateCommandsMd<Item, ItemEvent, Reservation.Reservation, ReservationEvents.ReservationEvents, string> item.Id reservationId eventStore messageSender String.Empty decrementCounter closeItem
        }    
            
        
            