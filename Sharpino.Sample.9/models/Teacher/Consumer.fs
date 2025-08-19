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

open Sharpino.Sample._9.Teacher
open Sharpino.Sample._9.TeacherEvents

module TeacherConsumer =
    type TeacherConsumer(sp: IServiceProvider, logger: ILogger<TeacherConsumer>) =
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
            let streamName = Teacher.Teacher.Version + Teacher.Teacher.StorageName
            channel.QueueDeclareAsync (streamName, false, false, false, null)
            |> Async.AwaitTask
            |> Async.RunSynchronously
        
        let mutable fallBackAggregateStateRetriever: Option<AggregateViewer<Teacher.Teacher>>  =
            None
        
        let statePerAggregate =
            ConcurrentDictionary<AggregateId, EventId * Teacher.Teacher>()
            
        let resetFallbackAggregateStateRetriever () =
            fallBackAggregateStateRetriever <- None
        
        let resyncFallbackAggregateStateRetriever (id: AggregateId) =
            match fallBackAggregateStateRetriever with
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
                        let message = Encoding.UTF8.GetString (body, 0, body.Length)
                        logger.LogDebug ("Received {message}", message)
                        let deserializedMessage = AggregateMessage<Teacher, TeacherEvents>.Deserialize message
                        match deserializedMessage with
                        | Ok message ->
                            let aggregateId = message.AggregateId
                            match message with
                            | {Message = InitialSnapshot reservation} ->
                                statePerAggregate.[aggregateId] <- (0, reservation)
                            | {Message = Message.Events {InitEventId = eventId; EndEventId = endEventId; Events = events}} ->
                                if (statePerAggregate.ContainsKey aggregateId && (statePerAggregate.[aggregateId] |> fst = eventId || statePerAggregate.[aggregateId] |> fst = 0)) then
                                    let currentState = statePerAggregate.[aggregateId] |> snd
                                    let newState = evolve currentState events
                                    if newState.IsOk then
                                        statePerAggregate.[aggregateId] <- (endEventId, newState.OkValue)
                                        ()
                                    else
                                        let (Error e) = newState
                                        logger.LogError ("error {e}", e)
                                        resyncFallbackAggregateStateRetriever aggregateId
                                        ()
                                else
                                    resyncFallbackAggregateStateRetriever aggregateId
                                    ()
                            | {Message = Message.Delete} when statePerAggregate.ContainsKey aggregateId ->
                                statePerAggregate.TryRemove aggregateId  |> ignore
                            | {Message = Message.Delete}  ->
                                logger.LogError ("deleting an unexisting aggregate: {aggregateId}", aggregateId)
                        | Error e ->
                            logger.LogError ("Error: {e}", e)
                    }
                )
       
        member this.GetAggregateState (id: AggregateId) =
            if (statePerAggregate.ContainsKey id) then
                statePerAggregate.[id] |> Result.Ok
            else
                Result.Error $"No state for aggregate {id}, in stream {Teacher.Teacher.Version + Teacher.Teacher.StorageName}"
        member this.SetFallbackAggregateStateRetriever (aggregateViewer: AggregateViewer<Teacher.Teacher>) =
            fallBackAggregateStateRetriever <- Some aggregateViewer
            
        member this.ResetFallbackAggregateStateRetriever () =
            fallBackAggregateStateRetriever <- None
        
        override this.ExecuteAsync cancellationToken =
            channel.BasicConsumeAsync(queueDeclare.QueueName, true, consumer)    
            
        member this.ResetAllStates () =
            statePerAggregate.Clear() 
