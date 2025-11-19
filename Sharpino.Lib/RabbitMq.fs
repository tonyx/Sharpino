namespace Sharpino

open System
open System.Collections.Concurrent
open System.Linq
open System.Threading.Tasks
open FsToolkit.ErrorHandling
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Caching.Memory

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
        | MessageSenders.MessageSender messageSender when (messageSender queueName |> Result.isOk) ->
            let sender = messageSender queueName |> Result.get
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
        | MessageSenders.MessageSender messageSender when (messageSender queueName |> Result.isOk) ->
            let sender = messageSender queueName |> Result.get 
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
        | MessageSenders.MessageSender messageSender when (messageSender queueName |> Result.isOk) ->
            let sender = messageSender queueName |> Result.get
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
        
    // this will become obsolete in that the replacement, RabbitMqReceiver2 uses MemoryCache instead of ConcurrentDictionary    
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
                    logger.LogInformation ($"No fallback state viewer is set for aggregateId {aggregateId}", aggregateId)
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
                        when
                            // todo: there are still some corner cases where this index match fails (and it shouldn't) leaving control to the "ResyncWithFallbackAggregateStateRetriever" next pattern
                            (statesPerAggregate.ContainsKey aggregateId && (statesPerAggregate.[aggregateId] |> fst = initEventId || statesPerAggregate.[aggregateId] |> fst = 0)) ->
                            let currentState = statesPerAggregate.[aggregateId] |> snd
                            let newState = evolve currentState events
                            if newState.IsOk then
                                statesPerAggregate.[aggregateId] <- (endEventId, newState.OkValue)
                            else
                                let (Error e) = newState
                                logger.LogError ("error {e}", e)
                                this.ResyncWithFallbackAggregateStateRetriever optAggregateStateViewer statesPerAggregate aggregateId
                    | Ok { Message = MessageType.Events e; AggregateId = aggregateId } ->
                        logger.LogError ("events indexes unalignments for aggregate: {aggregateId}, unexpected indexes in message {e}", aggregateId, e)
                        this.ResyncWithFallbackAggregateStateRetriever optAggregateStateViewer statesPerAggregate aggregateId
                    | Ok { Message = MessageType.Delete; AggregateId = aggregateId } when statesPerAggregate.ContainsKey aggregateId ->
                            statesPerAggregate.TryRemove aggregateId  |> ignore
                    | Ok { Message = MessageType.Delete; AggregateId = aggregateId }  ->
                        logger.LogError ("deleting an unexisting aggregate: {aggregateId}", aggregateId)
                    | Error e ->
                        logger.LogError ("Error: {e}", e)
                    return ()
               }
                
    // same as RabbitMqReceiver using MemoryCache rather than ConcurrentDictionary    
    type RabbitMqReceiver2 (logger: ILogger<RabbitMqReceiver>) =
        member private this.ResyncWithFallbackAggregateStateRetriever
            (optStateViewer: Option<AggregateViewer<'A>>)
            (statesPerAggregate: MemoryCache)
            (aggregateId: AggregateId)
            =
                match optStateViewer with
                | Some viewer ->
                    let tryState = viewer aggregateId
                    match tryState with
                    | Ok (eventId, state) ->
                        let entryOptions = MemoryCacheEntryOptions().SetSize(1L)
                        statesPerAggregate.Set<(EventId * 'A)>(aggregateId, (eventId, state), entryOptions) |> ignore
                    | Error e ->
                        logger.LogError ("Error: {e}", e)
                | None ->
                    logger.LogInformation ($"No fallback state viewer is set for aggregateId {aggregateId}", aggregateId)
                    ()
        
        member this.BuildReceiver<'A, 'E, 'F
            when 'E :> Event<'A> and
            'A :> Aggregate<'F>>
            (statesPerAggregate: MemoryCache)
            (optAggregateStateViewer: Option<AggregateViewer<'A>>)
            (ea: BasicDeliverEventArgs) =
                task {
                    let body = ea.Body.ToArray()
                    let message = Encoding.UTF8.GetString(body)
                    logger.LogDebug $"Received {message}"
                    let deserializedMessage = AggregateMessage<'A, 'E>.Deserialize message
                    match deserializedMessage with
                    | Ok { Message = InitialSnapshot good; AggregateId = aggregateId } ->
                        let entryOptions = MemoryCacheEntryOptions().SetSize(1L)
                        statesPerAggregate.Set<(EventId * 'A)>(aggregateId, (0, good), entryOptions) |> ignore
                        ()
                    | Ok { Message = MessageType.Events { InitEventId = initEventId; EndEventId = endEventId; Events = events  }; AggregateId = aggregateId }
                        when
                            // todo: there are still some corner cases where this index match fails (and it shouldn't) leaving control to the "ResyncWithFallbackAggregateStateRetriever" next pattern
                            (let v = statesPerAggregate.Get<(EventId * 'A)>(aggregateId)
                             not (obj.ReferenceEquals(v, null)) && ((v |> fst) = initEventId || (v |> fst) = 0)) ->
                            let currentState = (statesPerAggregate.Get<(EventId * 'A)>(aggregateId)) |> snd
                            let newState = evolve currentState events
                            if newState.IsOk then
                                let entryOptions = MemoryCacheEntryOptions().SetSize(1L)
                                statesPerAggregate.Set<(EventId * 'A)>(aggregateId, (endEventId, newState.OkValue), entryOptions) |> ignore
                            else
                                let (Error e) = newState
                                logger.LogError ("error {e}", e)
                                this.ResyncWithFallbackAggregateStateRetriever optAggregateStateViewer statesPerAggregate aggregateId
                    | Ok { Message = MessageType.Events e; AggregateId = aggregateId } ->
                        logger.LogError ("events indexes unalignments for aggregate: {aggregateId}, unexpected indexes in message {e}", aggregateId, e)
                        this.ResyncWithFallbackAggregateStateRetriever optAggregateStateViewer statesPerAggregate aggregateId
                    | Ok { Message = MessageType.Delete; AggregateId = aggregateId } when not (obj.ReferenceEquals(statesPerAggregate.Get<(EventId * 'A)>(aggregateId), null)) ->
                            statesPerAggregate.Remove aggregateId
                    | Ok { Message = MessageType.Delete; AggregateId = aggregateId }  ->
                        logger.LogError ("deleting an unexisting aggregate: {aggregateId}", aggregateId)
                    | Error e ->
                        logger.LogError ("Error: {e}", e)
                    return ()
               }
                
        
        
        
        
        