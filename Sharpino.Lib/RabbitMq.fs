namespace Sharpino

open System
open System.Collections.Concurrent
open System.Threading.Tasks
open FsToolkit.ErrorHandling
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
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
   
    let optionallySendAggregateEventsAsync<'A, 'E when 'E :> Event<'A>>
        (queueName: StreamName)
        (messageSenders: MessageSenders)
        (aggregateId: AggregateId)
        (events: List<'E>)
        (initEventId: EventId)
        (endEventId: EventId)
        =
        match messageSenders with
        | MessageSenders.MessageSender messageSender ->
            let sender = messageSender queueName
            let message =
                MessageType<'A, 'E>.Events
                    {
                        InitEventId = initEventId
                        EndEventId = endEventId
                        Events = events
                    }
            let aggregateMessage =
                {
                  AggregateId = aggregateId
                  Message = message
                }.Serialize
            sender aggregateMessage
        | _ ->
            ValueTask.CompletedTask
   
    let optionallySendMultipleAggregateEventsAsync<'A, 'E when 'E :> Event<'A>>
        (queueName: StreamName)
        (messageSenders: MessageSenders)
        (aggregateIdsInitEventIdEndEventIdAndEvents: List<AggregateId * EventId * EventId * List<'E>>)
        =
        aggregateIdsInitEventIdEndEventIdAndEvents
        |> List.iter (fun (aggregateId, initEventId, endEventId, events) ->
            let _ = optionallySendAggregateEventsAsync<'A, 'E> queueName messageSenders aggregateId events initEventId endEventId
            ()
        )
     
    let optionallySendInitialInstanceAsync<'A, 'E when 'E :> Event<'A>>
        (queueName: string)
        (messageSenders: MessageSenders)
        (aggregateId: AggregateId)
        (initialInstance: 'A)
        =
        match messageSenders with
        | MessageSenders.MessageSender messageSender ->
            let sender = messageSender queueName // todo handle lookup error using option or result
            let message =
                MessageType<'A, 'E>.InitialSnapshot initialInstance
            let aggregateMessage =
                {
                    AggregateId = aggregateId
                    Message = message
                }.Serialize
            sender aggregateMessage
        | _ ->
            ValueTask.CompletedTask
            
    let optionallySendDeleteMessageAsync<'A>
        (queueName: StreamName)
        (messageSenders: MessageSenders)
        (aggregateId: AggregateId)
        =
        match messageSenders with
        | MessageSenders.MessageSender messageSender ->
            let sender = messageSender queueName // todo handle lookup error using option or result
            let message =
                MessageType<'A, _>.Delete
            let aggregateMessage =
                {
                    AggregateId = aggregateId
                    Message = message
                }.Serialize
            sender aggregateMessage    
        | _ ->
            ValueTask.CompletedTask
        
    type RabbitMqReceiver (logger: ILogger<RabbitMqReceiver>) =
        member private this.ResyncWithFallbackAggregateStateRetriever
            (optStateViewer: Option<AggregateViewer<'A>>)
            (statesPerAggregate: ConcurrentDictionary<AggregateId, (EventId * 'A)>)
            (aggregateId: AggregateId)
            =
                match optStateViewer with
                | Some viewer ->
                    let tryState = viewer aggregateId
                    match tryState with
                    | Ok (eventId, state) -> statesPerAggregate.[aggregateId] <- (eventId, state)
                    | Error e ->
                        logger.LogError ("Error: {e}", e)
                | None ->
                    // logerror
                    ()
        
        member this.BuildReceiver<'A, 'E, 'F
            when 'E :> Event<'A> and
            'A :> Aggregate<'F>>
            (statesPerAggregate: ConcurrentDictionary<AggregateId, (EventId * 'A)>)
            (optAggregateStateViewer: Option<AggregateViewer<'A>>)
            (ea: BasicDeliverEventArgs) =
                task {
                    let body = ea.Body.ToArray()
                    let message = Encoding.UTF8.GetString(body)
                    logger.LogDebug $"Received {message}"
                    let deserializedMessage = AggregateMessage<'A, 'E>.Deserialize message
                    match deserializedMessage with
                    | Ok { Message = InitialSnapshot good; AggregateId = aggregateId } ->
                        statesPerAggregate.[aggregateId] <- (0, good)
                        ()
                    | Ok { Message = MessageType.Events { InitEventId = initEventId; EndEventId = endEventId; Events = events  }; AggregateId = aggregateId }
                        when (statesPerAggregate.ContainsKey aggregateId && (statesPerAggregate.[aggregateId] |> fst = initEventId || statesPerAggregate.[aggregateId] |> fst = 0)) ->
                            let currentState = statesPerAggregate.[aggregateId] |> snd
                            let newState = evolve currentState events
                            if newState.IsOk then
                                statesPerAggregate.[aggregateId] <- (endEventId, newState.OkValue)
                            else
                                let (Error e) = newState
                                logger.LogError ("error {e}", e)
                                this.ResyncWithFallbackAggregateStateRetriever optAggregateStateViewer statesPerAggregate aggregateId
                    | Ok { Message = MessageType.Events {InitEventId = initEventId; EndEventId = endEventId}; AggregateId = aggregateId } ->
                        logger.LogError ("events disalignments for aggregate: {aggregateId}", aggregateId)
                        this.ResyncWithFallbackAggregateStateRetriever optAggregateStateViewer statesPerAggregate aggregateId
                    | Ok { Message = MessageType.Delete; AggregateId = aggregateId } when statesPerAggregate.ContainsKey aggregateId ->
                            statesPerAggregate.TryRemove aggregateId  |> ignore
                    | Ok { Message = MessageType.Delete; AggregateId = aggregateId }  ->
                        logger.LogError ("deleting an unexisting aggregate: {aggregateId}", aggregateId)
                    | Error e ->
                        logger.LogError ("Error: {e}", e)
                    return ()
               }
                
    
            
        
        
        
        
        
        