namespace Tonyx.SeatsBooking
open System.Threading.Tasks
open Sharpino.Commons
open Sharpino.EventBroker
open Sharpino.PgStorage
open Tonyx.SeatsBooking.SeatRow
open Tonyx.SeatsBooking.Stadium
open Tonyx.SeatsBooking.StadiumEvents
open Tonyx.SeatsBooking.StadiumCommands
open Tonyx.SeatsBooking.RowAggregateEvent
open Tonyx.SeatsBooking.RowAggregateCommand

open Tonyx.SeatsBooking
open Entities
open Sharpino
open Sharpino.CommandHandler
open Sharpino.Definitions
open System
open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Sharpino.Storage
open Sharpino.Core
open Sharpino.Utils

module StorageStadiumBookingSystem =
    
    let emptyMessageSender =
        fun queueName ->
            fun message ->
                ValueTask.CompletedTask

    type StadiumBookingSystem
        (eventStore: IEventStore<string>, messageSenders: MessageSenders, stadiumStateViewer: StateViewer<Stadium>, rowStateViewer: AggregateViewer<SeatsRow>) =

        new (eventStore: IEventStore<string>) =
            StadiumBookingSystem(eventStore, MessageSenders.NoSender, getStorageFreshStateViewer<Stadium, StadiumEvent, string > eventStore, getAggregateStorageFreshStateViewer<SeatsRow, RowAggregateEvent, string> eventStore)
        new (eventStore: IEventStore<string>, messageSenders: MessageSenders) =
            StadiumBookingSystem(eventStore, messageSenders, getStorageFreshStateViewer<Stadium, StadiumEvent, string > eventStore, getAggregateStorageFreshStateViewer<SeatsRow, RowAggregateEvent, string> eventStore)
        
        member this.AddRow (row: SeatsRow) =
             result {
                let addRowReference = StadiumCommand.AddRowReference row.Id
                let! result = runInitAndCommand<Stadium, StadiumEvent, SeatsRow, string> eventStore messageSenders row addRowReference
                return result
            }

        member this.BookSeats (rowId: Guid) (booking: Booking) =
            result {
                let bookSeat = RowAggregateCommand.BookSeats booking
                let! result =
                    runAggregateCommand<SeatsRow, RowAggregateEvent, string>
                        rowId eventStore messageSenders bookSeat
                return result
            }

        member this.BookSeatsNRows (rowsAndBookings: List<Guid * Booking>) =
            result {
                let bookSeatsCommands =
                    rowsAndBookings
                    |>> fun (_, booking) -> (BookSeats booking)  :> AggregateCommand<SeatsRow, RowAggregateEvent>
                let rowIDs = rowsAndBookings |>> fst
                // todo: should consider the total number of seats
                let! mustBeLessThanThreeRows =
                    rowsAndBookings.Length < 3
                    |> Result.ofBool "Can only book up to 2 rows at a time"
                let! result =
                    runNAggregateCommands<SeatsRow, RowAggregateEvent, string>
                        rowIDs
                        eventStore
                        messageSenders
                        bookSeatsCommands
                return result
            }

        member this.GetRow id =
            result {
                let! (_, rowState) = rowStateViewer id
                return rowState
            }
        member this.GetSeatsRowTO (id: Guid) =
            result {
                let! (_, rowState) = rowStateViewer id
                return rowState.ToSeatsRowTO ()
            }

        member this.AddSeat (rowId: Guid) (seat: Seat) =
            result {
                let! rowIdMustBeUnassigned =
                    seat.RowId.IsNone |> boolToResult "Seat already assigned to a row"
                let addSeat =
                    RowAggregateCommand.AddSeat {seat with RowId = rowId |> Some}
                let! result =
                    runAggregateCommand<SeatsRow, RowAggregateEvent, string>
                        rowId eventStore messageSenders addSeat
                return result
            }

        member this.RemoveSeat  (seat: Seat) =
            result {
                let! rowId =
                    seat.RowId |> Result.ofOption "Seat not assigned to a row"
                let removeSeat =
                    RowAggregateCommand.RemoveSeat seat
                let! result =
                    runAggregateCommand<SeatsRow, RowAggregateEvent, string>
                        rowId eventStore messageSenders removeSeat
                return result
            }

        member this.AddSeats (rowId: Guid) (seats: List<Seat>) =
            result {
                let addSeats = RowAggregateCommand.AddSeats seats
                let! result =
                    runAggregateCommand<SeatsRow, RowAggregateEvent, string>
                        rowId eventStore messageSenders addSeats
                return result
            }

        member this.AddSeatsToRows (rowAndSeats: List<Guid * List<Seat>>) =
            result {
                let addSeatsCommands =
                    rowAndSeats
                    |>> fun (_, seats) -> (AddSeats seats):> AggregateCommand<SeatsRow, RowAggregateEvent>
                let rowIDs = rowAndSeats |>> fst
                let! result =
                    runNAggregateCommands<SeatsRow, RowAggregateEvent, string>
                        rowIDs
                        eventStore
                        messageSenders
                        addSeatsCommands
                return result
            }

        member this.GetAllRowsSeatsTo () =
            result {
                let! (_, stadiumState) = stadiumStateViewer ()
                let rowReferences = stadiumState.GetRowReferences ()
                let! rowsTos =
                    rowReferences
                    |> List.traverseResultM (fun  rowId -> this.GetSeatsRowTO rowId)
                return rowsTos
            }

        member this.GetAllRowReferences () =
            result {
                let! (_, stadiumState) = stadiumStateViewer ()
                return stadiumState.GetRowReferences ()
            }
        

        member this.AddInvariant (rowId: Guid) (invariant: InvariantContainer) =
            let addInvariant = RowAggregateCommand.AddInvariant invariant
            result {
                let! result =
                    runAggregateCommand<SeatsRow, RowAggregateEvent, string>
                        rowId eventStore messageSenders addInvariant
                return result
            }

        member this.RemoveInvariant (rowId: Guid) (invariant: InvariantContainer) =
            let removeInvariant = RowAggregateCommand.RemoveInvariant invariant
            result {
                let! result =
                    runAggregateCommand<SeatsRow, RowAggregateEvent, string>
                        rowId eventStore messageSenders removeInvariant
                return result
            }
