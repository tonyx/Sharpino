
namespace seatsLockWithSharpino 
open seatsLockWithSharpino.RowAggregate
open seatsLockWithSharpino.StadiumContext
open FsToolkit.ErrorHandling
open Sharpino.CommandHandler
open System

module RefactoredApp =
    open Sharpino.Storage
    open Seats
    let doNothingBroker: IEventBroker = 
        {
            notify = None
        }
    type RefactoredApp(storage: IEventStore, eventBroker: IEventBroker) =
        let stadiumStateViewer =
            getStorageFreshStateViewer<StadiumContext, StadiumEvent > storage
        new(storage: IEventStore) = RefactoredApp(storage, doNothingBroker)

        member this.AddRow (row: RefactoredRow) =
            result {
                let addRow = StadiumCommand.AddRow row
                let! result = runCommand<StadiumContext.StadiumContext, StadiumContext.StadiumEvent> storage eventBroker stadiumStateViewer addRow 
                return result
            }

        member this.GetRow (id: Guid) =
            result {
                let! (_, stadiumState, _, _) = stadiumStateViewer()
                let! row = stadiumState.GetRow id
                return row
            }

        member this.GetAllRows() = 
            result {
                let! (_, stadiumState, _, _) = stadiumStateViewer()
                return stadiumState.GetRows()
            }

