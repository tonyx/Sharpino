namespace Tonyx.SeatsBooking
open Shared.Entities
open Sharpino.PgStorage
open Tonyx.SeatsBooking.SeatRow
open Tonyx.SeatsBooking.Stadium
open Tonyx.SeatsBooking.StadiumEvents
open Tonyx.SeatsBooking.StadiumCommands
open Tonyx.SeatsBooking.RowAggregateEvent
open Tonyx.SeatsBooking.RowAggregateCommand
open Tonyx.SeatsBooking
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
    let doNothingBroker: IEventBroker =
        {
            notify = None
            notifyAggregate = None
        }
    let connection =
        "Server=127.0.0.1;"+
        "Database=es_seat_booking;" +
        "User Id=safe;"+
        "Password=safe;"

    type StadiumBookingSystem
        (eventStore: IEventStore, eventBroker: IEventBroker, stadiumStateViewer: StateViewer<Stadium>, rowStateViewer: AggregateViewer<SeatsRow>) =

        new (eventStore: IEventStore) =
            StadiumBookingSystem(eventStore, doNothingBroker, getStorageFreshStateViewer<Stadium, StadiumEvent > eventStore, getAggregateStorageFreshStateViewer<SeatsRow, RowAggregateEvent> eventStore)
        new (eventStore: IEventStore, eventBroker: IEventBroker) =
            StadiumBookingSystem(eventStore, eventBroker, getStorageFreshStateViewer<Stadium, StadiumEvent > eventStore, getAggregateStorageFreshStateViewer<SeatsRow, RowAggregateEvent> eventStore)
        member this.SetAggregateStateControlInOptimisticLock version name =
            ResultCE.result {
                return! eventStore.SetClassicOptimisticLock version name
            }
        member this.UnSetAggregateStateControlInOptimisticLock version name =
            ResultCE.result {
                return! eventStore.UnSetClassicOptimisticLock version name
            }
        member this.AddRowReference (rowId: Guid)  =
            ResultCE.result {
                let seatsRow = SeatsRow rowId
                let addRowReference = StadiumCommand.AddRowReference rowId
                let! result = runInitAndCommand<Stadium, StadiumEvent, SeatsRow> eventStore eventBroker stadiumStateViewer seatsRow addRowReference
                return result
            }
        member this.AddRowReference () =
            this.AddRowReference (Guid.NewGuid())

        member this.BookSeats (rowId: Guid) (booking: Booking) =
            result {
                let bookSeat = RowAggregateCommand.BookSeats booking
                let! result =
                    runAggregateCommand<SeatsRow, RowAggregateEvent>
                        rowId eventStore eventBroker rowStateViewer bookSeat
                return result
            }

        member this.BookSeatsNRows (rowsAndBookings: List<Guid * Booking>) =
            result {
                let bookSeatsCommands =
                    rowsAndBookings
                    |>> fun (_, booking) -> (BookSeats booking)  :> Command<SeatsRow, RowAggregateEvent>
                let rowIDs = rowsAndBookings |>> fst
                // todo: should consider the total number of seats
                let! mustBeLessThanThreeRows =
                    rowsAndBookings.Length < 3
                    |> Result.ofBool "Can only book up to 2 rows at a time"
                let! result =
                    runNAggregateCommands<SeatsRow, RowAggregateEvent>
                        rowIDs
                        eventStore
                        eventBroker
                        rowStateViewer
                        bookSeatsCommands
                return result
            }

        member this.GetRow id =
            result {
                let! (_, rowState, _, _) = rowStateViewer id
                return rowState
            }
        member this.GetSeatsRowTO (id: Guid) =
            result {
                let! (_, rowState, _, _) = rowStateViewer id
                return rowState.ToSeatsRowTO ()
            }

        member this.AddSeat (rowId: Guid) (seat: Seat) =
            result {
                let! rowIdMustBeUnassigned =
                    seat.RowId.IsNone |> boolToResult "Seat already assigned to a row"
                let addSeat =
                    RowAggregateCommand.AddSeat {seat with RowId = rowId |> Some}
                let! result =
                    runAggregateCommand<SeatsRow, RowAggregateEvent>
                        rowId eventStore eventBroker rowStateViewer addSeat
                return result
            }

        member this.RemoveSeat  (seat: Seat) =
            result {
                let! rowId =
                    seat.RowId |> Result.ofOption "Seat not assigned to a row"
                let removeSeat =
                    RowAggregateCommand.RemoveSeat seat
                let! result =
                    runAggregateCommand<SeatsRow, RowAggregateEvent>
                        rowId eventStore eventBroker rowStateViewer removeSeat
                return result
            }

        member this.AddSeats (rowId: Guid) (seats: List<Seat>) =
            result {
                let addSeats = RowAggregateCommand.AddSeats seats
                let! result =
                    runAggregateCommand<SeatsRow, RowAggregateEvent>
                        rowId eventStore eventBroker rowStateViewer addSeats
                return result
            }

        member this.AddSeatsToRows (rowAndSeats: List<Guid * List<Seat>>) =
            result {
                let addSeatsCommands =
                    rowAndSeats
                    |>> fun (_, seats) -> (AddSeats seats):> Command<SeatsRow, RowAggregateEvent>
                let rowIDs = rowAndSeats |>> fst
                let! result =
                    runNAggregateCommands<SeatsRow, RowAggregateEvent>
                        rowIDs
                        eventStore
                        eventBroker
                        rowStateViewer
                        addSeatsCommands
                return result
            }

        member this.GetAllRowsSeatsTo () =
            result {
                let! (_, stadiumState, _, _) = stadiumStateViewer ()
                let rowReferences = stadiumState.GetRowReferences ()
                let! rowsTos =
                    rowReferences
                    |> List.traverseResultM (fun  rowId -> this.GetSeatsRowTO rowId)
                return rowsTos
            }

        member this.GetAllRowReferences () =
            result {
                let! (_, stadiumState, _, _) = stadiumStateViewer ()
                return stadiumState.GetRowReferences ()
            }

        member this.AddInvariant (rowId: Guid) (invariant: InvariantContainer) =
            let addInvariant = RowAggregateCommand.AddInvariant invariant
            result {
                let! result =
                    runAggregateCommand<SeatsRow, RowAggregateEvent>
                        rowId eventStore eventBroker rowStateViewer addInvariant
                return result
            }

        member this.RemoveInvariant (rowId: Guid) (invariant: InvariantContainer) =
            let removeInvariant = RowAggregateCommand.RemoveInvariant invariant
            result {
                let! result =
                    runAggregateCommand<SeatsRow, RowAggregateEvent>
                        rowId eventStore eventBroker rowStateViewer removeInvariant
                return result
            }
