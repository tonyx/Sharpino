namespace Sharpino.Sample._10.Models.Consumer

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
open Sharpino.Sample._10.Models.Counter
open Sharpino.Sample._10.Models.Events

module CounterConsumer =
    type CounterConsumer (sp: IServiceProvider, logger: ILogger<CounterConsumer>, rb: RabbitMqReceiver) =
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
            let streamName = Counter.Version + Counter.StorageName
            channel.QueueDeclareAsync (streamName, false, false, false, null)
            |> Async.AwaitTask
            |> Async.RunSynchronously
        
        let mutable fallBackAggregateStateRetriever: Option<AggregateViewer<Counter>> =
            None
        
        let statePerAggregate =
            ConcurrentDictionary<AggregateId, EventId * Counter>()
        
        let consumer = AsyncEventingBasicConsumer channel
        
        member this.SetFallbackAggregateStateRetriever (aggregateViewer: AggregateViewer<Counter>) =
            fallBackAggregateStateRetriever <- Some aggregateViewer
        
        member this.GetAggregateState (aggregateId: AggregateId) =
            if (statePerAggregate.ContainsKey aggregateId) then
                statePerAggregate.[aggregateId]
                |> Result.Ok
            else
                Result.Error $"No state for aggregate {aggregateId} of type: {Counter.Version + Counter.StorageName}"
        
        override this.ExecuteAsync cancellationToken =
            consumer.add_ReceivedAsync
                (fun _ ea ->
                    rb.BuildReceiver<Counter, CounterEvents, byte[]> statePerAggregate fallBackAggregateStateRetriever ea
                )
            channel.BasicConsumeAsync(queueDeclare.QueueName, true, consumer)