
namespace seatsLockWithSharpino 
open FsToolkit.ErrorHandling
open Sharpino.CommandHandler
open Sharpino

module App =
    open Sharpino.Storage
    let doNothingBroker: IEventBroker<string> = 
        {
            notify = None
            notifyAggregate = None 
        }

    type App(storage: IEventStore<string>, eventBroker: IEventBroker<string>) =
        let row1StateViewer =
            getStorageFreshStateViewer< Row1Context.Row1, Row1Events.Row1Events, string > storage
            
        let row2StateViewer =
            getStorageFreshStateViewer<Row2Context.Row2, Row2Events.Row2Events, string > storage
        new (storage: IEventStore<string>) = App(storage, doNothingBroker)

        member private this.BookSeatsRow1 (bookingRow1: Seats.Booking) =
            result {
                let bookRow1 = Row1Command.BookSeats bookingRow1
                let! result = runCommand<Row1Context.Row1, Row1Events.Row1Events, string> storage eventBroker bookRow1
                return result
            }
        member private this.BookSeatsRow2 (bookingRow2: Seats.Booking) =
            result {
                let bookRow2 = Row2Command.BookSeats bookingRow2
                let! result = runCommand<Row2Context.Row2, Row2Events.Row2Events, string> storage eventBroker bookRow2
                return result
            }

        member this.BookSeats (booking: Seats.Booking) =
            let row1Booking = booking |> Seats.toRow1
            let row2Booking = booking |> Seats.toRow2
            result {
                let result =
                    match row1Booking.isEmpty(), row2Booking.isEmpty() with
                    | true, true -> Error "booking is empty"
                    | false, true -> this.BookSeatsRow1 row1Booking
                    | true, false -> this.BookSeatsRow2 row2Booking
                    | false, false ->
                        runTwoCommands<Row1Context.Row1, Row2Context.Row2, Row1Events.Row1Events, Row2Events.Row2Events, string> 
                            storage eventBroker (Row1Command.BookSeats row1Booking) (Row2Command.BookSeats row2Booking) 
                return! result
            }

        member this.GetAllAvailableSeats () =
            result { 
                let! (_, row1State) = row1StateViewer()
                let! (_, row2State) = row2StateViewer()
                let row1FreeSeats = row1State.GetAvailableSeats()
                let row2FreeSeats = row2State.GetAvailableSeats()
                return row1FreeSeats @ row2FreeSeats
            }

