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
open Sharpino.Sample._9.Course
open Sharpino.Sample._9.CourseEvents

module CourseConsumer =
    type CourseConsumer(sp: IServiceProvider, logger: ILogger<CourseConsumer>, rb: RabbitMqReceiver) =
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
            let streamName = Course.Course.Version + Course.Course.StorageName
            channel.QueueDeclareAsync (streamName, false, false, false, null)
            |> Async.AwaitTask
            |> Async.RunSynchronously
        
        let mutable fallBackAggregateStateRetriever: Option<AggregateViewer<Course.Course>>  =
            None
        
        let statePerAggregate =
            ConcurrentDictionary<AggregateId, EventId * Course.Course>()
            
        let resetFallbackAggregateStateRetriever () =
            fallBackAggregateStateRetriever <- None
       
        let resyncWithFallbackAggregateStateRetriever (id: AggregateId) =
            match fallBackAggregateStateRetriever  with
            | Some retriever ->
                match retriever id with
                | Result.Ok (eventId, state) ->
                    statePerAggregate.[id] <- (eventId, state)
                | Result.Error e ->
                    logger.LogError ("Error: {e}", e)
            | None ->
                logger.LogError "no fallback aggregate state retriever set" 
          
        let consumer = AsyncEventingBasicConsumer channel
            
        member this.SetFallbackAggregateStateRetriever (aggregateViewer: AggregateViewer<Course.Course>) =
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
        
        override this.ExecuteAsync (cancellationToken) =
            consumer.add_ReceivedAsync
                (fun _ ea ->
                    rb.BuildReceiver<Course, CourseEvents, string> statePerAggregate fallBackAggregateStateRetriever ea
                )
            consumer.add_ShutdownAsync
                (fun _ ea ->
                    task
                        {
                            logger.LogInformation($"Course Consumer shutdown: {consumer.ShutdownReason}")
                            channel.Dispose()
                        }
                )
            channel.BasicConsumeAsync (queueDeclare.QueueName, false, consumer)
        override this.Dispose () =
            channel.Dispose ()    
