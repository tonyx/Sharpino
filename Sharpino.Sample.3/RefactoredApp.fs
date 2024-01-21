
namespace seatsLockWithSharpino 
open seatsLockWithSharpino.RefactoredRow
open seatsLockWithSharpino.StadiumContext
open Sharpino.CommandHandler
open System
open Farmer
open FSharpPlus.Operators
open FSharpPlus
open FsToolkit.ErrorHandling

module RefactoredApp =
    open Sharpino.Storage
    open Seats
    open Sharpino.Core
    open Sharpino.Utils
    let doNothingBroker: IEventBroker = 
        {
            notify = None
        }
    type RefactoredApp(storage: IEventStore, eventBroker: IEventBroker) =
        let stadiumStateViewer =
            getStorageFreshStateViewer<StadiumContext, StadiumEvent > storage

        let rowStateViewer =    
            getStorageFreshStateViewerRefactored<RefactoredRow, RowAggregateEvent> storage

        new(storage: IEventStore) = RefactoredApp(storage, doNothingBroker)

        member this.AddRow (row: RefactoredRow) =
            ResultCE.result {
                let serializedRow = (row :> Aggregate).Serialize serializer
                let! stored =
                    storage.SetInitialAggregateState (row.Id) RefactoredRow.Version RefactoredRow.StorageName serializedRow
                    
                let addRow = StadiumCommand.AddRow row
                let! result = runCommand<StadiumContext.StadiumContext, StadiumContext.StadiumEvent> storage eventBroker stadiumStateViewer addRow 
                return result
            }

        member this.BookSeats (rowId: Guid) (booking: Booking) =
            result {
                let bookSeat = RowAggregateCommand.BookSeats booking
                let! result = 
                    runCommandRefactored<RefactoredRow, RowAggregateEvent> 
                        rowId storage eventBroker (fun () -> rowStateViewer rowId) bookSeat
                return result
            }

        member this.BookSeatsTwoRows (rowId1: Guid, booking1: Booking) (rowid2: Guid, booking2: Booking) =
            result {
                let bookSeat1 = RowAggregateCommand.BookSeats booking1
                let bookSeat2 = RowAggregateCommand.BookSeats booking2
                let! result = 
                    runTwoCommandsRefactored<RefactoredRow, RefactoredRow, RowAggregateEvent, RowAggregateEvent> 
                        rowId1 rowid2 storage eventBroker (fun () -> rowStateViewer rowId1) (fun () -> rowStateViewer rowid2) bookSeat1 bookSeat2
                return result
            }

        member this.GetRowRefactored id =
            result {
                let! (_, rowState, _, _) = rowStateViewer id
                return rowState
            }
        member this.AddRowRefactored (row: RefactoredRow) =
            let alreadyExists = 
                rowStateViewer row.Id
            result {
                let! notAlreadyExists = 
                    rowStateViewer row.Id
                    |> Result.toOption
                    |> Option.isSome
                    |> not
                    |> boolToResult (sprintf "A row with id '%A' already exists" row.Id)

                let serializedRow = row.Serialize serializer
                // todo: shold use an undo mechanism here (unset the snapshot if the referenceadded fails)
                let! rowAdd =
                    storage.SetInitialAggregateState (row.Id) RefactoredRow.Version RefactoredRow.StorageName serializedRow
                let! referenceAdded = 
                    runCommand<StadiumContext.StadiumContext, StadiumContext.StadiumEvent> storage eventBroker stadiumStateViewer (StadiumCommand.AddRowReference row.Id)
                return referenceAdded
            }

        member this.GetRow (id: Guid) =
            result {
                let! (_, stadiumState, _, _) = stadiumStateViewer()
                let! row = stadiumState.GetRow id
                return row
            }

        member this.AddSeat (rowId: Guid) (seat: Seat) =
            result {
                let addSeat = RowAggregateCommand.AddSeat seat
                let! result = 
                    runCommandRefactored<RefactoredRow, RowAggregateEvent> 
                        rowId storage eventBroker (fun () -> rowStateViewer rowId) addSeat
                return result
            }

        member this.AddSeats (rowId: Guid) (seats: List<Seat>) =
            result {
                let addSeats = RowAggregateCommand.AddSeats seats
                let! result = 
                    runCommandRefactored<RefactoredRow, RowAggregateEvent> 
                        rowId storage eventBroker (fun () -> rowStateViewer rowId) addSeats
                return result
            }

        member this.GetAllRows() = 
            result {
                let! (_, stadiumState, _, _) = stadiumStateViewer ()
                return stadiumState.GetRows()
            }

        member this.GetAllRowReferences() = 
            result {
                let! (_, stadiumState, _, _) = stadiumStateViewer ()
                return stadiumState.GetRowReferences ()
            }

        member this.AddSeatToRow (rowId: Guid) (seat: Seat) =
            result { 
                let addSeat = RowAggregateCommand.AddSeat seat
                let! result = 
                    runCommandRefactored<RefactoredRow, RowAggregateEvent> 
                        rowId storage eventBroker (fun () -> rowStateViewer rowId) addSeat
                return result
            }
