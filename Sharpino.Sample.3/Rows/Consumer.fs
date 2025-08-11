namespace Tonyx.SeatsBooking


open System.Threading
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

open Tonyx.SeatsBooking.SeatRow
open Tonyx.SeatsBooking.RowAggregateEvent

module RowConsumer =
    let streamName = SeatsRow.Version + SeatsRow.StorageName
    type RowConsumer (sp: IServiceProvider, logger: ILogger<RowConsumer>) =
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
            channel.QueueDeclareAsync (streamName, false, false, false, null)
            |> Async.AwaitTask
            |> Async.RunSynchronously

        let mutable fallBackAggregateStateRetriever: Option<AggregateViewer<SeatsRow>>  =
            None

        let statePerAggregate =
            ConcurrentDictionary<AggregateId, EventId * SeatsRow>()
            
        member this.SetFallbackAggregateStateRetriever (aggregateStateRetriever: AggregateViewer<SeatsRow>) =
            fallBackAggregateStateRetriever <- Some aggregateStateRetriever
        
        member this.GetAggregateState (id: AggregateId) =
            if (statePerAggregate.ContainsKey id) then
                statePerAggregate.[id]
                |> Result.Ok
            else
                Result.Error $"No state for aggregate {streamName} with id {id}"
        
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
       
        override this.ExecuteAsync (cancellationToken: CancellationToken) =
            let consumer = AsyncEventingBasicConsumer channel
            consumer.add_ReceivedAsync
                (fun _ ea ->
                    task {
                        let body = ea.Body.ToArray()
                        let message = Encoding.UTF8.GetString(body)
                        logger.LogDebug ("Received {message}", message)
                        let deserializedMessage = jsonPSerializer.Deserialize<AggregateMessage<SeatsRow, RowAggregateEvent>> message
                        match deserializedMessage with
                        | Result.Ok message ->
                            let aggregateId = message.AggregateId
                            match message with
                            | { Message = InitialSnapshot good } ->
                                statePerAggregate.[aggregateId] <- (0, good)
                                ()
                            | { Message = Message.Events { InitEventId = eventId; EndEventId = endEventId; Events = events  } }  ->
                                if (statePerAggregate.ContainsKey aggregateId && (statePerAggregate.[aggregateId] |> fst = eventId || statePerAggregate.[aggregateId] |> fst = 0)) then
                                    let currentState = statePerAggregate.[aggregateId] |> snd
                                    let newState = evolve currentState events
                                    if newState.IsOk then
                                        statePerAggregate.[aggregateId] <- (endEventId, newState.OkValue)
                                    else
                                        let (Error e) = newState
                                        logger.LogError ("error {e}", e)
                                        this.ResyncWithFallbackAggregateStateRetriever aggregateId
                                else
                                    this.ResyncWithFallbackAggregateStateRetriever aggregateId
                            | {Message = Message.Delete } when (statePerAggregate.ContainsKey aggregateId) ->
                                statePerAggregate.TryRemove aggregateId  |> ignore
                            | {Message = Message.Delete } ->
                                logger.LogError ("deleting an unexisting aggregate: {aggregateId}", aggregateId)
                        | Error e ->
                            logger.LogError ("error {e}", e)
                            
                        return ()
                    }
               ) 
            channel.BasicConsumeAsync(queueDeclare.QueueName, true, consumer)        
                
                
                
                