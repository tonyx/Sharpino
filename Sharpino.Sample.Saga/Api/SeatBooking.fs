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
                    |> List.traverseResultM (seatsViewer >> Result.map snd)
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
                    let totalNumberOfRowsToBeRemoved = ns |> List.sum
                    let totalNumberOfSeats = row.FreeSeats
                    let! validation =
                        totalNumberOfRowsToBeRemoved <= totalNumberOfSeats
                        |> Result.ofBool "total number of seats to be removed is greater than the total number of seats"
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
                    |> List.traverseResultM (bookingsViewer >> Result.map snd)
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
        
        // even though without saga forcing running commands toward the same aggregate id could fail
        // we can prevent this by using prevalidation 
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
               
                // pre-validation is a pain here, only because I wanted to use forceRunTwoNAggregateCommands with no-saga
                let uniqueRows =
                    rows |> List.distinctBy (fun r -> r.Id)

                let rowsPerBookings =
                    bookingAndRows
                    |> List.groupBy snd
                    |> List.map (fun (rowId, bookingAndRows) -> rowId, bookingAndRows |> List.map fst)
             
                let claimsPerRows =
                    rowsPerBookings
                    |> Map.ofList
                    
                let seatsClaimedPerRow =
                    rowsPerBookings
                    |> List.map (fun (rowId, bookingIds) -> (rows |> List.find (fun x -> x.Id = rowId)).FreeSeats  , bookingIds |> List.sumBy (fun bookingId -> bookings |> List.find (fun b -> b.Id = bookingId) |> fun b -> b.ClaimedSeats))
                    
                let! enoughFreeSeats =
                    seatsClaimedPerRow
                    |> List.forall (fun (totalSeats, totalClaimedSeats) -> totalSeats >= totalClaimedSeats)
                    |> Result.ofBool "not enough free seats"
                
                // prevalidation finished:
                // what I have got here (also thanks copilot) is
                // for each row, considering all the booking, the total number of seats claimed is less than the total number of free seats
                // only if this fits I can proceed with the forceRunTwoNAggregateCommands
                    
                    
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
                
            