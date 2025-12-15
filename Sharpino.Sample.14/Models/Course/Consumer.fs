namespace Sharpino.Sample._14

open System
open System.Text
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Caching.Memory
open RabbitMQ.Client
open RabbitMQ.Client.Events
open Sharpino.Commons
open Sharpino.Definitions
open Sharpino.EventBroker
open Sharpino.Core

open Sharpino.RabbitMq
open Sharpino.Sample._14.Course
open Sharpino.Sample._14.CourseEvents

module CourseConsumer =
    type CourseConsumer(sp: IServiceProvider, logger: ILogger<CourseConsumer>, rb: RabbitMqReceiver2) =
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
            // I decided to not set any size so the GC will do the work for me
            new MemoryCache(MemoryCacheOptions())
            
        let resetFallbackAggregateStateRetriever () =
            fallBackAggregateStateRetriever <- None
       
        let resyncWithFallbackAggregateStateRetriever (id: AggregateId) =
            match fallBackAggregateStateRetriever  with
            | Some retriever ->
                match retriever id with
                | Result.Ok (eventId, state) ->
                    let entryOptions = MemoryCacheEntryOptions().SetSize(1L)
                    statePerAggregate.Set<(EventId * Course.Course)>(id, (eventId, state), entryOptions) |> ignore
                | Result.Error e ->
                    logger.LogError ("Error: {e}", e)
            | None ->
                logger.LogError "no fallback aggregate state retriever set" 
          
        let consumer = AsyncEventingBasicConsumer channel
        do
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
                    let entryOptions = MemoryCacheEntryOptions().SetSize(1L)
                    statePerAggregate.Set<(EventId * Course.Course)>(id, (eventId, state), entryOptions) |> ignore
                | Result.Error e ->
                    logger.LogError ("Error: {e}", e)
            | None ->
                logger.LogError "no fallback aggregate state retriever set"
                
        member this.ResetAllStates () =
            statePerAggregate.Compact(1.0) 
            
        member this.GetAggregateState (id: AggregateId) =
            let v = statePerAggregate.Get<(EventId * Course.Course)>(id)
            if not (obj.ReferenceEquals(v, null)) then v |> Result.Ok else Result.Error "No state"
        
        override this.ExecuteAsync (cancellationToken) =
            channel.BasicConsumeAsync (queueDeclare.QueueName, false, consumer)
            
        override this.Dispose () =
            channel.Dispose ()    
