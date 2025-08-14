namespace Tonyx.Sharpino.Pub

open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open RabbitMQ.Client
open RabbitMQ.Client.Events
open Sharpino.Commons
open Sharpino.Definitions
open Sharpino.EventBroker
open Sharpino.Core
open System
open System.Collections.Concurrent
open System.Text
open Tonyx.Sharpino.Pub.Dish
open Tonyx.Sharpino.Pub.DishEvents

module DishConsumer =
    type DishConsumer (sp: IServiceProvider, logger: ILogger<DishConsumer>) =
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
            let streamName = Dish.Version + Dish.StorageName
            channel.QueueDeclareAsync (streamName, false, false, false, null)
            |> Async.AwaitTask
            |> Async.RunSynchronously
        
        let mutable fallBackAggregateStateRetriever: Option<AggregateViewer<Dish>>  =
            None
            
        let consumer =  AsyncEventingBasicConsumer channel
        
        let statePerAggregate =
            ConcurrentDictionary<AggregateId, EventId * Dish> ()
       
        let resyncWithFallbackAggregateStateRetriever (id: AggregateId) =
            match fallBackAggregateStateRetriever  with
            | Some retriever ->
                match retriever id with
                | Result.Ok (eventId, state) ->
                    statePerAggregate.[id] <- (eventId, state)
                | _ -> ()
            | None -> ()
       
        do
            consumer.add_ReceivedAsync
                (fun _ ea ->
                    task {
                        let body = ea.Body.ToArray()
                        let message = Encoding.UTF8.GetString(body)
                        logger.LogDebug ("Received {message}", message)
                        let deserializedMessage = AggregateMessage<Dish, DishEvents>.Deserialize message
                        match deserializedMessage with
                        | Ok message ->
                            let aggregateId = message.AggregateId
                            match message with
                            | { Message = InitialSnapshot good } ->
                                statePerAggregate.[aggregateId] <- (0, good)
                                ()
                            | { Message = Message.Events { InitEventId = eventId; EndEventId = endEventId; Events = events  } }  ->
                                if (statePerAggregate.ContainsKey aggregateId && (statePerAggregate.[aggregateId] |> fst = eventId || statePerAggregate.[aggregateId] |> fst = 0)) then
                                    let currentState = statePerAggregate.[aggregateId] |> snd
                                    let newState = evolve currentState events
                                    if newState.IsOk then
                                        statePerAggregate.[aggregateId] <- (endEventId, newState.OkValue)
                                    else
                                        let (Error e) = newState
                                        logger.LogError (e, "Error applying events to aggregate state: {aggregateId} {eventId} {endEventId} {events}", aggregateId, eventId, endEventId, events)
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
                                else
                                    logger.LogError ("no state for aggregateId {aggregateId}", aggregateId)
                                    () 
                            | { Message = Message.Delete } ->
                                if (statePerAggregate.ContainsKey aggregateId) then
                                    statePerAggregate.TryRemove aggregateId |> ignore
                                else
                                    logger.LogError ("no state for aggregateId {aggregateId}", aggregateId)
                        | Error e ->
                            logger.LogError ("Error {error}", e)            
                        return ()
                   }
                )
            
        member this.SetFallbackAggregateStateRetriever (aggregateViewer: AggregateViewer<Dish>) =
            fallBackAggregateStateRetriever <- Some aggregateViewer 
      
        member this.GetAggregateState (id: AggregateId) =
            if (statePerAggregate.ContainsKey id) then
                statePerAggregate.[id]
                |> Result.Ok
            else
                Result.Error "No state"
        
        override this.ExecuteAsync (stoppingToken) =
            channel.BasicConsumeAsync(queueDeclare.QueueName, true, consumer)    
            