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
open Sharpino.Sample._9.Reservation
open Sharpino.Sample._9.ReservationEvents

module ReservationConsumer =
    type ReservationConsumer(sp: IServiceProvider, logger: ILogger<ReservationConsumer>, rb: RabbitMqReceiver) =
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
            let streamName = Reservation.Reservation.Version + Reservation.Reservation.StorageName
            channel.QueueDeclareAsync (streamName, false, false, false, null)
            |> Async.AwaitTask
            |> Async.RunSynchronously
        
        let mutable fallBackAggregateStateRetriever: Option<AggregateViewer<Reservation.Reservation>>  =
            None
        
        let statePerAggregate =
            ConcurrentDictionary<AggregateId, EventId * Reservation.Reservation>()
            
        let resetFallbackAggregateStateRetriever () =
            fallBackAggregateStateRetriever <- None
        
        let setFallbackAggregateStateRetriever (aggregateViewer: AggregateViewer<Reservation.Reservation>) =
            fallBackAggregateStateRetriever <- Some aggregateViewer
     
        let resyncWithFallbackAggregateStateRetriever (id: AggregateId) =
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
         
        let consumer = AsyncEventingBasicConsumer channel
         
        member this.SetFallbackAggregateStateRetriever (aggregateViewer: AggregateViewer<Reservation.Reservation>) =
            setFallbackAggregateStateRetriever aggregateViewer
        
        member this.ResetFallbackAggregateStateRetriever () =
            fallBackAggregateStateRetriever <- None
      
        member this.GetAggregateState (id: AggregateId) =
            if (statePerAggregate.ContainsKey id) then
                statePerAggregate.[id]
                |> Result.Ok
            else
                Result.Error "No state" 
          
        override this.ExecuteAsync cancellationToken =
            consumer.add_ReceivedAsync
                (fun _ ea ->
                    rb.BuildReceiver<Reservation, ReservationEvents, string> statePerAggregate fallBackAggregateStateRetriever ea
                )
            consumer.add_ShutdownAsync
                (fun _ ea ->
                    task
                        {
                            logger.LogInformation($"Reservation Consumer shutdown: {consumer.ShutdownReason}")
                            channel.Dispose()
                        }
                )
            channel.BasicConsumeAsync(queueDeclare.QueueName, true, consumer)
            
        member this.ResetAllStates () =
            statePerAggregate.Clear() 
        
        member this.Dispose () =
            channel.Dispose () 
