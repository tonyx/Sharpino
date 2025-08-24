
namespace Tonyx.Sharpino.Pub

open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open RabbitMQ.Client
open RabbitMQ.Client.Events
open Sharpino
open Sharpino.CommandHandler
open Sharpino.Commons
open Sharpino.Definitions
open Sharpino.EventBroker
open Sharpino.Core
open System
open System.Collections.Concurrent
open System.Text
open Tonyx.Sharpino.Pub.Supplier
open Tonyx.Sharpino.Pub.SupplierEvents

module SupplierConsumer =
    type SupplierConsumer(sp: IServiceProvider, logger: ILogger<SupplierConsumer>) =
        inherit BackgroundService()
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
            let streamName = Supplier.Version + Supplier.StorageName
            channel.QueueDeclareAsync (streamName, false, false, false, null)
            |> Async.AwaitTask
            |> Async.RunSynchronously
        
        let mutable fallBackAggregateStateRetriever: Option<AggregateViewer<Supplier>>  =
            None
        
        let statePerAggregate =
            ConcurrentDictionary<AggregateId, EventId * Supplier>()
        
        member this.SetFallbackAggregateStateRetriever (aggregateViewer: AggregateViewer<Supplier>) =
            fallBackAggregateStateRetriever <- Some aggregateViewer
            
        member this.ResetFallbackAggregateStateRetriever () =
            fallBackAggregateStateRetriever <- None    
       
        member this.GetAggregateState (id: AggregateId) =
            if (statePerAggregate.ContainsKey id) then
                statePerAggregate.[id]
                |> Result.Ok
            else
                Result.Error "No state"    
        
        override this.ExecuteAsync(stoppingToken) =
            let consumer =  AsyncEventingBasicConsumer channel
            consumer.add_ReceivedAsync
                (fun _ ea ->
                    task {
                        let body = ea.Body.ToArray()
                        let message = Encoding.UTF8.GetString(body)
                        logger.LogDebug ("Received {message}", message)
                        let deserializedMessage = jsonPSerializer.Deserialize<AggregateMessage<Supplier, SupplierEvents>> message
                        match deserializedMessage with
                        | Ok message ->
                            let aggregateId = message.AggregateId
                            match message with
                            | { Message = InitialSnapshot good } ->
                                statePerAggregate.[aggregateId] <- (0, good)
                                ()
                            | { Message = MessageType.Events { InitEventId = eventId; EndEventId = endEventId; Events = events } }  ->
                                if (statePerAggregate.ContainsKey aggregateId && (statePerAggregate.[aggregateId] |> fst = eventId || statePerAggregate.[aggregateId] |> fst = 0)) then
                                    let currentState = statePerAggregate.[aggregateId] |> snd
                                    let newState = evolve currentState events
                                    if newState.IsOk then
                                        statePerAggregate.[aggregateId] <- (endEventId, newState.OkValue)
                                    else
                                        let (Error e) = newState
                                        logger.LogError ("Error {error}", e)
                                        match fallBackAggregateStateRetriever with
                                        | Some aggregateViewer ->
                                            let state = aggregateViewer aggregateId
                                            match state with
                                            | Ok (eventId, state) ->
                                                statePerAggregate.[aggregateId] <- (eventId, state)
                                            | Error e ->
                                                logger.LogError ("Error {error}", e)
                                            ()
                                        | None ->
                                            logger.LogError ("no fallback aggregate state retriever set")
                                        ()
                                else
                                    logger.LogError ("No state for aggregateId {aggregateId}", aggregateId)
                                    () 
                            | { Message = MessageType.Delete } ->
                                if (statePerAggregate.ContainsKey aggregateId) then
                                    statePerAggregate.TryRemove aggregateId |> ignore
                                else
                                    logger.LogError ("deleting an unexisting aggregate: {aggregateId}", aggregateId)
                        return ()
                   })
            channel.BasicConsumeAsync(queueDeclare.QueueName, true, consumer)    
