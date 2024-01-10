
namespace seatsLockWithSharpino 
open FsToolkit.ErrorHandling
open Sharpino.CommandHandler

module App =
    open Sharpino.Storage
    let doNothingBroker: IEventBroker = 
        {
            notify = None
        }
    type App(storage: IEventStore, eventBroker: IEventBroker) =
        let row1StateViewer =
            getStorageFreshStateViewer< Row1Context.Row1, Row1Events.Row1Events > storage
            
        let row2StateViewer =
            getStorageFreshStateViewer<Row2Context.Row2, Row2Events.Row2Events > storage
        new(storage: IEventStore) = App(storage, doNothingBroker)

        member private this.BookSeatsRow1 (bookingRow1: Seats.Booking) =
            result {
                let reserveCommandRow1 = Row1Command.BookSeats bookingRow1
                let! result = runCommand<Row1Context.Row1, Row1Events.Row1Events> storage eventBroker row1StateViewer reserveCommandRow1 
                return result
            }
        member private this.BookSeatsRow2 (bookingRow2: Seats.Booking) =
            result {
                let reserveCommandRow2 = Row2Command.BookSeats bookingRow2
                let! result = runCommand<Row2Context.Row2, Row2Events.Row2Events> storage eventBroker row2StateViewer reserveCommandRow2
                return result
            }

        // todo: this does not handle the multiple rows case in a consistent way. will fix later
        member this.BookSeats (booking: Seats.Booking) =
            let assignToRow1 = booking |> Seats.toRow1
            let assignToRow2 = booking |> Seats.toRow2
            result {
                let result =
                    match assignToRow1.isEmpty(), assignToRow2.isEmpty() with
                    | true, true -> Error "No seats available"
                    | false, true -> this.BookSeatsRow1 assignToRow1
                    | true, false -> this.BookSeatsRow2 assignToRow2
                    | false, false ->
                        runTwoCommands<Row1Context.Row1, Row2Context.Row2, Row1Events.Row1Events, Row2Events.Row2Events> storage eventBroker (Row1Command.BookSeats assignToRow1) (Row2Command.BookSeats assignToRow2) row1StateViewer row2StateViewer
                        
                return! result
            }

        member this.GetAllAvailableSeats () =
            result { 
                let! (_, row1State, _, _) = row1StateViewer()
                let! (_, row2State, _, _) = row2StateViewer()
                let row1FreeSeats = row1State.GetAvailableSeats()
                let row2FreeSeats = row2State.GetAvailableSeats()
                return row1FreeSeats @ row2FreeSeats
            }


