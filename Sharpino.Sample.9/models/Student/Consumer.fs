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

open Sharpino.Sample._9.Student
open Sharpino.Sample._9.StudentEvents

module StudentConsumer =
    type StudentConsumer(sp: IServiceProvider, logger: ILogger<StudentConsumer>) =
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
            let streamName = Student.Student.Version + Student.Student.StorageName
            channel.QueueDeclareAsync (streamName, false, false, false, null)
            |> Async.AwaitTask
            |> Async.RunSynchronously
        
        let mutable fallBackAggregateStateRetriever: Option<AggregateViewer<Student.Student>>  =
            None
        
        let statePerAggregate =
            ConcurrentDictionary<AggregateId, EventId * Student.Student>()
            
        let setFallbackAggregateStateRetriever (aggregateViewer: AggregateViewer<Student.Student>) =
            fallBackAggregateStateRetriever <- Some aggregateViewer

        let resyncWithAggregateStateRetriever (id: AggregateId) =
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
                        let message = Encoding.UTF8.GetString(body)
                        logger.LogDebug ("Received {message}", message)
                        let deserializedMessage = AggregateMessage<Student.Student, StudentEvents>.Deserialize message
                        match deserializedMessage with
                        | Ok message ->
                            let aggregateId = message.AggregateId
                            match message with
                            | {Message = InitialSnapshot student} ->
                                statePerAggregate.[message.AggregateId] <- (0, student)
                            | {Message = MessageType.Events {InitEventId = eventId; EndEventId = endEventId; Events = events}} ->
                                let currentEventId = statePerAggregate.[message.AggregateId] |> fst
                                if currentEventId = eventId || currentEventId = 0 then
                                    let currentState = statePerAggregate.[message.AggregateId] |> snd
                                    let newState = evolve currentState events
                                    if newState.IsOk then
                                        statePerAggregate.[message.AggregateId] <- (endEventId, newState.OkValue)
                                    else
                                        let (Error e) = newState
                                        logger.LogError ("error {e}", e)
                                        resyncWithAggregateStateRetriever message.AggregateId
                                else
                                    resyncWithAggregateStateRetriever message.AggregateId
                            | {Message = MessageType.Delete} when statePerAggregate.ContainsKey message.AggregateId ->
                                statePerAggregate.TryRemove message.AggregateId  |> ignore
                            | {Message = MessageType.Delete}  ->
                                logger.LogError ("deleting an unexisting aggregate: {aggregateId}", message.AggregateId)
                        | Error e ->
                            logger.LogError ("error {e}", e)
                    }
                )
        
        member this.GetAggregateState (id: AggregateId) =
            if (statePerAggregate.ContainsKey id) then
                statePerAggregate.[id]
                |> Result.Ok
            else
                Result.Error "No state"        
        member this.SetFallbackAggregateStateRetriever (aggregateViewer: AggregateViewer<Student.Student>) =
            fallBackAggregateStateRetriever <- Some aggregateViewer
            
        member this.ResetFallbackAggregateStateRetriever () =
            fallBackAggregateStateRetriever <- None
            
        override this.ExecuteAsync cancellationToken =
            channel.BasicConsumeAsync(queueDeclare.QueueName, true, consumer)
            
        member this.ResetAllStates () =
            statePerAggregate.Clear() 
        