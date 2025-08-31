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
open ShoppingCart.CartEvents
open ShoppingCart.Cart

module CartConsumer =
    type CartConsumer(sp: IServiceProvider, logger: ILogger<CartConsumer>, rb: RabbitMqReceiver) =
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
            let streamName = Cart.Cart.Version + Cart.Cart.StorageName
            channel.QueueDeclareAsync (streamName, false, false, false, null)
            |> Async.AwaitTask
            |> Async.RunSynchronously

        let mutable fallBackAggregateStateRetriever: Option<AggregateViewer<Cart.Cart>>  =
            None 
        
        let statePerAggregate =
            ConcurrentDictionary<AggregateId, EventId * Cart.Cart>()
         
        let consumer = AsyncEventingBasicConsumer channel
        
        member this.SetFallbackAggregateStateRetriever (aggregateStateRetriever: AggregateViewer<Cart.Cart>) =
            fallBackAggregateStateRetriever <- Some aggregateStateRetriever
            
        member this.ResetFallbackAggregateStateRetriever () =
            fallBackAggregateStateRetriever <- None
          
        member this.GetAggregateState (id: AggregateId) =
            if (statePerAggregate.ContainsKey id) then
                statePerAggregate.[id]
                |> Result.Ok
            else
                Result.Error "No state"
                
        override this.ExecuteAsync (stoppingToken) =
            consumer.add_ReceivedAsync
                (fun _ ea ->
                    rb.BuildReceiver<Cart, CartEvents, string> statePerAggregate fallBackAggregateStateRetriever ea
                )
            consumer.add_ShutdownAsync
                (fun _ ea ->
                    task
                        {
                            logger.LogInformation($"Cart Consumer shutdown: {consumer.ShutdownReason}")
                            channel.Dispose()
                        }
                )
            channel.BasicConsumeAsync(queueDeclare.QueueName, true, consumer)
        
        override this.Dispose () =
            channel.Dispose()
            
