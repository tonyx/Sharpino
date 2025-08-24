module Sharpino.TransportTycoon.Truck

open Sharpino.RabbitMq
open Sharpino.TransportTycoon.Transporter
open Sharpino.TransportTycoon.TruckEvents
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

module Consumer =
    type TransporterConsumer(sp: IServiceProvider, logger: ILogger<TransporterConsumer>, rb: RabbitMqReceiver) =
        inherit BackgroundService ()
        let factory = ConnectionFactory (HostName = "localhost")
        let connection =
            factory.CreateConnectionAsync()
            |> Async.AwaitTask
            |> Async.RunSynchronously
        let channel =
            connection.CreateChannelAsync ()
            |> Async.AwaitTask
            |> Async.RunSynchronously
        let queueDeclare =
            channel.QueueDeclareAsync ("_01_truck", false, false, false, null)
            |> Async.AwaitTask
            |> Async.RunSynchronously

        let mutable fallBackAggregateStateRetriever: Option<AggregateViewer<Transporter>>  =
            None
            
        let statePerAggregate =
            ConcurrentDictionary<AggregateId, EventId * Transporter>()
            
        let resyncWithFallbackAggregateStateRetriever (id: AggregateId) =
            let retriever = fallBackAggregateStateRetriever
            match retriever with
            | Some retriever ->
                match retriever id with
                | Result.Ok (eventId, state) ->
                    statePerAggregate.[id] <- (eventId, state)
                | Result.Error e ->
                    logger.LogError ("Error: {e}", e)
            | None ->
                logger.LogError "no fallback aggregate state retriever set"    
        let consumer =  AsyncEventingBasicConsumer channel
        do
            consumer.add_ReceivedAsync
                (fun _ ea ->
                    rb.BuildReceiver<Transporter, TruckEvents, string> statePerAggregate fallBackAggregateStateRetriever ea
                )
            
        member this.SetFallbackAggregateStateRetriever (retriever: AggregateViewer<Transporter>) =
            fallBackAggregateStateRetriever <- Some retriever
      
        member this.ResetFallbackAggregateStateRetriever () =
            fallBackAggregateStateRetriever <- None
       
        member this.ResyncWithFallbackAggregateStateRetriever (id: AggregateId) =
            let retriever = fallBackAggregateStateRetriever
            match retriever with
            | Some retriever ->
                match retriever id with
                | Result.Ok (eventId, state) ->
                    statePerAggregate.[id] <- (eventId, state)
                | Result.Error e ->
                    logger.LogError ("Error: {e}", e)
            | None ->
                logger.LogError "no fallback aggregate state retriever set"        
         
        member this.GetAggregateState (id: AggregateId) =
            if (statePerAggregate.ContainsKey id) then
                statePerAggregate.[id]
                |> Result.Ok
            else
                Result.Error "No state"
         
        override this.ExecuteAsync (stoppingToken) =
            channel.BasicConsumeAsync(queueDeclare.QueueName, true, consumer)    


