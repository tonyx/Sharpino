
namespace Shaprino.Sample._11
open System
open System.Collections.Concurrent
open System.Text
open System.Threading
open Microsoft.Extensions.Caching.Memory
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open RabbitMQ.Client
open RabbitMQ.Client.Events
open Sharpino.Commons
open Sharpino.Definitions
open Sharpino.EventBroker
open Sharpino.Core

open Sharpino.RabbitMq
open Sharpino.Sample._11
open Sharpino.Sample._11.Student2
open Sharpino.Sample._11.StudentEvents2
module StudentConsumer2 =
    type StudentConsumer2(sp: IServiceProvider, logger: ILogger<StudentConsumer2>, rb: RabbitMqReceiver2) =
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
            let streamName = Student2.Version + Student2.StorageName
            channel.QueueDeclareAsync (streamName, false, false, false, null)
            |> Async.AwaitTask
            |> Async.RunSynchronously
        
        let mutable fallBackAggregateStateRetriever: Option<AggregateViewer<Student2>> =
            None
        
        let statePerAggregate =
            new MemoryCache(MemoryCacheOptions())
       
        let setFallBackAggregateStateRetriever (aggregateViewer: AggregateViewer<Student2>) =
            fallBackAggregateStateRetriever <- Some aggregateViewer
            
        let resetFallbackAggregateStateRetriever () =
            fallBackAggregateStateRetriever <- None
            
        let consumer = AsyncEventingBasicConsumer channel
        do
            consumer.add_ReceivedAsync
                (fun _ ea ->
                    rb.BuildReceiver<Student2, StudentEvents2, string> statePerAggregate fallBackAggregateStateRetriever ea
                )
                
            consumer.add_ShutdownAsync
                (fun _ ea ->
                    task
                        {
                            logger.LogInformation($"Student Consumer shutdown: {consumer.ShutdownReason}")
                            channel.Dispose()
                        }
                )
            
        let resyncWithFallbackAggregateStateRetriever (id: AggregateId) =
            match fallBackAggregateStateRetriever  with
            | Some retriever ->
                match retriever id with
                | Result.Ok (eventId, state) ->
                    let entryOptions = MemoryCacheEntryOptions().SetSize(1L)
                    statePerAggregate.Set<(EventId * Student2)>(id, (eventId, state), entryOptions) |> ignore
                | Result.Error e ->
                    logger.LogError ("Error: {e}", e)
            | None ->
                logger.LogError "no fallback aggregate state retriever set"
                
        member this.SetFallbackAggregateStateRetriever (aggregateViewer: AggregateViewer<Student2>) =
            setFallBackAggregateStateRetriever aggregateViewer        
        member this.ResetAllStates () =
            statePerAggregate.Clear()
        
        member this.GetAggregateState (id: AggregateId) =
            let v = statePerAggregate.Get<(EventId * Student2)>(id)
            if not (obj.ReferenceEquals(v, null)) then v |> Result.Ok else Result.Error "No state"
        
        member this.GetAllAggregateStates () =
            statePerAggregate.Keys
            |> Seq.map (fun x -> statePerAggregate.Get<(EventId * Student2)>(x))
            |> List.ofSeq
            |> Result.Ok

        override this.ExecuteAsync (stoppingToken: CancellationToken) =
            channel.BasicConsumeAsync (queueDeclare.QueueName, false, consumer)
