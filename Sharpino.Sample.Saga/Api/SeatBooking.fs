namespace Sharpino.Sample.Saga.Api
open System
open Sharpino
open Sharpino.CommandHandler

open Sharpino.Core
open FsToolkit.ErrorHandling

open Sharpino.Sample.Saga.Context.SeatBookings
open Sharpino.Sample.Saga.Context.Events
open Sharpino.Sample.Saga.Context.Commands
open Sharpino.Sample.Saga.Domain.Seat.Row
open Sharpino.Sample.Saga.Domain.Seat.Commands
open Sharpino.Sample.Saga.Domain.Seat.Events
open Sharpino.Sample.Saga.Domain.Booking.Booking
open Sharpino.Sample.Saga.Domain.Booking.Commands
open Sharpino.Sample.Saga.Domain.Booking.Events
open Sharpino.Sample.Saga.Domain.Vaucher.Voucher
open Sharpino.Sample.Saga.Domain.Vaucher.Events
open Sharpino.Sample.Saga.Domain.Vaucher.Commands
open Sharpino.Storage
module SeatBooking =

    // will implement stuff like: add many reservations at once
    let doNothingBroker: IEventBroker<_> =
        {
            notify = None
            notifyAggregate = None
        }
        
    type SeatBookingService
        (eventStore: IEventStore<string>,
         eventBroker: IEventBroker<string>,
         theaterViewer: StateViewer<Theater>,
         seatsViewer: AggregateViewer<Row>,
         bookingsViewer: AggregateViewer<Booking>)
         // vouchersViewer: AggregateViewer<Voucher>) 
         =
        
        member this.GetRow id =
            result {
                let! (_, row) = seatsViewer id
                return row
            }
            
        member this.GetRows() =
            result {
                let! (_, theater) = theaterViewer ()
                let rowReferences = theater.Rows
                let! rows =
                    rowReferences
                    |> List.traverseResultM (seatsViewer >> Result.map snd)
                return rows    
            }
        
        member this.AddRow (row: Row) =
            result {
                let! (_, theater) = theaterViewer ()
                let addRowReferenceCommand = AddRowReference (row.Id)
                let! result =
                    runInitAndCommand<Theater, TheaterEvents, Row, string> eventStore eventBroker row addRowReferenceCommand
                return result    
            }
            
        member this.AddSeatsToRow (rowId, n) =
            result {
                let! (_, row) = seatsViewer rowId
                let addSeatsCommand = RowCommands.AddSeats n
                let! result =
                    runAggregateCommand<Row, RowEvents, string> rowId eventStore eventBroker addSeatsCommand
                return result    
            }
       
        member this.RemoveSeatsFromRow (rowId, n) =
            result {
                let! (_, row) = seatsViewer rowId
                let removeSeatsCommand = RowCommands.RemoveSeats n
                let! result =
                    runAggregateCommand<Row, RowEvents, string> rowId eventStore eventBroker removeSeatsCommand
                return result    
            }
            
        member this.RemoveSeatsFromRow (rowId, ns: List<int>) =
            if (ns.Length = 0) then
                Ok ()
            else
                result {
                    let! (_, row) = seatsViewer rowId
                    let removeSeatsCommands: List<AggregateCommand<Row, RowEvents>>
                        = ns |> List.map (fun n -> RowCommands.RemoveSeats n)
                    let rowIds =
                        [ for i in 1 .. ns.Length -> rowId ]
                        
                    let! result =
                        runSagaNAggregateCommands<Row, RowEvents, string> rowIds eventStore eventBroker removeSeatsCommands
                    return result    
                }
        member this.RemoveSeatsFromRowPreValidation (rowId, ns: List<int>) =
            if (ns.Length = 0) then
                Ok ()
            else
                result {
                    let! (_, row) = seatsViewer rowId
                    
                    let removeSeatsCommands: List<AggregateCommand<Row, RowEvents>>
                        = ns |> List.map (fun n -> RowCommands.RemoveSeats n)
                    let rowIds =
                        [ for i in 1 .. ns.Length -> rowId ]
                    let! result =
                        forceRunNAggregateCommands<Row, RowEvents, string> rowIds eventStore eventBroker removeSeatsCommands
                    return result    
                }
                
        member this.GetBooking id =
            result {
                let! (_, booking) = bookingsViewer id
                return booking
            }
       
        member this.AddBooking (booking: Booking) =
            result {
                let addBookingReferenceCommand = AddBookingReference (booking.Id)
                let! result =
                    runInitAndCommand<Theater, TheaterEvents, Booking, string> eventStore eventBroker booking addBookingReferenceCommand
                return result    
            }
        member this.GetBookings() =
            result {
                let! (_, theater) = theaterViewer ()
                let bookingReferences = theater.Bookings
                let! bookings =
                    bookingReferences
                    |> List.traverseResultM (bookingsViewer >> Result.map snd)
                return bookings    
            }
        member this.AssignBooking bookingId rowId =
            result {
                let! (_, booking) = bookingsViewer bookingId
                let assignRowToBookingCommand = BookingCommands.Assign rowId
                let assignBookingToRowCommand = RowCommands.Book (bookingId, booking.ClaimedSeats)
                let! result =
                    runTwoAggregateCommands<Booking, BookingEvents, Row, RowEvents, string> bookingId rowId eventStore eventBroker assignRowToBookingCommand assignBookingToRowCommand
                return result    
            }
        
        member this.AssignBookings (bookingAndRows: List<Guid * Guid>) =
            result {
                let rowIds = bookingAndRows |> List.map snd
                let bookingIds = bookingAndRows |> List.map fst
                
                let! bookings =
                    bookingIds
                    |> List.traverseResultM (bookingsViewer >> Result.map snd)
                let! rows =
                    rowIds
                    |> List.traverseResultM (seatsViewer >> Result.map snd)
               
                let assignBookingsToRowsCommands: List<AggregateCommand<Row, RowEvents>> =
                    List.zip bookingIds bookings
                    |> List.map (fun (bookingId, booking) -> RowCommands.Book (bookingId, booking.ClaimedSeats)) 
                
                let assignRowsToBookingsCommands: List<AggregateCommand<Booking, BookingEvents>> =
                    List.zip rowIds rows
                    |> List.map (fun (rowId, row) -> BookingCommands.Assign rowId)    
              
                return!    
                    runTwoNAggregateCommands<Booking, BookingEvents, Row, RowEvents, string> bookingIds rowIds eventStore eventBroker assignRowsToBookingsCommands assignBookingsToRowsCommands
            }
        
        // we don't need prevalidation anymore: multiple commands toward the same aggregate id is fine (no saga-ish style involved)
        member this.ForceAssignBookings  (bookingAndRows: List<Guid * Guid>) =
            result {
                let rowIds = bookingAndRows |> List.map snd
                let bookingIds = bookingAndRows |> List.map fst
                
                let! bookings =
                    bookingIds
                    |> List.traverseResultM (bookingsViewer >> Result.map snd)
                let! rows =
                    rowIds
                    |> List.traverseResultM (seatsViewer >> Result.map snd)
                    
                let assignBookingsToRowsCommands: List<AggregateCommand<Row, RowEvents>> =
                    List.zip bookingIds bookings
                    |> List.map (fun (bookingId, booking) -> RowCommands.Book (bookingId, booking.ClaimedSeats)) 
                
                let assignRowsToBookingsCommands: List<AggregateCommand<Booking, BookingEvents>> =
                    List.zip rowIds rows
                    |> List.map (fun (rowId, row) -> BookingCommands.Assign rowId)    
              
                return!
                    forceRunTwoNAggregateCommands<Booking, BookingEvents, Row, RowEvents, string> bookingIds rowIds eventStore eventBroker assignRowsToBookingsCommands assignBookingsToRowsCommands
            }
            
        member this.AssignBookingUsingSagaWay (bookingAndRows: List<Guid * Guid>) =
            result {
                let rowIds = bookingAndRows |> List.map snd
                let bookingIds = bookingAndRows |> List.map fst
                
                let! bookings =
                    bookingIds
                    |> List.traverseResultM (bookingsViewer >> Result.map snd)
                let! rows =
                    rowIds
                    |> List.traverseResultM (seatsViewer >> Result.map snd)
                
                let assignBookingsToRowsCommands: List<AggregateCommand<Row, RowEvents>> =
                    List.zip bookingIds bookings
                    |> List.map (fun (bookingId, booking) -> RowCommands.Book (bookingId, booking.ClaimedSeats)) 
                
                let assignRowsToBookingsCommands: List<AggregateCommand<Booking, BookingEvents>> =
                    List.zip rowIds rows
                    |> List.map (fun (rowId, row) -> BookingCommands.Assign rowId)
                    
                let! result =
                    runSagaTwoNAggregateCommands<Booking, BookingEvents, Row, RowEvents, string> bookingIds rowIds eventStore eventBroker assignRowsToBookingsCommands assignBookingsToRowsCommands
                return result
            }   
                
            