
namespace Tonyx.SeatsBooking
open Tonyx.SeatsBooking.IStadiumBookingSystem
open Tonyx.SeatsBooking.Seats
open Tonyx.SeatsBooking.SeatRow
open Tonyx.SeatsBooking.Stadium
open Tonyx.SeatsBooking.StadiumEvents
open Tonyx.SeatsBooking.StadiumCommands
open Tonyx.SeatsBooking.RowAggregateEvent
open Tonyx.SeatsBooking.RowAggregateCommand
open Tonyx.SeatsBooking
open Sharpino.CommandHandler
open Sharpino.Definitions
open System
open System.Collections
open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Sharpino.Storage
open Sharpino.ApplicationInstance
open Sharpino.Core
open Sharpino.Utils

module StadiumKafkaBookingSystem =
    open Sharpino.KafkaReceiver
    let doNothingBroker: IEventBroker = 
        {
            notify = None
            notifyAggregate = None 
        }
    type StadiumKafkaBookingSystem
        (storage: IEventStore, eventBroker: IEventBroker) =
        let storageStadiumViewer = getStorageFreshStateViewer<Stadium, StadiumEvent > storage
        let stadiumSubscriber = KafkaSubscriber.Create("localhost:9092", "_01", "_stadium", "sharpinoTestClient") |> Result.get
        let rowSubscriber = KafkaSubscriber.Create("localhost:9092", "_01", "_seatrow", "sharpinoTestClient") |> Result.get
        let kafkaStadiumViewer = mkKafkaViewer<Stadium, StadiumEvent> stadiumSubscriber storageStadiumViewer  (ApplicationInstance.Instance.GetGuid())
        let kafkaBasedStadiumState: StateViewer<Stadium> =
            fun () ->
                kafkaStadiumViewer.RefreshLoop()
                kafkaStadiumViewer.State() 

        let kafkaRowViewer = 
            fun (rowId: Guid) ->
                mkKafkaAggregateViewer<SeatsRow, RowAggregateEvent> rowId rowSubscriber (getAggregateStorageFreshStateViewer<SeatsRow, RowAggregateEvent> storage) (ApplicationInstance.Instance.GetGuid()) 

        let kafkaRowViewer' rowSubscriber' = 
            fun (rowId: Guid) ->
                mkKafkaAggregateViewer<SeatsRow, RowAggregateEvent> rowId rowSubscriber' (getAggregateStorageFreshStateViewer<SeatsRow, RowAggregateEvent> storage) (ApplicationInstance.Instance.GetGuid()) 
        
        // todo: adjust to avoid recreating the viewer each time. See carefully the subscriber alignment issues (code about 'Assign'...)
        let rowStateViewer: AggregateViewer<SeatsRow> =
            // let rowSubscriber = KafkaSubscriber.Create("localhost:9092", "_01", "_seatrow", "sharpinoTestClient") |> Result.get
            fun (rowId: Guid) ->
                let kafkarViewer = kafkaRowViewer' rowSubscriber
                let viewer = kafkarViewer rowId
                viewer.RefreshLoop()
                viewer.State()
                
        // this is spike code:        
        // let rowStateViewer: AggregateViewer<SeatsRow> =
        //     let viewers = Generic.Dictionary<Guid, KafkaAggregateViewer<SeatsRow, RowAggregateEvent>>()
        //     fun (rowId: Guid) ->
        //         let viewer =
        //             if false then
        //             // if viewers.ContainsKey(rowId) then
        //                 viewers.[rowId]
        //             else
        //                 let rowSubscriber = KafkaSubscriber.Create("localhost:9092", "_01", "_seatrow", "sharpinoTestClient") |> Result.get
        //                 let kafkarViewer = kafkaRowViewer' rowSubscriber
        //                 let viewer = kafkarViewer rowId
        //                 // viewers.Add(rowId, viewer)
        //                 viewer
        //         // let kafkarViewer = kafkaRowViewer' rowSubscriber
        //         // let viewer = kafkarViewer rowId
        //         viewer.RefreshLoop()
        //         viewer.State()

        
        member this.AddInvariant (rowId: Guid) (invariant: InvariantContainer) =
            let addInvariant = RowAggregateCommand.AddInvariant invariant
            result {
                let! result =
                    runAggregateCommand<SeatsRow, RowAggregateEvent> 
                        rowId storage eventBroker (fun () -> rowStateViewer rowId) addInvariant
                return result         
            }
        member this.SetAggregateStateControlInOptimisticLock Version Name =
            ResultCE.result {
                return! storage.SetClassicOptimisticLock Version Name
            }
        member this.UnSetAggregateStateControlInOptimisticLock Version Name =
            ResultCE.result {
                return! storage.UnSetClassicOptimisticLock Version Name
            }
        member this.AddRowReference (rowId: Guid)  =
            ResultCE.result {
                // todo: undo mechanism or similar to remove the initialsnapshot if the addrow fails
                // or accept there will be potentially rows with no reference (not hurting anything)
                let seatsRow = SeatsRow rowId
                let initSnapshot = seatsRow.Serialize serializer
                let! stored =
                    storage.SetInitialAggregateState rowId seatsRow.StateId SeatsRow.Version SeatsRow.StorageName initSnapshot
                let addRowReference = StadiumCommand.AddRowReference rowId

                let! result = runCommand<Stadium, StadiumEvent> storage eventBroker kafkaBasedStadiumState addRowReference
                return result
            }
        member this.BookSeats (rowId: Guid) (booking: Booking) =
            result {
                let bookSeat = RowAggregateCommand.BookSeats booking
                let! result = 
                    runAggregateCommand<SeatsRow, RowAggregateEvent> 
                        rowId storage eventBroker (fun () -> rowStateViewer rowId) bookSeat
                return result
            }
        member this.BookSeatsNRows (rowAndbookings: List<Guid * Booking>) =
            result {
                let bookSeatsCommands = 
                    rowAndbookings
                    |> List.map (fun (_, booking) -> (BookSeats booking):> Command<SeatsRow, RowAggregateEvent>)
                let rowIDs = rowAndbookings |>> fst    
                let! mustBeThreeRowsOrLess =
                    rowAndbookings.Length < 3
                    |> boolToResult "Can only book up to 2 rows at a time"
                let! result = 
                    runNAggregateCommands<SeatsRow, RowAggregateEvent>
                        rowIDs
                        storage 
                        eventBroker 
                        (rowIDs |>> (fun rowId -> fun () -> rowStateViewer rowId))
                        bookSeatsCommands
                return result
            }
        member this.GetRow id =
            result {
                let! (_, rowState, _, _) = rowStateViewer id
                return rowState
            }
        member this.AddSeat (rowId: Guid) (seat: Seat) =
            result {
                let! rowIdMustBeUnassigned =
                    seat.RowId.IsNone |> boolToResult "Seat already assigned to a row"
                let addSeat =
                    RowAggregateCommand.AddSeat {seat with RowId = rowId |> Some}
                let! result = 
                    runAggregateCommand<SeatsRow, RowAggregateEvent> 
                        rowId storage eventBroker (fun () -> rowStateViewer rowId) addSeat
                return result
            }
        member this.AddSeats (rowId: Guid) (seats: List<Seat>) =
            result {
                let addSeats = RowAggregateCommand.AddSeats seats
                let! result = 
                    runAggregateCommand<SeatsRow, RowAggregateEvent> 
                        rowId storage eventBroker (fun () -> rowStateViewer rowId) addSeats
                return result
            }
        member this.GetAllRowReferences() = 
            result {
                let! (_, stadiumState, _, _) = kafkaBasedStadiumState ()
                return stadiumState.GetRowReferences ()
            }
        interface IStadiumBookingSystem with
            member this.SetAggregateStateControlInOptimisticLock version name = 
                this.SetAggregateStateControlInOptimisticLock version name
            member this.UnSetAggregateStateControlInOptimisticLock version name = 
                this.UnSetAggregateStateControlInOptimisticLock version name     
            member this.AddRowReference rowId = this.AddRowReference rowId
            // member this.BookSeats = this.BookSeats
            member this.BookSeats (rowId: Guid) (booking: Booking) = this.BookSeats rowId booking
            member this.BookSeatsNRows (rowAndbookings: List<Guid * Booking>) = this.BookSeatsNRows (rowAndbookings: List<Guid * Booking>)
            member this.GetRow id = this.GetRow id
            member this.AddSeat (rowId: Guid) (seat: Seat) = this.AddSeat (rowId: Guid) (seat: Seat) 
            member this.AddSeats (rowId: Guid) (seats: List<Seat>) =  this.AddSeats (rowId: Guid) (seats: List<Seat>) 
            member this.GetAllRowReferences() =  this.GetAllRowReferences()
            member this.AddInvariant (rowId: Guid) (invariant: InvariantContainer) =
                this.AddInvariant (rowId: Guid) (invariant: InvariantContainer)

