
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
    type StadiumBookingSystem(storage: IEventStore, eventBroker: IEventBroker) =
        let stadiumStateViewer =
            getStorageFreshStateViewer<StadiumContext, StadiumEvent > storage

        let rowStateViewer =    
            getAggregateStorageFreshStateViewer<SeatsRow, RowAggregateEvent> storage

        new(storage: IEventStore) = StadiumBookingSystem(storage, doNothingBroker)

        member this.AddRow (row: SeatsRow) =
            ResultCE.result {
                let serializedRow = (row :> Aggregate).Serialize serializer
                let! stored =
                    storage.SetInitialAggregateState (row.Id) SeatsRow.Version SeatsRow.StorageName serializedRow
                    
                let addRow = StadiumCommand.AddRow row
                let! result = runCommand<StadiumContext.StadiumContext, StadiumContext.StadiumEvent> storage eventBroker stadiumStateViewer addRow 
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

        member this.BookSeatsTwoRows' (rowId1: Guid, booking1: Booking) (rowid2: Guid, booking2: Booking) =
            result {
                let bookSeats = 
                    [ (BookSeats booking1):> Command<SeatsRow, RowAggregateEvent>
                      (BookSeats booking2):> Command<SeatsRow, RowAggregateEvent> ]
                let! result = 
                    runNAggregateCommands<SeatsRow, RowAggregateEvent> 
                        [rowId1; rowid2] 
                        storage 
                        eventBroker 
                        [
                            (fun () -> rowStateViewer rowId1)
                            (fun () -> rowStateViewer rowid2)
                        ] 
                        bookSeats
                return result
            }

        member this.BookSeatsNRows (rowAndbookings: List<Guid * Booking>) =
            result {
                let bookSeats = 
                    rowAndbookings
                    |> List.map (fun (rowId, booking) -> (BookSeats booking):> Command<SeatsRow, RowAggregateEvent>)
                let! result = 
                    runNAggregateCommands<SeatsRow, RowAggregateEvent> 
                        (rowAndbookings |> List.map fst) 
                        storage 
                        eventBroker 
                        (rowAndbookings |> List.map (fun (rowId, _) -> fun () -> rowStateViewer rowId)) 
                        bookSeats
                return result
            }

        member this.GetRowRefactored id =
            result {
                let! (_, rowState, _, _) = rowStateViewer id
                return rowState
            }
        member this.AddRowRefactored (row: SeatsRow) =
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
                    storage.SetInitialAggregateState (row.Id) SeatsRow.Version SeatsRow.StorageName serializedRow
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

        // member this.GetAllRows() = 
        //     result {
        //         let! (_, stadiumState, _, _) = stadiumStateViewer ()
        //         return stadiumState.GetRows()
        //     }
            
        // member this.GetAllAvailableSeats() =
        //     result {
        //         let! rows = this.GetAllRows()
        //         let availableSeats =
        //             rows
        //             |> List.map (fun row -> row.GetAvailableSeats())
        //             |> List.concat
        //         return availableSeats     
        //     }

        member this.GetAllRowReferences() = 
            result {
                let! (_, stadiumState, _, _) = stadiumStateViewer ()
                return stadiumState.GetRowReferences ()
            }

        member this.AddSeatToRow (rowId: Guid) (seat: Seat) =
            result { 
                let addSeat = RowAggregateCommand.AddSeat seat
                let! result = 
                    runAggregateCommand<SeatsRow, RowAggregateEvent> 
                        rowId storage eventBroker (fun () -> rowStateViewer rowId) addSeat
                return result
            }
