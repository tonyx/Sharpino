namespace Tonyx.Sharpino.Pub

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
open System
open System.Collections.Concurrent
open System.Text
open Sharpino.RabbitMq
open Tonyx.Sharpino.Pub.Commons

module IngredientConsumer =
    type IngredientConsumer(sp: IServiceProvider, logger: ILogger<IngredientConsumer>, rb: RabbitMqReceiver) =
        inherit BackgroundService()
        let factory = ConnectionFactory(HostName = "localhost")
        let connection =
            factory.CreateConnectionAsync()
            |> Async.AwaitTask
            |> Async.RunSynchronously
        let channel =
            connection.CreateChannelAsync ()
            |> Async.AwaitTask
            |> Async.RunSynchronously
        let queueDeclare =
            let queueName = Ingredient.Version + Ingredient.StorageName
            channel.QueueDeclareAsync (queueName, false, false, false, null)
            |> Async.AwaitTask
            |> Async.RunSynchronously
            
        let mutable fallBackAggregateStateRetriever: Option<AggregateViewer<Ingredient>>  =
            None
        
        let statePerAggregate =
            ConcurrentDictionary<AggregateId, EventId * Ingredient>()
        let resyncWithFallbackAggregateStateRetriever (id: AggregateId) =
            match fallBackAggregateStateRetriever  with
            | Some retriever ->
                match retriever id with
                | Result.Ok (eventId, state) ->
                    statePerAggregate.[id] <- (eventId, state)
                | _ -> ()
            | None -> ()    
        let consumer = AsyncEventingBasicConsumer(channel)
        
        member this.SetFallbackAggregateStateRetriever (aggregateViewer: AggregateViewer<Ingredient>) =
            fallBackAggregateStateRetriever <- Some aggregateViewer    
        
        member this.GetAggregateState (id: AggregateId) =
            if (statePerAggregate.ContainsKey id) then
                statePerAggregate.[id]
                |> Result.Ok
            else
                Result.Error "No state" 
        
        override this.ExecuteAsync (cancellationToken) =
            consumer.add_ReceivedAsync 
                (fun _ ea ->
                    rb.BuildReceiver<Ingredient, IngredientEvents, string> statePerAggregate fallBackAggregateStateRetriever ea
                )
            consumer.add_ShutdownAsync
                (fun _ ea ->
                    task
                        {
                            logger.LogInformation($"Ingredient Consumer shutdown: {consumer.ShutdownReason}")
                            channel.Dispose()
                        }
                )
            channel.BasicConsumeAsync(queueDeclare.QueueName, true, consumer)    
        override this.Dispose () =
            channel.Dispose()    
            