
module Sharpino.Sample.Saga.Domain.Booking.Consumer

open System
open System.Collections.Concurrent
open System.Text
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open RabbitMQ.Client
open RabbitMQ.Client.Events
open Sharpino.Commons
open Sharpino.Definitions
open Sharpino.EventBroker
open Sharpino.Core
open Sharpino.RabbitMq
open Sharpino.Sample.Saga.Domain.Booking.Booking
open Sharpino.Sample.Saga.Domain.Booking.Events

module BookingConsumer =
    type BookingConsumer(sp: IServiceProvider, logger: ILogger<BookingConsumer>, rb: RabbitMqReceiver) =
        inherit BackgroundService()
        let factory = ConnectionFactory (HostName = "localhost")
        let connection =
            factory.CreateConnectionAsync()
            |> Async.AwaitTask
            |> Async.RunSynchronously
        let channel =
            connection.CreateChannelAsync()
            |> Async.AwaitTask
            |> Async.RunSynchronously
        let queueDeclare =
            let streamName = Booking.Version + Booking.StorageName
            channel.QueueDeclareAsync (streamName, false, false, false, null)
            |> Async.AwaitTask
            |> Async.RunSynchronously
        let mutable fallBackAggregateStateRetriever: Option<AggregateViewer<Booking>> = None    
        
        let statePerAggregate =
            ConcurrentDictionary<AggregateId, EventId * Booking>()
        
        let resyncWithFallbackAggregateStateRetriever (id: AggregateId) =
            match fallBackAggregateStateRetriever with
            | Some retriever ->
                match retriever id with
                | Ok (eventId, state) ->
                    statePerAggregate.[id] <- (eventId, state)
                | Error e ->
                    logger.LogError ("Error: {e}", e)
            | None ->
                logger.LogError ("No fallback aggregate state retriever")
       
        let consumer = AsyncEventingBasicConsumer channel
       
        do
            consumer.add_ReceivedAsync
                (fun _ ea ->
                    rb.BuildReceiver<Booking, BookingEvents, string> statePerAggregate fallBackAggregateStateRetriever ea
                )
        
        member this.SetFallbackAggregateStateRetriever retriever =
            fallBackAggregateStateRetriever <- Some retriever
        
        member this.ResetFallbackAggregateStateRetriever () =
            fallBackAggregateStateRetriever <- None
        
        member this.ResetAllStates () =
            statePerAggregate.Clear()
      
        member this.GetAggregateState (aggregateId: AggregateId) =
            if (statePerAggregate.ContainsKey aggregateId) then
                statePerAggregate.[aggregateId] |> Ok
            else
                Error $"Aggregate {aggregateId} nod found viewer for type: {Booking.Version + Booking.StorageName}"
         
        override this.ExecuteAsync cancellationToken =
            channel.BasicConsumeAsync (queueDeclare.QueueName, false, consumer)
