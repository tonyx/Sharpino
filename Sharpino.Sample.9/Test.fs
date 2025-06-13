module Sharpino.Sample._9.Test

open Expecto
open ItemManager
open ItemManager.Common
open Sharpino.CommandHandler
open Sharpino.Sample._9.Events
open Sharpino.Sample._9.Item
open Sharpino.TestUtils

let pgStorageItemViewer = getAggregateStorageFreshStateViewer<Item, ItemEvent, string> pgEventStore
let memoryStorageItemViewer = getAggregateStorageFreshStateViewer<Item, ItemEvent, string> memEventStore

let pgStorageReservationViewer = getAggregateStorageFreshStateViewer<Reservation.Reservation, ReservationEvents.ReservationEvents, string> pgEventStore
let memoryStorageReservationViewer = getAggregateStorageFreshStateViewer<Reservation.Reservation, ReservationEvents.ReservationEvents, string> memEventStore

let instances =
    [
        (fun () -> setUp(pgEventStore)), ItemManager(pgEventStore, pgStorageItemViewer, pgStorageReservationViewer)
        (fun () -> setUp(memEventStore)),  ItemManager(memEventStore, memoryStorageItemViewer, memoryStorageReservationViewer)
    ]

[<Tests>]
let tests =
    testList "Sharpino.Sample._9" [
        multipleTestCase "create a new item - Ok" instances <| fun (setUp, itemManger) ->
            setUp()
            let item = Item.MkItem ("name", "description")
            let addItem = itemManger.AddItem item
            Expect.isOk addItem "should be ok"
            let tryGetItem = itemManger.GetItem item.Id
            Expect.isOk tryGetItem "should be ok"
            
        multipleTestCase "create and delete an Item" instances <| fun (setUp, itemManger) ->
            setUp()
            let item = Item.MkItem ("name", "description")
            let addItem = itemManger.AddItem item
            Expect.isOk addItem "should be ok"
            let tryGetItem = itemManger.GetItem item.Id
            Expect.isOk tryGetItem "should be ok"
            let deleteItem = itemManger.DeleteItem item.Id
            Expect.isOk deleteItem "should be ok"
            let retrieveItem = itemManger.GetItem item.Id
            Expect.isError retrieveItem "should be error"
        
        multipleTestCase "create an item and open a reservation. The counter should be 1" instances <| fun (setUp, itemManger) ->
            setUp()
            let item = Item.MkItem ("name", "description")
            let addItem = itemManger.AddItem item
            Expect.isOk addItem "should be ok"
            let tryGetItem = itemManger.GetItem item.Id
            Expect.isOk tryGetItem "should be ok"
            
            let reservation = Reservation.Reservation.MkReservation [item.Id] |> Result.get
            let addReservation = itemManger.AddReservation reservation
            Expect.isOk addReservation "should be ok"
            
            let retrieveItem = itemManger.GetItem item.Id
            Expect.isOk retrieveItem "should be ok"
            let item = retrieveItem.OkValue
            Expect.equal item.ReferencesCounter 1 "should be 1"
            
        multipleTestCase "create two items and an open reservation for both. Both the counters should be 1" instances <| fun (setUp, itemManger) ->
            setUp ()
            let item1 = Item.MkItem ("name", "description")
            let addItem1 = itemManger.AddItem item1
            Expect.isOk addItem1 "should be ok"
            let tryGetItem1 = itemManger.GetItem item1.Id
            Expect.isOk tryGetItem1 "should be ok"
            
            let item2 = Item.MkItem ("name", "description")
            let addItem2 = itemManger.AddItem item2
            Expect.isOk addItem2 "should be ok"
            let tryGetItem2 = itemManger.GetItem item2.Id
            Expect.isOk tryGetItem2 "should be ok"
            
            let reservation = Reservation.Reservation.MkReservation [item1.Id; item2.Id] |> Result.get
            let addReservation = itemManger.AddReservation reservation
            Expect.isOk addReservation "should be ok"
           
            let retrieveItem1 = itemManger.GetItem item1.Id
            Expect.equal retrieveItem1.OkValue.ReferencesCounter 1 "should be 1"
            
            let retrieveItem2 = itemManger.GetItem item2.Id
            Expect.equal retrieveItem2.OkValue.ReferencesCounter 1 "should be 1"
        
        multipleTestCase "when an item has a reservation to it then it cannot be deleted - Error" instances <| fun (setUp, itemManager) ->
            setUp ()
            let item = Item.MkItem ("name", "description")
            let addItem = itemManager.AddItem item
            Expect.isOk addItem "should be ok"
            
            let reservation = Reservation.Reservation.MkReservation [item.Id] |> Result.get
            let addReservation = itemManager.AddReservation reservation
            let retrievedItem = itemManager.GetItem item.Id |> Result.get
            Expect.equal retrievedItem.ReferencesCounter 1 "should be equal"
            
            let tryDeleteItem = itemManager.DeleteItem item.Id
            Expect.isError tryDeleteItem "should be error"
       
        multipleTestCase "add an item and a reservation to it, then close the item in the reservation and the item can be deleted - Ok" instances <| fun (setUp, itemManager) ->
            setUp ()
            let item = Item.MkItem ("name", "description")
            let addItem = itemManager.AddItem item
            Expect.isOk addItem "should be ok"
            
            let reservation = Reservation.Reservation.MkReservation [item.Id] |> Result.get
            let addReservation = itemManager.AddReservation reservation
            let retrievedItem = itemManager.GetItem item.Id |> Result.get
            Expect.equal retrievedItem.ReferencesCounter 1 "should be equal"
            
            let closeReservation = itemManager.CloseItemInReservation reservation.Id item.Id
            Expect.isOk closeReservation "should be ok"
            
            let retrieveReservation = itemManager.GetReservation reservation.Id
            Expect.isOk retrieveReservation "should be ok"
           
            let itemsInReservation = retrieveReservation.OkValue.Reservations
            Expect.isTrue (itemsInReservation |> List.forall (fun x -> match x with | Closed _ -> true | _ -> false)) "should be true" 
            
            let tryDeleteItem = itemManager.DeleteItem item.Id
            Expect.isOk tryDeleteItem "should be ok"
            
    ]
    |> testSequenced
    
    
    
        
