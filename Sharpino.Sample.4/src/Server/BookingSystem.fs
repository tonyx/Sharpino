namespace Tonyx.SeatsBooking
open Shared.Entities
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

    type StadiumBookingSystem
        (storage: IEventStore, eventBroker: IEventBroker, stadiumStateViewer: StateViewer<Stadium>, rowStateViewer: AggregateViewer<SeatsRow>) =

        new(storage: IEventStore) =
            StadiumBookingSystem(storage, doNothingBroker, getStorageFreshStateViewer<Stadium, StadiumEvent > storage, getAggregateStorageFreshStateViewer<SeatsRow, RowAggregateEvent> storage)
        new(storage: IEventStore, eventBroker: IEventBroker) =
            StadiumBookingSystem(storage, eventBroker, getStorageFreshStateViewer<Stadium, StadiumEvent > storage, getAggregateStorageFreshStateViewer<SeatsRow, RowAggregateEvent> storage)
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
                // let seatsRow = SeatsRow (rowId, doNothingBroker)
                let seatsRow = SeatsRow rowId
                let initSnapshot = seatsRow.Serialize serializer
                let! stored =
                    storage.SetInitialAggregateState rowId seatsRow.StateId SeatsRow.Version SeatsRow.StorageName initSnapshot
                let addRowReference = StadiumCommand.AddRowReference rowId
                let! result = runCommand<Stadium, StadiumEvent> storage eventBroker stadiumStateViewer addRowReference
                return result
            }
        member this.AddRowReference () =
            this.AddRowReference (Guid.NewGuid())

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
                    |>> fun (_, booking) -> (BookSeats booking):> Command<SeatsRow, RowAggregateEvent>
                let rowIDs = rowAndbookings |>> fst
                // todo: should consider the total number of seats
                let! mustBeLessThanThreeRows =
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

        member this.AddSeatsToRows (rowAndSeats: List<Guid * List<Seat>>) =
            result {
                let addSeatsCommands =
                    rowAndSeats
                    |>> fun (_, seats) -> (AddSeats seats):> Command<SeatsRow, RowAggregateEvent>
                let rowIDs = rowAndSeats |>> fst
                let! result =
                    runNAggregateCommands<SeatsRow, RowAggregateEvent>
                        rowIDs
                        storage
                        eventBroker
                        (rowIDs |>> (fun rowId -> fun () -> rowStateViewer rowId))
                        addSeatsCommands
                return result
            }

        member this.GetAllRowsSeatsTo () =
            printf "getAllRowsSeatsTo\n"
            let res =
                result {
                    let! (_, stadiumState, _, _) = stadiumStateViewer ()
                    let rowReferences = stadiumState.GetRowReferences ()
                    let! rowsTos =
                        rowReferences
                        |> List.traverseResultM (fun rowId -> this.GetSeatsRowTO rowId)
                    printf "getAllRowsSeatsTo done\n"
                    return rowsTos
                }
            printf "getAllRowsSeatsTo: getting result %A\n" res
            res

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
                        rowId storage eventBroker (fun () -> rowStateViewer rowId) addInvariant
                return result
            }
