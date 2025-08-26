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
open Sharpino.Sample._10.Models.Account
open Sharpino.Sample._10.Models.AccountEvents

module AccountConsumer =
    type AccountConsumer (sp: IServiceProvider, logger: ILogger<AccountConsumer>, rb: RabbitMqReceiver) =
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
            let streamName = Account.Version + Account.StorageName
            channel.QueueDeclareAsync (streamName, false, false, false, null)
            |> Async.AwaitTask
            |> Async.RunSynchronously
        
        let mutable fallBackAggregateStateRetriever: Option<AggregateViewer<Account>> =
            None
        
        let statePerAggregate =
            ConcurrentDictionary<AggregateId, EventId * Account>()
        
        let consumer = AsyncEventingBasicConsumer channel
        
        member this.SetFallbackAggregateStateRetriever (aggregateViewer: AggregateViewer<Account>) =
            fallBackAggregateStateRetriever <- Some aggregateViewer
        
        member this.GetAggregateState (aggregateId: AggregateId) =
            if (statePerAggregate.ContainsKey aggregateId) then
                statePerAggregate.[aggregateId]
                |> Result.Ok
            else
                Result.Error $"No state found for aggregate {aggregateId} of type: {Account.Version + Account.StorageName}"
        
        override this.ExecuteAsync cancellationToken =
            consumer.add_ReceivedAsync
                (fun _ ea ->
                    rb.BuildReceiver<Account, AccountEvents, byte[]> statePerAggregate fallBackAggregateStateRetriever ea
                )
            channel.BasicConsumeAsync(queueDeclare.QueueName, true, consumer)        