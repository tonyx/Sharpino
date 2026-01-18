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

open Sharpino.RabbitMq
open Tonyx.SeatsBooking.SeatRow
open Tonyx.SeatsBooking
open Tonyx.SeatsBooking.RowAggregateEvent

module RowConsumer =
    let streamName = SeatsRow.Version + SeatsRow.StorageName
    type RowConsumer (sp: IServiceProvider, logger: ILogger<RowConsumer>, rb: RabbitMqReceiver) =
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
            
        let consumer = AsyncEventingBasicConsumer channel
        
        do
            consumer.add_ReceivedAsync
                (fun _ ea ->
                    rb.BuildReceiver<SeatsRow, RowAggregateEvent, string> statePerAggregate fallBackAggregateStateRetriever ea
                )
            consumer.add_ShutdownAsync
                (fun _ ea ->
                    task
                        {
                            logger.LogInformation($"Row Consumer shutdown: {consumer.ShutdownReason}")
                            channel.Dispose()
                        }
                )
        
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
            channel.BasicConsumeAsync(queueDeclare.QueueName, true, consumer)
            
        override this.Dispose() =
            channel.Dispose()
                
                
                