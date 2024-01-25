
namespace seatsLockWithSharpino 
open seatsLockWithSharpino.RefactoredRow
open seatsLockWithSharpino.Stadium
open seatsLockWithSharpino.StadiumEvents
open seatsLockWithSharpino.StadiumCommands
open Sharpino.CommandHandler
open System
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
            getStorageFreshStateViewer<Stadium, StadiumEvent > storage

        let rowStateViewer =    
            getAggregateStorageFreshStateViewer<SeatsRow, RowAggregateEvent> storage

        new(storage: IEventStore) = StadiumBookingSystem(storage, doNothingBroker)
        
        member this.AddRowReference (rowId: Guid)  =
            ResultCE.result {
                // todo: undo mechanism or similar to remove the initialsnapshot if the addrow fails
                // or accept there will be potentially rows with no reference (not hurting anything)
                let seatsRow = SeatsRow rowId
                let initSnapshot = seatsRow.Serialize serializer
                let! stored =
                    storage.SetInitialAggregateState rowId SeatsRow.Version SeatsRow.StorageName initSnapshot
                let addRowReference = StadiumCommand.AddRowReference rowId
                let! result = runCommand<Stadium, StadiumEvent> storage eventBroker stadiumStateViewer addRowReference
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
                let! (_, stadiumState, _, _) = stadiumStateViewer ()
                return stadiumState.GetRowReferences ()
            }
