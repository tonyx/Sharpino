namespace Sharpino.Sample._9

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
open Sharpino.Sample._9.Item
open Sharpino.Sample._9.Events

module ItemConsumer =
    type ItemConsumer(sp: IServiceProvider, logger : ILogger<ItemConsumer>, rb: RabbitMqReceiver) =
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
            let streamName = Item.Item.Version + Item.Item.StorageName
            channel.QueueDeclareAsync (streamName, false, false, false, null)
            |> Async.AwaitTask
            |> Async.RunSynchronously
            
        let mutable fallBackAggregateStateRetriever: Option<AggregateViewer<Item.Item>>  =
            None
            
        let statePerAggregate =
            ConcurrentDictionary<AggregateId, EventId * Item.Item>()
       
        let setFallbackAggregateStateRetriever (aggregateViewer: AggregateViewer<Item.Item>) =
            fallBackAggregateStateRetriever <- Some aggregateViewer
        
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
                    rb.BuildReceiver<Item, ItemEvent, string> statePerAggregate fallBackAggregateStateRetriever ea
                )
         
        member this.SetFallbackAggregateStateRetriever (aggregateViewer: AggregateViewer<Item.Item>) =
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
            
        member this.ResetAllStates () =
            statePerAggregate.Clear()
            
        member this.GetAggregateState (id: AggregateId) =
            if (statePerAggregate.ContainsKey id) then
                statePerAggregate.[id]
                |> Result.Ok
            else
                Result.Error "No state"
        
        override this.ExecuteAsync cancellationToken =
            consumer.add_ReceivedAsync
                (fun _ ea ->
                    rb.BuildReceiver<Item, ItemEvent, string> statePerAggregate fallBackAggregateStateRetriever ea
                )
            channel.BasicConsumeAsync(queueDeclare.QueueName, true, consumer)
       
        override this.Dispose () =
            channel.Dispose ()     
