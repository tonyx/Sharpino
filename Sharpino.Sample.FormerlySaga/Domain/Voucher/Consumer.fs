module Sharpino.Sample.Saga.Domain.Voucher.Consumer

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
open Sharpino.Sample.Saga.Domain.Vaucher.Voucher
open Sharpino.Sample.Saga.Domain.Vaucher.Events

module VoucherConsumer =
    type VoucherConsumer(sp: IServiceProvider, logger: ILogger<VoucherConsumer>) =
        inherit BackgroundService()
        let factory = ConnectionFactory (HostName = "localhost")
        let connection =
            factory.CreateConnectionAsync ()
            |> Async.AwaitTask
            |> Async.RunSynchronously
        
        let channel =
            connection.CreateChannelAsync ()
            |> Async.AwaitTask
            |> Async.RunSynchronously
        
        let queueDeclare =
            let streamName = Voucher.Version + Voucher.StorageName
            channel.QueueDeclareAsync (streamName, false, false, false, null)
            |> Async.AwaitTask
            |> Async.RunSynchronously
        
        let mutable fallBackAggregateStateRetriever: Option<AggregateViewer<Voucher>> = None
        
        let statePerAggregate =
            ConcurrentDictionary<AggregateId, EventId * Voucher>()
        
        let resyncWithFallbackAggregateStateRetriever (id: AggregateId) =
            match fallBackAggregateStateRetriever with
            | Some retriever ->
                match retriever id with
                | Ok (eventId, state) ->
                    statePerAggregate.[id] <- (eventId, state)
                | Error e ->
                    logger.LogError ("Error: {e}", e)
            | None ->
                logger.LogError "no fallback aggregate state retriever set"
       
        let consumer = AsyncEventingBasicConsumer channel
        
        do
            consumer.add_ReceivedAsync
                (fun _ ea ->
                    task {
                        let body = ea.Body.ToArray()
                        let message = Encoding.UTF8.GetString(body)
                        logger.LogDebug ("ReceivedX {message}", message)
                        let deserializedMessage = AggregateMessage<Voucher, VoucherEvents>.Deserialize message
                        match deserializedMessage with
                        | Ok message ->
                            let aggregateId = message.AggregateId
                            match message with
                            | { Message = InitialSnapshot good } ->
                                statePerAggregate.[aggregateId] <- (0, good)
                                ()
                            | { Message = MessageType.Events { InitEventId = eventId; EndEventId = endEventId; Events = events  } }  ->
                                if (statePerAggregate.ContainsKey aggregateId && (statePerAggregate.[aggregateId] |> fst = eventId || statePerAggregate.[aggregateId] |> fst = 0)) then
                                    let currentState = statePerAggregate.[aggregateId] |> snd
                                    let newState = evolve currentState events
                                    if newState.IsOk then
                                        statePerAggregate.[aggregateId] <- (endEventId, newState.OkValue)
                                    else
                                        let (Error e) = newState
                                        logger.LogError ("error {e}", e)
                                        resyncWithFallbackAggregateStateRetriever aggregateId
                                else
                                    resyncWithFallbackAggregateStateRetriever aggregateId
                            | { Message = MessageType.Delete } when statePerAggregate.ContainsKey aggregateId ->
                                statePerAggregate.TryRemove aggregateId  |> ignore
                            | { Message = MessageType.Delete }  ->
                                logger.LogError ("deleting an unexisting aggregate: {aggregateId}", aggregateId)
                        | Error e ->
                            logger.LogError ("Error: {e}", e)            
                        return ()
                   }
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
                Error $"Aggregate {aggregateId} nod found viewer for type: {Voucher.Version + Voucher.StorageName}" 
         
        override this.ExecuteAsync cancellationToken =
            channel.BasicConsumeAsync(queueDeclare.QueueName, false, consumer)
