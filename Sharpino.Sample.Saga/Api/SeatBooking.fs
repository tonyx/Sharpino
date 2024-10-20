namespace Sharpino.Sample.Saga.Api
// module Sharpino.Sample.Saga.Api.SeatBooking
open System
open Sharpino
open Sharpino.CommandHandler
open Sharpino.StateView
open Sharpino.Commons

open Sharpino.Core
open FSharpPlus
open FSharpPlus.Operators
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
         viewer: StateViewer<Theater>,
         seatsViewer: AggregateViewer<Row>,
         bookingsViewer: AggregateViewer<Booking>) 
         =
        
        member this.GetRow id =
            result {
                let! (_, row) = seatsViewer id
                return row
            }
            
        member this.GetRows() =
            result {
                let! (_, theater) = viewer ()
                let rowReferences = theater.Rows
                let! rows =
                    rowReferences
                    |> List.traverseResultM (seatsViewer >> Result.map (fun (_, row) -> row))
                return rows    
            }
        
        member this.AddRow (row: Row) =
            result {
                let! (_, theater) = viewer ()
                let addRowReferenceCommand = AddRowReference (row.Id)
                let! result =
                    runInitAndCommand<Theater, TheaterEvents, Row, string> eventStore eventBroker row addRowReferenceCommand
                return result    
            }
        
        member this.GetBooking id =
            result {
                let! (_, booking) = bookingsViewer id
                return booking
            }
       
        member this.AddBooking (booking: Booking) =
            result {
                let! (_, theater) = viewer ()
                let addBookingReferenceCommand = AddBookingReference (booking.Id)
                let! result =
                    runInitAndCommand<Theater, TheaterEvents, Booking, string> eventStore eventBroker booking addBookingReferenceCommand
                return result    
            }
        member this.GetBookings() =
            result {
                let! (_, theater) = viewer ()
                let bookingReferences = theater.Bookings
                let! bookings =
                    bookingReferences
                    |> List.traverseResultM (bookingsViewer >> Result.map (fun (_, booking) -> booking))
                return bookings    
            }
        member this.AssignBooking bookingId rowId =
            result {
                let! (_, booking) = bookingsViewer bookingId
                let! (_, row) = seatsViewer rowId
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
                    |> List.traverseResultM (bookingsViewer >> Result.map (fun (_, booking) -> booking))
                let! rows =
                    rowIds
                    |> List.traverseResultM (seatsViewer >> Result.map (fun (_, row) -> row))
               
                let assignBookingsToRowsCommands: List<AggregateCommand<Row, RowEvents>> =
                    List.zip bookingIds bookings
                    |> List.map (fun (bookingId, booking) -> RowCommands.Book (bookingId, booking.ClaimedSeats)) 
                
                let assignRowsToBookingsCommands: List<AggregateCommand<Booking, BookingEvents>> =
                    List.zip rowIds rows
                    |> List.map (fun (rowId, row) -> BookingCommands.Assign rowId)    
              
                return!    
                    runTwoNAggregateCommands<Booking, BookingEvents, Row, RowEvents, string> bookingIds rowIds eventStore eventBroker assignRowsToBookingsCommands assignBookingsToRowsCommands
            }
        
        // deprecated: this will return wrong results if the target seat is repeated
        member this.ForceAssignBookings  (bookingAndRows: List<Guid * Guid>) =
            result {
                let rowIds = bookingAndRows |> List.map snd
                let bookingIds = bookingAndRows |> List.map fst
                
                let! bookings =
                    bookingIds
                    |> List.traverseResultM (bookingsViewer >> Result.map (fun (_, booking) -> booking))
                let! rows =
                    rowIds
                    |> List.traverseResultM (seatsViewer >> Result.map (fun (_, row) -> row))
                
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
                    |> List.traverseResultM (bookingsViewer >> Result.map (fun (_, booking) -> booking))
                let! rows =
                    rowIds
                    |> List.traverseResultM (seatsViewer >> Result.map (fun (_, row) -> row))
                
                let assignBookingsToRowsCommands: List<AggregateCommand<Row, RowEvents>> =
                    List.zip bookingIds bookings
                    |> List.map (fun (bookingId, booking) -> RowCommands.Book (bookingId, booking.ClaimedSeats)) 
                
                let assignRowsToBookingsCommands: List<AggregateCommand<Booking, BookingEvents>> =
                    List.zip rowIds rows
                    |> List.map (fun (rowId, row) -> BookingCommands.Assign rowId)
                    
                printf "XXXXXX YYYYY 1\n"
                let! result =
                    runSagaTwoNAggregateCommands<Booking, BookingEvents, Row, RowEvents, string> bookingIds rowIds eventStore eventBroker assignRowsToBookingsCommands assignBookingsToRowsCommands
                printf "XXXXXX YYYYY 2\n"
                return result
            }   
                
            
        member this.Foo() = "bar"
            