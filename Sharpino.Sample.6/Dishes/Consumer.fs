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
open Sharpino.RabbitMq
open Tonyx.Sharpino.Pub.Dish
open Tonyx.Sharpino.Pub.DishEvents

module DishConsumer =
    type DishConsumer (sp: IServiceProvider, logger: ILogger<DishConsumer>, rb: RabbitMqReceiver) =
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
            
        member this.SetFallbackAggregateStateRetriever (aggregateViewer: AggregateViewer<Dish>) =
            fallBackAggregateStateRetriever <- Some aggregateViewer 
      
        member this.GetAggregateState (id: AggregateId) =
            if (statePerAggregate.ContainsKey id) then
                statePerAggregate.[id]
                |> Result.Ok
            else
                Result.Error "No state"
        
        override this.ExecuteAsync (stoppingToken) =
            consumer.add_ReceivedAsync
                (fun _ ea ->
                    rb.BuildReceiver<Dish, DishEvents, string> statePerAggregate fallBackAggregateStateRetriever ea
                )
            consumer.add_ShutdownAsync
                (fun _ ea ->
                    task
                        {
                            logger.LogInformation($"Dish Consumer shutdown: {consumer.ShutdownReason}")
                            channel.Dispose()
                        }
                )
            
            channel.BasicConsumeAsync(queueDeclare.QueueName, true, consumer)    
        
        override this.Dispose () =
            channel.Dispose()