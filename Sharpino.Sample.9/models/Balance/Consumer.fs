namespace  Sharpino.Sample._9

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
open Sharpino.Sample._9.Balance
open Sharpino.Sample._9.BalanceEvents

module BalanceConsumer =
    type BalanceConsumer(sp: IServiceProvider, logger: ILogger<BalanceConsumer>) =
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
            let streamName = Balance.Balance.Version + Balance.Balance.StorageName
            channel.QueueDeclareAsync (streamName, false, false, false, null)
            |> Async.AwaitTask
            |> Async.RunSynchronously
            
        let mutable fallBackAggregateStateRetriever: Option<AggregateViewer<Balance.Balance>>  =
            None
            
        let statePerAggregate =
            ConcurrentDictionary<AggregateId, EventId * Balance.Balance>()
        
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
                
        let consumer = AsyncEventingBasicConsumer channel
        
        do
            consumer.add_ReceivedAsync
                (fun _ ea ->
                    task {
                        let body = ea.Body.ToArray()
                        let message = Encoding.UTF8.GetString(body)
                        logger.LogDebug ("ReceivedX {message}", message)
                        let deserializedMessage = AggregateMessage<Balance, BalanceEvents>.Deserialize message
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
                                    // logger.LogInformation ("XXXXX evolving {aggregateId} from {eventId} to {endEventId}", aggregateId, eventId, endEventId)
                                    // logger.LogInformation ("XXXXX currentState {currentState}", currentState)
                                    // logger.LogInformation ("XXXXX events {events}", events)
                                    // logger.LogInformation ("XXXXX newState {newState}", newState)
                                    if newState.IsOk then
                                        statePerAggregate.[aggregateId] <- (endEventId, newState.OkValue)
                                        // logger.LogInformation ("XXXXYYYYYX newState {newState}", (newState.OkValue).ToString())
                                    else
                                        let (Error e) = newState
                                        logger.LogError ("error {e}", e)
                                        resyncWithFallbackAggregateStateRetriever aggregateId
                                else
                                    resyncWithFallbackAggregateStateRetriever aggregateId
                            | { Message = Message.Delete } when statePerAggregate.ContainsKey aggregateId ->
                                statePerAggregate.TryRemove aggregateId  |> ignore
                            | { Message = Message.Delete }  ->
                                logger.LogError ("deleting an unexisting aggregate: {aggregateId}", aggregateId)
                        | Error e ->
                            logger.LogError ("Error: {e}", e)            
                        return ()
                   }
                )
        
        member this.SetFallbackAggregateStateRetriever (aggregateViewer: AggregateViewer<Balance.Balance>) =
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

        member this.ResetAllStates () =
            statePerAggregate.Clear() 
        
        override this.ExecuteAsync (cancellationToken) =
            channel.BasicConsumeAsync (queueDeclare.QueueName, false, consumer)
            
                
                
                
                
                
                
                
                
                
