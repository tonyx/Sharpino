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

open Sharpino.Sample._9.Course
open Sharpino.Sample._9.CourseEvents

module CourseConsumer =
    type CourseConsumer(sp: IServiceProvider, logger: ILogger<CourseConsumer>) =
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
        
        do
            consumer.add_ReceivedAsync
                (fun _ ea ->
                    task {
                        let body = ea.Body.ToArray()
                        let message = Encoding.UTF8.GetString(body)
                        logger.LogDebug ("Received {message}", message)
                        let deserializedMessage = AggregateMessage<Course, CourseEvents>.Deserialize message
                        match deserializedMessage with
                        | Ok message ->
                            match message with
                            | {Message = InitialSnapshot course} ->
                                statePerAggregate.[message.AggregateId] <- (0, course)
                            | {Message = Message.Events {InitEventId = eventId; EndEventId = endEventId; Events = events}} ->
                                let currentEventId = statePerAggregate.[message.AggregateId] |> fst
                                if currentEventId = eventId || currentEventId = 0 then
                                    let currentState = statePerAggregate.[message.AggregateId] |> snd
                                    let newState = evolve currentState events
                                    if newState.IsOk then
                                        statePerAggregate.[message.AggregateId] <- (endEventId, newState.OkValue)
                                    else
                                        let (Error e) = newState
                                        logger.LogError ("error {e}", e)
                                        resyncWithFallbackAggregateStateRetriever message.AggregateId
                                else
                                    resyncWithFallbackAggregateStateRetriever message.AggregateId
                            | {Message = Message.Delete} when statePerAggregate.ContainsKey message.AggregateId ->
                                statePerAggregate.TryRemove message.AggregateId  |> ignore
                            | {Message = Message.Delete}  ->
                                logger.LogError ("deleting an unexisting aggregate: {aggregateId}", message.AggregateId)
                        | Error e ->
                            logger.LogError ("Error: {e}", e)    
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
                    statePerAggregate.[id] <- (eventId, state)
                | Result.Error e ->
                    logger.LogError ("Error: {e}", e)
            | None ->
                logger.LogError "no fallback aggregate state retriever set"
            
        member this.GetAggregateState (id: AggregateId) =
            if (statePerAggregate.ContainsKey id) then
                statePerAggregate.[id]  
                |> Result.Ok
            else
                Result.Error "No state"
        
        override this.ExecuteAsync (cancellationToken) =
            channel.BasicConsumeAsync (queueDeclare.QueueName, false, consumer)
            
        member this.ResetAllStates () =
            statePerAggregate.Clear() 
