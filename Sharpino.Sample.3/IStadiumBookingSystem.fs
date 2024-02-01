
namespace Tonyx.SeatsBooking
open Tonyx.SeatsBooking.Seats
open Tonyx.SeatsBooking.NewRow
open Tonyx.SeatsBooking.Stadium
open Tonyx.SeatsBooking.StadiumEvents
open Tonyx.SeatsBooking.StadiumCommands
open Tonyx.SeatsBooking.RowAggregateEvent
open Tonyx.SeatsBooking.RowAggregateCommand
open Tonyx.SeatsBooking
open Sharpino.CommandHandler
open Sharpino.Definitions
open System
open FsToolkit.ErrorHandling
open Sharpino.Storage
open Sharpino.ApplicationInstance
open Sharpino.Core
open Sharpino.Utils

module IStadiumBookingSystem =
    
    type IStadiumBookingSystem =
        abstract member AddRowReference : Guid -> Result<(List<EventId> list * List<Confluent.Kafka.DeliveryResult<Confluent.Kafka.Null,string>> option list),string>
        abstract member BookSeats : Guid -> Booking -> Result<(List<EventId> list * List<Confluent.Kafka.DeliveryResult<Confluent.Kafka.Null,string>> option list),string>
        abstract member BookSeatsNRows : List<Guid * Booking> -> Result<(List<List<EventId>> * List<Confluent.Kafka.DeliveryResult<Confluent.Kafka.Null,string>> option list),string>
        abstract member GetRow : Guid -> Result<SeatsRow,string>
        abstract member AddSeat: Guid -> Seat -> Result<(List<EventId> list * List<Confluent.Kafka.DeliveryResult<Confluent.Kafka.Null,string>> option list),string>
        abstract member AddSeats: Guid -> List<Seat> -> Result<(List<EventId> list * List<Confluent.Kafka.DeliveryResult<Confluent.Kafka.Null,string>> option list),string>
        abstract member GetAllRowReferences: unit -> Result<List<Guid>,string>
        