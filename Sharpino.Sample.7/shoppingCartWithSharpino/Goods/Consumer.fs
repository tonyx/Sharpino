namespace ShoppingCart

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
open ShoppingCart.GoodEvents

module GoodConsumer =
    type GoodConsumer(sp: IServiceProvider, logger: ILogger<GoodConsumer>) =
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
            let streamName = Good.Good.Version + Good.Good.StorageName
            channel.QueueDeclareAsync (streamName, false, false, false, null)
            |> Async.AwaitTask
            |> Async.RunSynchronously

        let mutable fallBackAggregateStateRetriever: Option<AggregateViewer<Good.Good>>  =
            None 
        
        let statePerAggregate =
            ConcurrentDictionary<AggregateId, EventId * Good.Good>()
        
        member this.SetFallbackAggregateStateRetriever (aggregateViewer: AggregateViewer<Good.Good>) =
            fallBackAggregateStateRetriever <- Some aggregateViewer    
       
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
            let consumer =  AsyncEventingBasicConsumer channel
            consumer.add_ReceivedAsync
                (fun _ ea ->
                    task {
                        let body = ea.Body.ToArray()
                        let message = Encoding.UTF8.GetString(body)
                        logger.LogDebug ("Received {message}", message)
                        let deserializedMessage = AggregateMessage<Good.Good, GoodEvents>.Deserialize message
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
                                        logger.LogError ("error {e}", e)
                                        this.ResyncWithFallbackAggregateStateRetriever aggregateId
                                else
                                    this.ResyncWithFallbackAggregateStateRetriever aggregateId
                            | { Message = Message.Delete } when statePerAggregate.ContainsKey aggregateId ->
                                statePerAggregate.TryRemove aggregateId  |> ignore
                            | { Message = Message.Delete }  ->
                                logger.LogError ("deleting an unexisting aggregate: {aggregateId}", aggregateId)
                        | Error e ->
                            logger.LogError ("Error: {e}", e)            
                        return ()
                   }
                )
            channel.BasicConsumeAsync(queueDeclare.QueueName, true, consumer)    
