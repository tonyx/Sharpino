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
open Sharpino.RabbitMq
open Sharpino.Sample._9.Balance
open Sharpino.Sample._9.BalanceEvents

module BalanceConsumer =
    type BalanceConsumer(sp: IServiceProvider, logger: ILogger<BalanceConsumer>, rb: RabbitMqReceiver) =
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
                ( fun _ ea ->
                    rb.BuildReceiver<Balance, BalanceEvents, string> statePerAggregate fallBackAggregateStateRetriever ea
                )
        
        member this.SetFallbackAggregateStateRetriever (aggregateViewer: AggregateViewer<Balance.Balance>) =
            fallBackAggregateStateRetriever <- Some aggregateViewer
            
        member this.ResetFallbackAggregateStateRetriever () =
            fallBackAggregateStateRetriever <- None    
            
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
            
                
                
                
                
                
                
                
                
                
