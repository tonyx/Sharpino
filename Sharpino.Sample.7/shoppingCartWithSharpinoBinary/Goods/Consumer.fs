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
open Sharpino.RabbitMq
open ShoppingCartBinary
open ShoppingCartBinary.Good
open ShoppingCartBinary.GoodEvents

module GoodConsumer =
    type GoodConsumer (sp: IServiceProvider, logger: ILogger<GoodConsumer>, rb: RabbitMqReceiver) =
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
            let streamName = Good.Good.Version + Good.Good.StorageName
            channel.QueueDeclareAsync (streamName, false, false, false, null)
            |> Async.AwaitTask
            |> Async.RunSynchronously
            
        let statePerAggregate =
            ConcurrentDictionary<AggregateId, EventId * Good.Good>()
            
        let mutable fallBackAggregateStateRetriever: Option<AggregateViewer<Good.Good>>  =
            None
        let consumer =  AsyncEventingBasicConsumer channel
        do
            consumer.add_ReceivedAsync
                (fun _ ea ->
                    rb.BuildReceiver<Good, GoodEvents, byte[]> statePerAggregate fallBackAggregateStateRetriever ea
                )
            consumer.add_ShutdownAsync
                (fun _ ea ->
                    task
                        {
                            logger.LogInformation($"Good Consumer shutdown: {consumer.ShutdownReason}")
                            channel.Dispose()
                        }
                )
          
        member this.SetFallbackAggregateStateRetriever (aggregateViewer: AggregateViewer<Good.Good>) =
            fallBackAggregateStateRetriever <- Some aggregateViewer 
         
        member this.GetAggregateState (id: AggregateId) =
            if (statePerAggregate.ContainsKey id) then
                statePerAggregate.[id]
                |> Result.Ok
            else
                Result.Error "No state"
                
        override this.ExecuteAsync (stoppingToken) =
            channel.BasicConsumeAsync(queueDeclare.QueueName, true, consumer, stoppingToken)
            
        override this.Dispose () =
            channel.Dispose()    
