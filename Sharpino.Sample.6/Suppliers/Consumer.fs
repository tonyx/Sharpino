
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
open Sharpino.RabbitMq
open Tonyx.Sharpino.Pub.Supplier
open Tonyx.Sharpino.Pub.SupplierEvents

module SupplierConsumer =
    type SupplierConsumer(sp: IServiceProvider, logger: ILogger<SupplierConsumer>, rb: RabbitMqReceiver) =
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
        
        let consumer = AsyncEventingBasicConsumer channel    
        
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
            consumer.add_ReceivedAsync
                (fun _ ea ->
                    rb.BuildReceiver<Supplier, SupplierEvents, string> statePerAggregate fallBackAggregateStateRetriever ea
                )
            consumer.add_ShutdownAsync
                (fun _ ea ->
                    task
                        {
                            logger.LogInformation($"Supplier Consumer shutdown: {consumer.ShutdownReason}")
                            channel.Dispose()
                        }
                )
            channel.BasicConsumeAsync(queueDeclare.QueueName, true, consumer)    

        override this.Dispose () =
            channel.Dispose()