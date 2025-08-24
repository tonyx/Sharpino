namespace Sharpino

open System
open System.Collections.Concurrent
open FsToolkit.ErrorHandling
open Microsoft.Extensions.Hosting
open Sharpino.Core
open Sharpino.Definitions
open Sharpino.EventBroker
open System.Text
open RabbitMQ.Client
open RabbitMQ.Client.Events
open System.Net

module RabbitMq =
    let mkfactory(host: string) =
        let factory = new ConnectionFactory()
        factory.HostName <- host
        factory
   
    let queueDeclare (channel: IChannel) (queueName: string) =
        try
            let queueDeclare =
                channel.QueueDeclareAsync(queueName, false, false, false, null)
                |> Async.AwaitTask
                |> Async.RunSynchronously
            Ok ()
        with
        | _ as ex ->
            ex |> Error
            
        
    let mkSimpleChannel(factory: ConnectionFactory, streamName: string): TaskResult<IChannel, exn> =
        taskResult
            {
                let! connection = factory.CreateConnectionAsync()
                let! channel = connection.CreateChannelAsync()
                let! qDec =
                    queueDeclare channel streamName
                return channel
            }

    let mkMessageSender(host: string) (streamName: string) =
        result {
            let factory = mkfactory(host)
            let channel =
                mkSimpleChannel(factory, streamName)
                |> Async.AwaitTask
                |> Async.RunSynchronously
                |> Result.get
            
            let aggregateMessageSender =
                fun (message: string) ->
                    let body = Encoding.UTF8.GetBytes message
                    channel.BasicPublishAsync(
                        "",
                        streamName,
                        body
                    )
            return aggregateMessageSender
       }
    
    let resyncWithFallBackAggregateStateRetriever
        (optStateViewer: Option<AggregateViewer<'A>>)
        (statesPerAggregate: ConcurrentDictionary<AggregateId, (EventId * 'A)>)
        (aggregateId: AggregateId)
        =
            match optStateViewer with
            | Some viewer ->
                let tryState = viewer aggregateId
                match tryState with
                | Ok (eventId, state) -> statesPerAggregate.[aggregateId] <- (eventId, state)
            | None ->
                // logerror
                ()
     
    let buildReceiver<'A, 'E, 'F
        when 'E :> Event<'A> and
        'A :> Aggregate<'F>>
        (statesPerAggregate: ConcurrentDictionary<AggregateId, (EventId * 'A)>)
        (optAggregateStateViewer: Option<AggregateViewer<'A>>)
        (ea: BasicDeliverEventArgs) 
        
        =
            task {
                let body = ea.Body.ToArray()
                let message = Encoding.UTF8.GetString(body)
                // logger.LogDebug ("ReceivedX {message}", message)
                let deserializedMessage = AggregateMessage<'A, 'E>.Deserialize message
                match deserializedMessage with
                | Ok message ->
                    let aggregateId = message.AggregateId
                    match message with
                    | { Message = InitialSnapshot good } ->
                        statesPerAggregate.[aggregateId] <- (0, good)
                        ()
                    | { Message = MessageType.Events { InitEventId = eventId; EndEventId = endEventId; Events = events  } }  ->
                        if (statesPerAggregate.ContainsKey aggregateId && (statesPerAggregate.[aggregateId] |> fst = eventId || statesPerAggregate.[aggregateId] |> fst = 0)) then
                            let currentState = statesPerAggregate.[aggregateId] |> snd
                            let newState = evolve currentState events
                            if newState.IsOk then
                                statesPerAggregate.[aggregateId] <- (endEventId, newState.OkValue)
                            else
                                let (Error e) = newState
                                // logger.LogError ("error {e}", e)
                                resyncWithFallBackAggregateStateRetriever optAggregateStateViewer statesPerAggregate aggregateId
                        else
                            resyncWithFallBackAggregateStateRetriever optAggregateStateViewer statesPerAggregate aggregateId
                    | { Message = MessageType.Delete } when statesPerAggregate.ContainsKey aggregateId ->
                        statesPerAggregate.TryRemove aggregateId  |> ignore
                    | { Message = MessageType.Delete }  ->
                        // logger.LogError ("deleting an unexisting aggregate: {aggregateId}", aggregateId)
                        ()
                | Error e ->
                    // logger.LogError ("Error: {e}", e)
                    ()
                return ()
           }
            
        
        
        
        
        
        