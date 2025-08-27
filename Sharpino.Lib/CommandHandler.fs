
namespace Sharpino

open System

open FSharp.Core
open FSharpPlus

open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions
open Sharpino.Cache
open Sharpino.Core
open Sharpino.Storage
open Sharpino.Definitions
open Sharpino.StateView
open Sharpino.EventBroker

open FsToolkit.ErrorHandling

// the "md" version of any function is the one that takes a metadata parameter
// the md requires an extra text md field in any event and a proper new funcion on the db side
// like  insert_md{Version}{AggregateStorageName}_aggregate_event_and_return_id
// I rather duplicate the code than make it more complex
// after all what we are going for is leaving only the md version and keep the
// non-md only for backward compatibility
module CommandHandler =
    type StramName = string

    // will play around DI to improve logging
    // let host = Host.CreateApplicationBuilder().Build()
    //
    // let factory: ILoggerFactory = LoggerFactory.Create(fun builder -> builder.AddConsole() |> ignore)
    
    type PreExecutedAggregateCommand<'A, 'F> =
        {
           AggregateId: Guid
           NewState: obj 
           EventId: EventId
           SerializedEvents: List<'F>
           // Events: List<Event<'A>>
           Metadata: Metadata
           Version: string
           StorageName: string
           SnapshotsInterval: int
           EventType: Type
        }
                
    type UnitResult = ((unit -> unit) * AsyncReplyChannel<unit>)
   
    let logger: Microsoft.Extensions.Logging.ILogger ref = ref NullLogger.Instance
    let setLogger (newLogger: ILogger) =
        logger := newLogger
    
    // this is not used anymore, but just in case 
    let processor = MailboxProcessor<UnitResult>.Start (fun inbox  ->
        let rec loop() =
            async {
                let! (statement, replyChannel) = inbox.Receive()
                let result = statement()
                replyChannel.Reply result
                do! loop()
            }
        loop()
    )
    let postToProcessor f =
        Async.RunSynchronously(processor.PostAndAsyncReply(fun rc -> f, rc), Commons.generalAsyncTimeOut)

    let inline getStorageFreshStateViewer<'A, 'E, 'F
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (member Serialize: 'F)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'E:> Event<'A>
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'E: (member Serialize: 'F)
        >(eventStore: IEventStore<'F>) =
            fun () -> getFreshState<'A, 'E, 'F> eventStore

    let inline getAggregateStorageFreshStateViewer<'A, 'E, 'F
        when 'A :> Aggregate<'F> 
        and 'A : (static member Deserialize: 'F -> Result<'A, string>) 
        and 'A : (static member StorageName: string) 
        and 'A : (static member Version: string) 
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        >
        (eventStore: IEventStore<'F>) 
        =
            fun (id: Guid) ->
                result
                    {
                        let! (eventId, result) = getAggregateFreshState<'A, 'E, 'F> id eventStore
                        return
                            (eventId, result :?> 'A) 
                    }

    let config =
        try
            Conf.config ()
        with
        | :? _ as ex -> 
            logger.Value.LogError (sprintf "appSettings.json file not found using defult!!! %A\n" ex)
            printf "appSettings.json file not found using defult!!! %A\n" ex
            Conf.defaultConf

    let inline mkSnapshot<'A, 'E, 'F
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (member Serialize: 'F )
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'E: (member Serialize: 'F)
        > 
        (storage: IEventStore<'F>) =
            let stateViewer = getStorageFreshStateViewer<'A, 'E, 'F> storage
            logger.Value.LogDebug (sprintf "mkSnapshot %A %A" 'A.Version 'A.StorageName)
            Async.RunSynchronously(
                async {
                    return
                        result
                            {
                                let! (id, state) = stateViewer ()
                                let serState = state.Serialize
                                let! result = storage.SetSnapshot 'A.Version (id, serState) 'A.StorageName
                                return result 
                            }
                }, Commons.generalAsyncTimeOut)
    
    let inline mkAggregateSnapshot<'A, 'E, 'F
        when 'A :> Aggregate<'F> 
        and 'E :> Event<'A>
        and 'A : (static member Deserialize: 'F -> Result<'A, string>) 
        and 'A : (static member StorageName: string) 
        and 'A : (static member Version: string) 
        and 'E : (static member Deserialize: 'F -> Result<'E, string>)
        and 'E : (member Serialize: 'F)
        > 
        (storage: IEventStore<'F>) 
        (aggregateId: AggregateId) =
            logger.Value.LogDebug (sprintf "mkAggregateSnapshot %A" aggregateId)
            let stateViewer = getAggregateStorageFreshStateViewer<'A, 'E, 'F> storage
            Async.RunSynchronously 
                (async {
                    return
                        result
                            {
                                let! (eventId, state) = stateViewer aggregateId 
                                let serState = state.Serialize 
                                let result = storage.SetAggregateSnapshot 'A.Version (aggregateId, eventId, serState) 'A.StorageName
                                return! result 
                            }
                }, Commons.generalAsyncTimeOut)
                
    let inline mkSnapshotIfIntervalPassed2<'A, 'E, 'F
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (static member SnapshotsInterval : int)
        and 'A: (member Serialize: 'F)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'E: (member Serialize: 'F)
        >
        (storage: IEventStore<'F>)
        (state: 'A)
        (eventId: int)
        =
            logger.Value.LogDebug "mkSnapshotIfIntervalPassed"
            Async.RunSynchronously
                (async {
                    return
                        result
                            {
                                let! lastEventId = 
                                    storage.TryGetLastEventId 'A.Version 'A.StorageName 
                                    |> Result.ofOption "lastEventId is None"
                                let snapEventId = storage.TryGetLastSnapshotEventId 'A.Version 'A.StorageName |> Option.defaultValue 0
                                return! 
                                    if (lastEventId - snapEventId) > 'A.SnapshotsInterval || snapEventId = 0 then
                                        let result = storage.SetSnapshot 'A.Version (eventId, state.Serialize) 'A.StorageName
                                        result
                                    else
                                        () |> Ok
                            }
                }, Commons.generalAsyncTimeOut)    
   
    let inline mkAggregateSnapshotIfIntervalPassed2<'A, 'E, 'F
        when 'A :> Aggregate<'F> 
        and 'E :> Event<'A>
        and 'A : (static member Deserialize: 'F -> Result<'A, string>) 
        and 'A : (static member StorageName: string)
        and 'A : (static member SnapshotsInterval : int)
        and 'A : (static member Version: string) 
        and 'E : (static member Deserialize: 'F -> Result<'E, string>)
        and 'E : (member Serialize: 'F)
        >
        (storage: IEventStore<'F>)
        (aggregateId: AggregateId)
        (state: 'A)
        (eventId: int)
        =
            logger.Value.LogDebug "mkAggregateSnapshotIfIntervalPassed"
            Async.RunSynchronously
                (async {
                    return
                        result
                            {
                                let lastEventId = 
                                    storage.TryGetLastAggregateEventId 'A.Version 'A.StorageName aggregateId
                                    |> Option.defaultValue 0
                                let snapEventId = storage.TryGetLastAggregateSnapshotEventId 'A.Version 'A.StorageName aggregateId |> Option.defaultValue 0
                                let result =
                                    if (lastEventId - snapEventId) >= 'A.SnapshotsInterval then
                                        storage.SetAggregateSnapshot 'A.Version (aggregateId, eventId, state.Serialize) 'A.StorageName
                                    else
                                        () |> Ok
                                return! result
                            }
                }, Commons.generalAsyncTimeOut)
                
    let inline mkAggregateSnapshotIfIntervalPassed3<'F>
        (storage: IEventStore<'F>)
        (aggregateId: AggregateId)
        (storageVersion: string)
        (storageName: string)
        (eventId: EventId)
        (snapshotInterval: int)
        (state: 'F)  =
            logger.Value.LogDebug "mkAggregateSnapshotIfIntervalPassed"
            Async.RunSynchronously
                (async {
                    return
                        result
                            {
                                let lastEventId = 
                                    storage.TryGetLastAggregateEventId storageVersion storageName aggregateId
                                    |> Option.defaultValue 0
                                let snapEventId = storage.TryGetLastAggregateSnapshotEventId storageVersion storageName aggregateId |> Option.defaultValue 0
                                let result =
                                    if (lastEventId - snapEventId) > snapshotInterval then
                                        storage.SetAggregateSnapshot storageVersion (aggregateId, eventId, state) storageName
                                    else
                                        () |> Ok
                                return! result
                            }
                }, Commons.generalAsyncTimeOut)
        
           
    let inline runCommandMd<'A, 'E, 'F 
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (member Serialize: 'F)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'A: (static member SnapshotsInterval : int)
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'E: (member Serialize: 'F) 
        >
        (eventStore: IEventStore<'F>) 
        (eventBroker: IEventBroker<'F>)
        (md: Metadata)
        (command: Command<'A, 'E>) =
            logger.Value.LogDebug (sprintf "runCommand %A\n" command)
            let command = fun ()  ->
                result {
                    let! (eventId, state) = getFreshState<'A, 'E, 'F> eventStore
                    let! (newState, events) =
                        state
                        |> command.Execute
                        
                    let! ids =
                        (events |>> _.Serialize) |> eventStore.AddEventsMd eventId 'A.Version 'A.StorageName md
                    
                    StateCache2<'A>.Instance.Memoize2 newState (ids |> List.last)
                   
                    let _ = mkSnapshotIfIntervalPassed2<'A, 'E, 'F> eventStore newState (ids |> List.last)
                    
                    return ()
                }
        #if USING_MAILBOXPROCESSOR        
            let processor = MailBoxProcessors.Processors.Instance.GetProcessor 'A.StorageName
            MailBoxProcessors.postToTheProcessor processor command
        #else
            command()
        #endif    
            
    let inline runCommand<'A, 'E, 'F 
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (member Serialize: 'F)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'A: (static member SnapshotsInterval : int)
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'E: (member Serialize: 'F) 
        >
        (eventStore: IEventStore<'F>) 
        (eventBroker: IEventBroker<'F>) 
        (command: Command<'A, 'E>) =
            logger.Value.LogDebug (sprintf "runCommand %A\n" command)
            runCommandMd eventStore eventBroker Metadata.Empty command
   
    let inline runInitAndCommandMd<'A, 'E, 'A1, 'F
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (member Serialize: 'F)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'A: (static member SnapshotsInterval : int)
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'E: (member Serialize: 'F)
        and 'A1:> Aggregate<'F>
        and 'A1 : (static member StorageName: string) 
        and 'A1 : (static member Version: string)
        >
        (storage: IEventStore<'F>)
        (messageSender: StreamName -> MessageSender)
        // (messageSender: MessageSender)
        (initialInstance: 'A1)
        (md: Metadata)
        (command: Command<'A, 'E>)
        =
            logger.Value.LogDebug (sprintf "runInitAndCommand %A %A" 'A.StorageName command)
            
            let command = fun () ->
                result {
                    let! eventId, state = getFreshState<'A, 'E, 'F> storage
                    let! newState, events = 
                        state
                        |> command.Execute
                        
                    let! ids =
                        (events |>> _.Serialize) |> storage.SetInitialAggregateStateAndAddEventsMd eventId initialInstance.Id 'A1.Version 'A1.StorageName initialInstance.Serialize 'A.Version 'A.StorageName md

                    StateCache2<'A>.Instance.Memoize2 newState (ids |> List.last)
                    let _ = mkSnapshotIfIntervalPassed2<'A, 'E, 'F> storage newState (ids |> List.last)
                    AggregateCache2.Instance.Memoize2 (initialInstance |> box |> Ok) (ids |> List.last, initialInstance.Id)
                    
                    let queueName = 'A1.Version + 'A1.StorageName
                    let sender = messageSender queueName
                    let message =
                        MessageType<'A1, 'E1>.InitialSnapshot initialInstance
                    let aggregateMessage =
                        {
                            AggregateId = initialInstance.Id
                            Message = message
                        }.Serialize
                    let sent =
                        task {
                            return! sender aggregateMessage
                        }    
                    
                    return ()
                }
        #if USING_MAILBOXPROCESSOR        
            let processor = MailBoxProcessors.Processors.Instance.GetProcessor 'A.StorageName
            MailBoxProcessors.postToTheProcessor processor command
        #else
            command ()
        #endif    
    
    let inline runInit<'A1, 'E, 'F
        when 'A1 :> Aggregate<'F> and 'E :> Event<'A1>
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'A1: (static member StorageName: string)
        and 'A1: (static member Version: string)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        >
        (eventStore: IEventStore<'F>)
        (messageSenders: StreamName -> MessageSender)
        (initialInstance: 'A1) =
            logger.Value.LogDebug (sprintf "runInit %A" 'A1.StorageName)
            result {
                let! notAlreadyExists =
                    (StateView.getAggregateFreshState<'A1, 'E, 'F> initialInstance.Id eventStore
                    |> Result.isError) // it must be true that this is an error to continue
                    |> Result.ofBool (sprintf "Aggregate with id %A of type %s already exists" initialInstance.Id 'A1.StorageName)
                        
                let! _ = eventStore.SetInitialAggregateState initialInstance.Id 'A1.Version 'A1.StorageName initialInstance.Serialize
                AggregateCache2.Instance.Memoize2 (initialInstance |> box|> Ok) (0, initialInstance.Id)
               
                let queueName = 'A1.Version + 'A1.StorageName 
                let sender = messageSenders queueName // todo: check not found also
                let message =
                    MessageType<'A1, 'E>.InitialSnapshot initialInstance
                let aggregateMessage =
                    {
                        AggregateId = initialInstance.Id
                        Message = message
                    }.Serialize
                
                // fire and forget 
                let sent =
                    task {
                        return! sender aggregateMessage
                    }
                return ()
            }
            
    let inline runDelete<'A1, 'E, 'F
        when 'A1 :> Aggregate<'F> and 'E :> Event<'A1>
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'A1: (static member StorageName: string)
        and 'A1: (static member Version: string)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        >
        (eventStore: IEventStore<'F>)
        (messageSender: StreamName -> MessageSender) 
        (id: AggregateId)
        (predicate: 'A1 -> bool)
        
        =
            logger.Value.LogDebug (sprintf "runDelete %A" 'A1.StorageName)
            result {
                let! eventId, state =
                    StateView.getAggregateFreshState<'A1, 'E, 'F> id eventStore
                do!
                    predicate (state |> unbox)
                    |> Result.ofBool (sprintf "cannot delete aggregate with id %A of type %s as it is not safe according to the predicate" id 'A1.StorageName)
                
                let serializedState =
                    (state :?> 'A1).Serialize
               
                AggregateCache2.Instance.Clean id
                let! _ = eventStore.SnapshotAndMarkDeleted 'A1.Version 'A1.StorageName eventId id serializedState
                
                let queueName = 'A1.Version + 'A1.StorageName
                
                let sender = messageSender queueName // todo: check not found also
                let message =
                    MessageType<'A1, 'E>.Delete
                let aggregateMessage =
                    {
                        AggregateId = id
                        Message = message
                    }.Serialize
                    
                // TODO: fire and forget or what?    
                let sent =
                    task {
                        return! sender aggregateMessage
                    }
                    |> Async.AwaitTask
                    |> Async.RunSynchronously
                
                return ()
            }
   
    let inline runDeleteAndAggregateCommandMd<'A1, 'E1, 'A2, 'E2, 'F
        when 'A1 :> Aggregate<'F>
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'E2 :> Event<'A2>
        and 'E2 :(static member Deserialize: 'F -> Result<'E2, string>)
        and 'E2 : (member Serialize: 'F) 
        and 'A1: (static member StorageName: string)
        and 'A1: (static member Version: string)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1: (static member SnapshotsInterval : int)
        and 'A2 :> Aggregate<'F>
        and 'A2: (static member StorageName: string)
        and 'A2: (static member Version: string)
        and 'A2: (static member Deserialize: 'F -> Result<'A2, string>)
        >
        (eventStore: IEventStore<'F>)
        (messageSenders : StreamName -> MessageSender)
        (md: Metadata)
        (id: AggregateId)
        (streamAggregateId: AggregateId)
        (command: AggregateCommand<'A2, 'E2>)
        (predicate: 'A1 -> bool) =
            logger.Value.LogDebug (sprintf "runDelete %A" 'A1.StorageName)
            result {
                let! eventId, state =
                    getAggregateFreshState<'A1, 'E1, 'F> id eventStore
                do!
                    predicate (state |> unbox)
                    |> Result.ofBool (sprintf "cannot delete aggregate with id %A of type %s as it is not safe according to the predicate" id 'A1.StorageName)
                    
                let! streamEventId, streamState =
                   getAggregateFreshState<'A2, 'E2, 'F> streamAggregateId eventStore
                   
                let! newState, events =
                    streamState
                    |> unbox
                    |> command.Execute
                    
                AggregateCache2.Instance.Clean id
                let! ids =
                    eventStore.SnapshotMarkDeletedAndAddAggregateEventsMd
                        'A1.Version
                        'A1.StorageName
                        eventId
                        id
                        (state |> unbox<'A1>).Serialize
                        streamEventId
                        'A2.Version
                        'A2.StorageName
                        streamAggregateId
                        md
                        (events |>> _.Serialize)
                AggregateCache2.Instance.Memoize2 (newState |> box|> Ok) ((ids |> List.last, streamAggregateId))
                
                let aggregateDeleteMessage =
                    {
                        AggregateId = id
                        Message = MessageType<'A1, 'E1>.Delete
                    }.Serialize
                    
                let deleteMessageSender = messageSenders ('A1.Version + 'A1.StorageName)
                let sentDelete =
                    task {
                        return! deleteMessageSender aggregateDeleteMessage
                    }
                    // |> Async.AwaitTask
                    // |> Async.RunSynchronously
                let eventMessageA2 =
                    {
                        InitEventId = streamEventId
                        EndEventId = ids |> List.last
                        Events = events
                    }
                let aggregateMessageA2 =
                    {
                        AggregateId = streamAggregateId
                        Message = MessageType<'A2, 'E2>.Events eventMessageA2
                    }.Serialize
                let senderA2 = messageSenders ('A2.Version + 'A2.StorageName)
                let sentAggregateMessageA2 =
                    task {
                        return! senderA2 aggregateMessageA2
                    }
                    // |> Async.AwaitTask
                    // |> Async.RunSynchronously    
                                
                return ()
            }
    
    let inline runDeleteAndTwoAggregateCommandsMd<'A, 'E, 'A1, 'E1, 'A2, 'E2, 'F
        when 'A :> Aggregate<'F>
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)         
        and 'E :> Event<'A>
        and 'E : (member Serialize: 'F)
        and 'E : (static member Deserialize: 'F -> Result<'E, string>)
        and 'A1 :> Aggregate<'F>
        and 'A1: (static member StorageName: string)
        and 'A1: (static member Version: string)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A2 :> Aggregate<'F>
        and 'A2: (static member StorageName: string)
        and 'A2: (static member Version: string)
        and 'A2: (static member Deserialize: 'F -> Result<'A2, string>)
        and 'E2 :> Event<'A2>
        and 'E2 :(static member Deserialize: 'F -> Result<'E2, string>)
        and 'E2 : (member Serialize: 'F)>
        (eventStore: IEventStore<'F>)
        // (eventBroker: IEventBroker<'F>)
        (messageSenders: StreamName -> MessageSender)
        (md: Metadata)
        (aggregateId: AggregateId)
        (aggregateId1: AggregateId)
        (aggregateId2: AggregateId)
        (command1: AggregateCommand<'A1, 'E1>)
        (command2: AggregateCommand<'A2, 'E2>)
        (predicate: 'A -> bool) =
            result {
                let! eventId, state =
                    getAggregateFreshState<'A, 'E, 'F> aggregateId eventStore
                do!
                    predicate (state |> unbox)
                    |> Result.ofBool (sprintf "cannot delete aggregate with id %A of type %s as it is not safe according to the predicate" aggregateId 'A1.StorageName)
                
                let! eventIdA1, stateA1 =
                    getAggregateFreshState<'A1, 'E1, 'F> aggregateId1 eventStore
                    
                let! eventIdA2, stateA2 =
                    getAggregateFreshState<'A2, 'E2, 'F> aggregateId2 eventStore
                
                let! newStateA1, eventsA1 =
                    stateA1
                    |> unbox
                    |> command1.Execute
                    
                let! newStateA2, eventsA2 =
                    stateA2
                    |> unbox
                    |> command2.Execute
               
                AggregateCache2.Instance.Clean aggregateId
                let! newLastStateIdsList =
                    eventStore.SnapshotMarkDeletedAndMultiAddAggregateEventsMd
                        md
                        'A.Version
                        'A.StorageName
                        eventId
                        aggregateId
                        (state |> unbox<'A>).Serialize
                        [
                            (eventIdA1, eventsA1 |>> _.Serialize, 'A1.Version, 'A1.StorageName, aggregateId1)
                            (eventIdA2, eventsA2 |>> _.Serialize, 'A2.Version, 'A2.StorageName, aggregateId2)
                        ]    
                AggregateCache2.Instance.Memoize2 (newStateA1 |> box |> Ok) ((newLastStateIdsList.[0] |> List.last, aggregateId1))
                AggregateCache2.Instance.Memoize2 (newStateA2 |> box |> Ok) ((newLastStateIdsList.[1] |> List.last, aggregateId2))
                // todo: send messages
                
                return ()    
            }
        
    let inline foldCommands<'A, 'E when 'E:> Event<'A>>
        (initialState: 'A)
        (commands: List<AggregateCommand<'A, 'E>>) =
            let folder (stateResult: Result<'A * List<'E>, string>) (command: AggregateCommand<'A,'E>) =
                match stateResult with
                | Error e -> Error e
                | Ok (state, events) ->
                    match command.Execute state with
                    | Error e -> Error e
                    | Ok (newState, newEvents) -> Ok (newState, events @ newEvents)
            List.fold folder (Ok (initialState, [])) commands
  
    let inline runDeleteAndTwoNAggregateCommandsMd<'A, 'E, 'A1, 'E1, 'A2, 'E2, 'F
        when 'A :> Aggregate<'F>
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)         
        and 'E :> Event<'A>
        and 'E : (member Serialize: 'F)
        and 'E : (static member Deserialize: 'F -> Result<'E, string>)
        and 'A1 :> Aggregate<'F>
        and 'A1: (static member StorageName: string)
        and 'A1: (static member Version: string)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1: (static member SnapshotsInterval: int)
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A2 :> Aggregate<'F>
        and 'A2: (static member StorageName: string)
        and 'A2: (static member Version: string)
        and 'A2: (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2: (static member SnapshotsInterval: int)
        and 'E2 :> Event<'A2>
        and 'E2 :(static member Deserialize: 'F -> Result<'E2, string>)
        and 'E2 : (member Serialize: 'F)>
        (eventStore: IEventStore<'F>)
        (messageSenders: string -> MessageSender) 
        (md: Metadata)
        (aggregateId: AggregateId)
        (aggregateIds1: List<AggregateId>)
        (aggregateIds2: List<AggregateId>)
        (command1: List<AggregateCommand<'A1, 'E1>>)
        (command2: List<AggregateCommand<'A2, 'E2>>)
        (predicate: 'A -> bool) =
            
            logger.Value.LogDebug "runDeleteAndTwoNAggregateCommandsMd"
            result
                {
                    do!
                        ((aggregateIds1.Length = command1.Length) &&
                        (aggregateIds2.Length = command2.Length))
                        |> Result.ofBool "aggregateIds and commands length must correspond"
                        
                    let aggregateIdsWithCommands1 =
                        List.zip aggregateIds1 command1
                        |> List.groupBy fst
                        |> List.map (fun (id, cmds) -> id, cmds |> List.map snd)
                        
                    let uniqueAggregateIds1 =
                        aggregateIdsWithCommands1
                        |>> fst
                        
                    let aggregateIdsWithCommands2 =
                        List.zip aggregateIds2 command2
                        |> List.groupBy fst
                        |> List.map (fun (id, cmds) -> id, cmds |> List.map snd)    
                       
                    let uniqueAggregateIds2 =
                        aggregateIdsWithCommands2
                        |>> fst
                   
                    let! uniqueInitialstates1 =
                        aggregateIdsWithCommands1
                        |> List.traverseResultM (fun (id, _) -> getAggregateFreshState<'A1, 'E1, 'F> id eventStore)
                        
                    let! uniqueInitialstates2 =
                        aggregateIdsWithCommands2
                        |> List.traverseResultM (fun (id, _) -> getAggregateFreshState<'A2, 'E2, 'F> id eventStore)
                        
                    let uniqueInitialStatesOnly1 =
                        uniqueInitialstates1 
                        |>> fun (_, state) -> state
                    
                    let uniqueInitialStatesOnly2 =
                        uniqueInitialstates2 
                        |>> fun (_, state) -> state
                        
                    let multicommands1 =
                        aggregateIdsWithCommands1
                        |>> fun (_, cmds) -> cmds
                        
                    let multicommands2 =
                        aggregateIdsWithCommands2
                        |>> fun (_, cmds) -> cmds
                        
                    let initialStatesAndMultiCommands1 =
                        List.zip uniqueInitialStatesOnly1 multicommands1
                    let initialStatesAndMultiCommands2 =
                        List.zip uniqueInitialStatesOnly2 multicommands2
                        
                    let! newStatesAndEvents1 =
                        initialStatesAndMultiCommands1
                        |> List.traverseResultM (fun (state, commands) -> foldCommands (state |> unbox) commands)
                        
                    let! newStatesAndEvents2 =
                        initialStatesAndMultiCommands2
                        |> List.traverseResultM (fun (state, commands) -> foldCommands (state |> unbox) commands)
                    
                    let newStates1 =
                        newStatesAndEvents1
                        |>> fst
                        
                    let generatedEvents1 =
                        newStatesAndEvents1
                        |>> snd
                        
                    let newStates2 =
                        newStatesAndEvents2
                        |>> fst    
                        
                    let generatedEvents2 =
                        newStatesAndEvents2
                        |>> snd
                        
                    let serializedEvents1 =
                        generatedEvents1
                        |>> fun x -> x |>> fun (z: 'E1) -> z.Serialize
                    
                    let serializedEvents2 =
                        generatedEvents2
                        |>> fun x -> x |>> fun (z: 'E2) -> z.Serialize
                        
                    let initialStateEventIds1 =
                        uniqueInitialstates1
                        |>> fst
                        
                    let initialStateEventIds2 =
                        uniqueInitialstates2
                        |>> fst
                    
                    let aggregateIds1 =
                        aggregateIdsWithCommands1
                        |>> fst
                        
                    let aggregateIds2 =
                        aggregateIdsWithCommands2
                        |>> fst
                        
                    let initialEventIds1Events1AndAggregateIds1 =
                        List.zip3 initialStateEventIds1 serializedEvents1 aggregateIds1
                        |>> fun (eventId, events, id) -> (eventId, events, 'A1.Version, 'A1.StorageName, id)
                    
                    let initialEventIds2EventIds2AndAggregateIds2 =
                        List.zip3 initialStateEventIds2 serializedEvents2 aggregateIds2
                        |>> fun (eventId, events, id) -> (eventId, events, 'A2.Version, 'A2.StorageName, id)
                    
                    let allPacked = initialEventIds1Events1AndAggregateIds1 @ initialEventIds2EventIds2AndAggregateIds2
                    
                    let! (eventId, toBeDeleted) = getAggregateFreshState<'A, 'E, 'F> aggregateId eventStore
                    
                    do! predicate (toBeDeleted |> unbox)
                        |> Result.ofBool "condition is not met"
                    
                    let _ = AggregateCache2.Instance.Clean aggregateId
                    
                    let! dbNewStatesEventIds =
                        eventStore.SnapshotMarkDeletedAndMultiAddAggregateEventsMd
                            md
                            'A.Version
                            'A.StorageName
                            eventId
                            aggregateId
                            (toBeDeleted |> unbox<'A>).Serialize
                            allPacked
                    
                    let aggregateDeleteMessage =
                        {
                            AggregateId = aggregateId
                            Message = MessageType<'A, 'E>.Delete
                        }.Serialize
                 
                    let deleteMessageSender = messageSenders ('A.Version + 'A.StorageName)
                     
                    let sentDeleteMessage =
                        task {
                            return! deleteMessageSender aggregateDeleteMessage
                        }
                        
                    let newDbBasedEventIds1 =
                        dbNewStatesEventIds
                        |> List.take uniqueAggregateIds1.Length
                    let newDbBasedEventIds2 =
                        dbNewStatesEventIds
                        |> List.skip uniqueAggregateIds1.Length
                        
                    let doCacheResults = 
                        fun () ->
                            for i in 0 .. (uniqueAggregateIds1.Length - 1) do
                                AggregateCache2.Instance.Memoize2 (newStates1.[i] |> box |> Ok) (newDbBasedEventIds1.[i] |> List.last, uniqueAggregateIds1.[i])
                                mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore uniqueAggregateIds1.[i] newStates1.[i] (newDbBasedEventIds1.[i] |> List.last) |> ignore
                                
                            for i in 0 .. (uniqueAggregateIds2.Length - 1) do
                                AggregateCache2.Instance.Memoize2 (newStates2.[i] |> box |> Ok) (newDbBasedEventIds2.[i] |> List.last, uniqueAggregateIds2.[i])
                                mkAggregateSnapshotIfIntervalPassed2<'A2, 'E2, 'F> eventStore uniqueAggregateIds2.[i] newStates2.[i] (newDbBasedEventIds2.[i] |> List.last) |> ignore
                    doCacheResults ()
                   
                    let initialEventIdsFinalEventIdsAndEventsA1 =
                        List.zip3 initialStateEventIds1 (newDbBasedEventIds1 |>> List.last) generatedEvents1
                        |>> fun (eventId, finalEventId, events) ->
                            {
                                InitEventId = eventId
                                EndEventId = finalEventId
                                Events = events
                            }
                    logger.Value.LogDebug (sprintf "XXXXXXX Xevent A1 %A\n" initialEventIdsFinalEventIdsAndEventsA1)
                            
                    let aggregateMessagesA1 =
                        List.zip uniqueAggregateIds1 initialEventIdsFinalEventIdsAndEventsA1
                        |>> fun (id, message) ->
                            {
                                AggregateId = id
                                Message = MessageType<'A1, 'E1>.Events message
                            }.Serialize
                    
                    let senderA1 = messageSenders ('A1.Version + 'A1.StorageName)
                    logger.Value.LogDebug (sprintf "YYYYY Xevent A1 %A\n" initialEventIdsFinalEventIdsAndEventsA1)     
                    
                    // todo: evaluate this style of fire and forget and compare with other similar ones
                    let sentAggregateMessagesA1 =
                        aggregateMessagesA1
                        |> List.iter
                                (fun message ->
                                    logger.Value.LogDebug (sprintf "XXXXXA1 sending message to %s %A\n" ('A1.Version + 'A1.StorageName) message)
                                     
                                    senderA1 message |> ignore)
                        
                    logger.Value.LogDebug (sprintf "YYYYY Xevent A1 %A\n" initialEventIdsFinalEventIdsAndEventsA1)     
                        
                    let initialEventIdsFinalEventIdsAndEventsA2 =
                        List.zip3 initialStateEventIds2 (newDbBasedEventIds2 |>> List.last) generatedEvents2
                        |>> fun (eventId, finalEventId, events) ->
                            {
                                InitEventId = eventId
                                EndEventId = finalEventId
                                Events = events
                            }
                            
                    logger.Value.LogDebug (sprintf "XXXXXXX Xevent A2 %A\n" initialEventIdsFinalEventIdsAndEventsA2)
                    
                    let aggregateMessagesA2 =
                        List.zip uniqueAggregateIds2 initialEventIdsFinalEventIdsAndEventsA2
                        |>> fun (id, message) ->
                            {
                                AggregateId = id
                                Message = MessageType<'A2, 'E2>.Events message
                            }.Serialize
                    
                    let senderA2 = messageSenders ('A2.Version + 'A2.StorageName)
                    
                    let sentAggregateMessagesA2 =
                        aggregateMessagesA2
                        |> List.iter (fun message ->
                            logger.Value.LogDebug (sprintf "XXXXXA2 sending message to %s %A\n" ('A2.Version + 'A2.StorageName) message)
                            senderA2 message |> ignore)
                     
                    let allIds = uniqueAggregateIds1 @ uniqueAggregateIds2
                    let duplicatedIds =
                        allIds
                        |> List.groupBy id
                        |> List.filter (fun (_, l) -> l.Length > 1)
                        |> List.map (fun (id, _) -> id)
                    
                    let _ =
                        aggregateIds1 |> List.iter (fun id ->
                            if (duplicatedIds |> List.contains id) then
                                AggregateCache2.Instance.Clean id
                        )
                        aggregateIds2 |> List.iter (fun id ->
                            if (duplicatedIds |> List.contains id) then
                                AggregateCache2.Instance.Clean id
                        )
                    return ()
                }
        
    let inline runInitAndCommand<'A, 'E, 'A1, 'F
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (member Serialize: 'F)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'A: (static member SnapshotsInterval : int)
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'E: (member Serialize: 'F)
        and 'A1:> Aggregate<'F>
        and 'A1 : (static member StorageName: string) 
        and 'A1 : (static member Version: string)
        >
        (storage: IEventStore<'F>)
        (messageSender: StreamName -> MessageSender)
        (initialInstance: 'A1)
        (command: Command<'A, 'E>)
        =
            logger.Value.LogDebug (sprintf "runInitAndCommand %A %A" 'A.StorageName command)
            runInitAndCommandMd<'A, 'E, 'A1, 'F> storage messageSender initialInstance Metadata.Empty command
            
    let rec inline runInitAndAggregateCommandMd<'A1, 'E1, 'A2, 'F
        when 'A1 :> Aggregate<'F>
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1: (static member StorageName: string)
        and 'A1: (static member Version: string)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1: (static member SnapshotsInterval : int)
        and 'A2 :> Aggregate<'F>
        and 'A2: (static member StorageName: string)
        and 'A2: (static member Version: string)
        >
        (aggregateId: Guid)
        (storage: IEventStore<'F>)
        (messageSenders: StreamName -> MessageSender) 
        (initialInstance: 'A2)
        (md: Metadata)
        (command: AggregateCommand<'A1, 'E1>)
        =
            logger.Value.LogDebug (sprintf "runInitAndAggregateCommand %A %A" 'A1.StorageName command)
            let command = fun () ->
                result {
                    let! (eventId, state) = getAggregateFreshState<'A1, 'E1, 'F> aggregateId storage
                    let! (newState, events) =
                        state
                        |> unbox
                        |> command.Execute
                    let events' =
                        events 
                        |>> fun x -> x.Serialize
                    let! ids =
                        events' |> storage.SetInitialAggregateStateAndAddAggregateEventsMd eventId initialInstance.Id 'A2.Version 'A2.StorageName aggregateId initialInstance.Serialize 'A1.Version 'A1.StorageName md
                        
                    AggregateCache2.Instance.Memoize2 (newState |> box |> Ok) ((ids |> List.last, aggregateId))
                    AggregateCache2.Instance.Memoize2 (initialInstance |> box |>  Ok) (0, initialInstance.Id)
            
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> storage aggregateId newState (ids |> List.last)
                    
                    let snapshotMessageSenderA2 = messageSenders ('A2.Version + 'A2.StorageName)
                    let eventsMessageSenderA1 = messageSenders ('A1.Version + 'A1.StorageName)
                    
                    let snapshotMessage =
                        MessageType<'A2, 'E2>.InitialSnapshot initialInstance 
                    
                    let aggregateMessage =
                        {
                            AggregateId = initialInstance.Id
                            Message = snapshotMessage
                        }.Serialize
                        
                    let sentSnapshot =
                        task {
                            return! snapshotMessageSenderA2 aggregateMessage
                        }
                  
                    let eventMessage =
                        {
                            InitEventId = eventId
                            EndEventId = ids |> List.last
                            Events = events
                        }
                    let packedEventMessage =
                        {
                            AggregateId = aggregateId
                            Message = MessageType<'A1, 'E1>.Events eventMessage
                        }.Serialize
                    
                    let sentEvent =
                        task {
                            return! eventsMessageSenderA1 packedEventMessage
                        }
                        
                    return ()    
                }
                
        #if USING_MAILBOXPROCESSOR         
            let processor = MailBoxProcessors.Processors.Instance.GetProcessor 'A1.StorageName
            MailBoxProcessors.postToTheProcessor processor command
        #else
            command ()
        #endif
    
    let inline runInitAndNAggregateCommandsMd<'A1, 'E1, 'A2, 'F
        when 'A1 :> Aggregate<'F>
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1: (static member StorageName: string)
        and 'A1: (static member Version: string)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1: (static member SnapshotsInterval : int)
        and 'A2 :> Aggregate<'F>
        and 'A2: (static member StorageName: string)
        and 'A2: (static member Version: string)
        >
        (aggregateIds: List<Guid>)
        (storage: IEventStore<'F>)
        (messageSenders: StreamName -> MessageSender) 
        (initialInstance: 'A2)
        (md: Metadata)
        (commands: List<AggregateCommand<'A1, 'E1>>)
        =
            logger.Value.LogDebug (sprintf "runInitAndNAggregateCommands %A %A" 'A1.StorageName commands)
            let command = fun () ->
                result {
                    let! states =
                        aggregateIds
                        |> List.traverseResultM (fun id -> getAggregateFreshState<'A1, 'E1, 'F> id storage)
                    let states' = 
                        states 
                        |>> fun (_, state) -> state
                    let lastEventIds =
                        states
                        |>> fun (eventId, _) -> eventId
                    let statesAndCommands =
                        List.zip states' commands
                    let! stateEvents =
                        statesAndCommands
                        |>> fun (state, command) -> (command.Execute (state |> unbox))
                        |> List.traverseResultM id
                    let newStates =
                        stateEvents 
                        |>> fun (state, _) -> state
                    let events =
                        stateEvents
                        |>> fun (_, events) -> events
                        
                    let serializedEvents =
                        events 
                        |>> fun x -> x |>> fun (z: 'E1) -> z.Serialize
                        
                    let currentStateEventIdEventsAndAggregateIds =
                        List.zip3 lastEventIds serializedEvents aggregateIds
                        |>> fun (eventId, events, id) -> (eventId, events, 'A1.Version, 'A1.StorageName, id)
                        
                    let! eventIds =
                        currentStateEventIdEventsAndAggregateIds
                        |> storage.SetInitialAggregateStateAndMultiAddAggregateEventsMd initialInstance.Id 'A2.Version 'A2.StorageName initialInstance.Serialize md 
                    
                    let finalWrittenEventIds = eventIds |>> List.last  
                        
                    for i in 0..(aggregateIds.Length - 1) do
                        AggregateCache2.Instance.Memoize2 (newStates.[i] |> box |> Ok) (eventIds.[i] |> List.last, aggregateIds.[i])
                        mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> storage aggregateIds.[i] newStates.[i] (eventIds.[i] |> List.last) |> ignore
                     
                    let snapshotStreamName = sprintf "%s%s" 'A2.Version 'A2.StorageName
                    let snapshotMessageSender = messageSenders snapshotStreamName
                    let snapshotMessage =
                        MessageType<'A2, 'E2>.InitialSnapshot initialInstance
                    
                    let aggregateMessage =
                        {
                            AggregateId = initialInstance.Id
                            Message = snapshotMessage
                        }.Serialize
                    
                    // todo: may be without enveloping a task as it is already a task 🤭
                    let sent =
                        task
                            {
                                return! snapshotMessageSender aggregateMessage
                            }
                            
                    let eventsStreamName = sprintf "%s%s" 'A1.Version 'A1.StorageName
                    let eventsMessageSender = messageSenders eventsStreamName
                    let initialEventIdsFinalEventsIdsAndEvents =
                        List.zip3 lastEventIds finalWrittenEventIds events
                    let eventsMessages: List<EventsMessage<'E>> =
                        initialEventIdsFinalEventsIdsAndEvents
                        |>> fun (eventId, finalEventId, events) ->
                            {
                                InitEventId = eventId
                                EndEventId = finalEventId
                                Events = events
                            }
                        
                    let aggregateMessages =
                        List.zip aggregateIds eventsMessages
                        |>> (fun (id, eventsMessage) ->
                                {
                                    AggregateId = id
                                    Message = MessageType.Events eventsMessage
                                }.Serialize
                            )
                    
                    let sentAllEvents =
                        aggregateMessages
                        |> List.iter (fun message -> eventsMessageSender message |> ignore)
                             
                    return ()
                }
        #if USING_MAILBOXPROCESSOR         
            let processor = MailBoxProcessors.Processors.Instance.GetProcessor 'A1.StorageName
            MailBoxProcessors.postToTheProcessor processor command
        #else
            command ()
        #endif
    
    let inline runInitAndAggregateCommand<'A1, 'E1, 'A2, 'F
        when 'A1 :> Aggregate<'F>
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1: (static member StorageName: string)
        and 'A1: (static member Version: string)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1: (static member SnapshotsInterval : int)
        and 'A2 :> Aggregate<'F>
        and 'A2: (static member StorageName: string)
        and 'A2: (static member Version: string)
        >
        (aggregateId: Guid)
        (storage: IEventStore<'F>)
        // (eventBroker: IEventBroker<'F>)
        (messageSenders: StreamName -> MessageSender) 
        (initialInstance: 'A2)
        (command: AggregateCommand<'A1, 'E1>)
        =
            logger.Value.LogDebug (sprintf "runInitAndAggregateCommand %A %A" 'A1.StorageName command)
            runInitAndAggregateCommandMd<'A1, 'E1, 'A2, 'F> aggregateId storage messageSenders initialInstance String.Empty command
            
    let inline runInitAndTwoAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'F, 'A3
        when 'A1 :> Aggregate<'F>
        and 'E1:> Event<'A1>
        and 'E1: (member Serialize: 'F)
        and 'E1: (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1: (static member StorageName: string)
        and 'A1: (static member Version: string)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1: (static member SnapshotsInterval : int)
        and 'A2:> Aggregate<'F>
        and 'E2:> Event<'A2>
        and 'E2: (member Serialize: 'F)
        and 'E2: (static member Deserialize: 'F -> Result<'E2, string>)
        and 'A2: (static member StorageName: string)
        and 'A2: (static member Version: string)
        and 'A2: (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2: (static member SnapshotsInterval : int)
        and 'A3:> Aggregate<'F>
        and 'A3: (static member StorageName: string)
        and 'A3: (static member Version: string)
        >
        (aggregateId1: Guid)
        (aggregateId2: Guid)
        (eventStore: IEventStore<'F>)
        (messageSenders: StreamName -> MessageSender)
        (initialInstance: 'A3)
        (md: Metadata)
        (command1: AggregateCommand<'A1, 'E1>)
        (command2: AggregateCommand<'A2, 'E2>)
        =
            logger.Value.LogDebug (sprintf "runInitAndTwoAggregateCommands %A %A %A %A %A %A" 'A1.StorageName 'A2.StorageName command1 command2 aggregateId1 aggregateId2)
            let command = fun () ->
                result {
                    let! eventId1, state1 = getAggregateFreshState<'A1, 'E1, 'F> aggregateId1 eventStore
                    let! eventId2, state2 = getAggregateFreshState<'A2, 'E2, 'F> aggregateId2 eventStore
                    let! newState1, events1 =
                        state1
                        |> unbox
                        |> command1.Execute
                    let! newState2, events2 =
                        state2
                        |> unbox
                        |> command2.Execute
                        
                    let multiEvents =
                        [
                            (eventId1, events1 |>> _.Serialize, 'A1.Version, 'A1.StorageName, aggregateId1)
                            (eventId2, events2 |>> _.Serialize, 'A2.Version, 'A2.StorageName, aggregateId2)
                        ]
                    let! ids =
                        eventStore.SetInitialAggregateStateAndMultiAddAggregateEventsMd initialInstance.Id 'A3.Version 'A3.StorageName initialInstance.Serialize md multiEvents
                    
                    AggregateCache2.Instance.Memoize2 (newState1 |> box |> Ok) ((ids.[0] |> List.last, aggregateId1))
                    AggregateCache2.Instance.Memoize2 (newState2 |> box |> Ok) ((ids.[1] |> List.last, aggregateId2))
                    AggregateCache2.Instance.Memoize2 (initialInstance |> box|> Ok) (0, initialInstance.Id)
                    
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore aggregateId1 newState1 (ids.[0] |> List.last)
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A2, 'E2, 'F> eventStore aggregateId2 newState2 (ids.[1] |> List.last)
                    
                    let a3SnapshotMessageSender = messageSenders ('A3.Version + 'A3.StorageName)
                    let a1EventsMessageSender = messageSenders ('A1.Version + 'A1.StorageName)
                    let a2EventsMessageSender = messageSenders ('A2.Version + 'A2.StorageName)
                    
                    let snapshotMessage =
                        MessageType<'A3, 'E3>.InitialSnapshot initialInstance
                        
                    let snapshotAggregateMessage =
                        {
                            AggregateId = initialInstance.Id
                            Message = snapshotMessage
                        }.Serialize
                    
                    let sentSnapshot =
                        task {
                            return! a3SnapshotMessageSender snapshotAggregateMessage
                        }
                        
                    let eventMessageA1 =
                        {
                            InitEventId = eventId1
                            EndEventId = ids.[0] |> List.last
                            Events = events1
                        }
                   
                    let sendableEventMessageA1 =
                        {
                            AggregateId = aggregateId1
                            Message = MessageType<'A1, 'E1>.Events eventMessageA1
                        }.Serialize
                        
                    let eventMessageA2 =
                        {
                            InitEventId = eventId2
                            EndEventId = ids.[1] |> List.last
                            Events = events2
                        }
                    
                    let sendableEventMessageA2 =    
                        {
                            AggregateId = aggregateId2
                            Message = MessageType<'A2, 'E2>.Events eventMessageA2
                        }.Serialize
                        
                    let sendEventA1 =
                        task {
                            return! a1EventsMessageSender sendableEventMessageA1
                        }
                        
                    let sendEventA2 =
                        task {
                            return! a2EventsMessageSender sendableEventMessageA2
                        }
                    
                    return ()
                }
        #if USING_MAILBOXPROCESSOR        
            let lookupName = sprintf "%s_%s" 'A1.StorageName 'A2.StorageName
            MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) command
        #else    
            command()
        #endif
            
    let inline runInitAndTwoAggregateCommands<'A1, 'E1, 'A2, 'E2, 'F, 'A3
        when 'A1 :> Aggregate<'F>
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1: (static member StorageName: string)
        and 'A1: (static member Version: string)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1: (static member SnapshotsInterval : int)
        and 'A2 :> Aggregate<'F>
        and 'E2 :> Event<'A2>
        and 'E2 : (member Serialize: 'F)
        and 'E2 : (static member Deserialize: 'F -> Result<'E2, string>)
        and 'A2: (static member StorageName: string)
        and 'A2: (static member Version: string)
        and 'A2: (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2: (static member SnapshotsInterval : int)
        and 'A3 :> Aggregate<'F>
        and 'A3: (static member StorageName: string)
        and 'A3: (static member Version: string)
        >
        (aggregateId1: Guid)
        (aggregateId2: Guid)
        (eventStore: IEventStore<'F>)
        (messageSenders: StreamName -> MessageSender)
        (initialInstance: 'A3)
        (command1: AggregateCommand<'A1, 'E1>)
        (command2: AggregateCommand<'A2, 'E2>)
        =
            logger.Value.LogDebug (sprintf "runInitAndTwoAggregateCommands %A %A %A %A %A %A" 'A1.StorageName 'A2.StorageName command1 command2 aggregateId1 aggregateId2)
            runInitAndTwoAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'F, 'A3> aggregateId1 aggregateId2 eventStore messageSenders initialInstance String.Empty command1 command2
    
    let inline runInitAndThreeAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'A3, 'E3, 'F, 'A4
        when 'A1 :> Aggregate<'F>
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1: (static member StorageName: string)
        and 'A1: (static member Version: string)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1: (static member SnapshotsInterval : int)
        and 'A2 :> Aggregate<'F>
        and 'E2 :> Event<'A2>
        and 'E2 : (member Serialize: 'F)
        and 'E2 : (static member Deserialize: 'F -> Result<'E2, string>)
        and 'A2: (static member StorageName: string)
        and 'A2: (static member Version: string)
        and 'A2: (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2: (static member SnapshotsInterval : int)
        and 'A3 :> Aggregate<'F>
        and 'E3 :> Event<'A3>
        and 'E3 : (member Serialize: 'F)
        and 'E3 : (static member Deserialize: 'F -> Result<'E3, string>)
        and 'A3: (static member StorageName: string)
        and 'A3: (static member Version: string)
        and 'A3: (static member Deserialize: 'F -> Result<'A3, string>)
        and 'A3: (static member SnapshotsInterval : int)
        and 'A4 :> Aggregate<'F>
        and 'A4: (static member StorageName: string)
        and 'A4: (static member Version: string)
        >
        (aggregateId1: Guid)
        (aggregateId2: Guid)
        (aggregateId3: Guid)
        (eventStore: IEventStore<'F>)
        (messageSenders: StreamName -> MessageSender)
        (initialInstance: 'A4)
        (md: Metadata)
        (command1: AggregateCommand<'A1, 'E1>)
        (command2: AggregateCommand<'A2, 'E2>)
        (command3: AggregateCommand<'A3, 'E3>)
        =
            logger.Value.LogDebug (sprintf "runInitAndThreeAggregateCommands %A %A %A %A %A %A %A %A %A" 'A1.StorageName 'A2.StorageName 'A3.StorageName command1 command2 command3 aggregateId1 aggregateId2 aggregateId3)
            let command = fun () ->
                result {
                    let! eventId1, state1 = getAggregateFreshState<'A1, 'E1, 'F> aggregateId1 eventStore
                    let! eventId2, state2 = getAggregateFreshState<'A2, 'E2, 'F> aggregateId2 eventStore
                    let! eventId3, state3 = getAggregateFreshState<'A3, 'E3, 'F> aggregateId3 eventStore
                    let! newState1, events1 =
                        state1
                        |> unbox
                        |> command1.Execute
                    let! newState2, events2 =
                        state2
                        |> unbox
                        |> command2.Execute
                    let! newState3, events3 =
                        state3
                        |> unbox
                        |> command3.Execute
                        
                    let multiEvents =
                        [
                            (eventId1, events1 |>> _.Serialize, 'A1.Version, 'A1.StorageName, aggregateId1)
                            (eventId2, events2 |>> _.Serialize, 'A2.Version, 'A2.StorageName, aggregateId2)
                            (eventId3, events3 |>> _.Serialize, 'A3.Version, 'A3.StorageName, aggregateId3)
                        ]
                    let! ids =
                        eventStore.SetInitialAggregateStateAndMultiAddAggregateEventsMd initialInstance.Id 'A4.Version 'A4.StorageName initialInstance.Serialize md multiEvents
                    
                    AggregateCache2.Instance.Memoize2 (newState1 |> box |> Ok) ((ids.[0] |> List.last, aggregateId1))
                    AggregateCache2.Instance.Memoize2 (newState2 |> box |> Ok) ((ids.[1] |> List.last, aggregateId2))
                    AggregateCache2.Instance.Memoize2 (newState3 |> box |> Ok) ((ids.[2] |> List.last, aggregateId3))
                    
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore aggregateId1 newState1 (ids.[0] |> List.last)
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A2, 'E2, 'F> eventStore aggregateId2 newState2 (ids.[1] |> List.last)
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A3, 'E3, 'F> eventStore aggregateId3 newState3 (ids.[2] |> List.last)
                   
                    let a4SnapshotMessageSender = messageSenders ('A4.Version + 'A4.StorageName)
                    let a1EventsMessageSender = messageSenders ('A1.Version + 'A1.StorageName)
                    let a2EventsMessageSender = messageSenders ('A2.Version + 'A2.StorageName)
                    let a3EventsMessageSender = messageSenders ('A3.Version + 'A3.StorageName)
                    let snapshotMessage =
                        MessageType<'A4, 'E4>.InitialSnapshot initialInstance
                    
                    let snapshotAggregateMessage =
                        {
                            AggregateId = initialInstance.Id
                            Message = snapshotMessage
                        }.Serialize
                    
                    let sentSnapshot =
                        task {
                            return! a4SnapshotMessageSender snapshotAggregateMessage
                        }
                    
                    let eventMessageA1 =
                        {
                            InitEventId = eventId1
                            EndEventId = ids.[0] |> List.last
                            Events = events1
                        }
                    let sendableEventMessageA1 =
                        {
                            AggregateId = aggregateId1
                            Message = MessageType<'A1, 'E1>.Events eventMessageA1
                        }.Serialize
                  
                    let eventMessageA2 =
                        {
                            InitEventId = eventId2
                            EndEventId = ids.[1] |> List.last
                            Events = events2
                        }
                    let sendableEventMessageA2 =
                        {
                            AggregateId = aggregateId2
                            Message = MessageType<'A2, 'E2>.Events eventMessageA2
                        }.Serialize
                    
                    let eventMessageA3 =
                        {
                            InitEventId = eventId3
                            EndEventId = ids.[2] |> List.last
                            Events = events3
                        }
                    let sendableEventMessageA3 =
                        {
                            AggregateId = aggregateId3
                            Message = MessageType<'A3, 'E3>.Events eventMessageA3
                        }.Serialize
                    
                    let sendA1 =
                        a1EventsMessageSender sendableEventMessageA1
                    let sendA2 =
                        a2EventsMessageSender sendableEventMessageA2
                    let sendA3 =
                        a3EventsMessageSender sendableEventMessageA3
                     
                    return ()
                }
        #if USING_MAILBOXPROCESSOR       
            let lookupName = sprintf "%s_%s_%s" 'A1.StorageName 'A2.StorageName 'A3.StorageName
            MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) command
        #else    
            command ()
        #endif     
            
    let inline runInitAndThreeAggregateCommands<'A1, 'E1, 'A2, 'E2, 'A3, 'E3, 'F, 'A4
        when 'A1 :> Aggregate<'F>
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1: (static member StorageName: string)
        and 'A1: (static member Version: string)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1: (static member SnapshotsInterval : int)
        and 'A2 :> Aggregate<'F>
        and 'E2 :> Event<'A2>
        and 'E2 : (member Serialize: 'F)
        and 'E2 : (static member Deserialize: 'F -> Result<'E2, string>)
        and 'A2: (static member StorageName: string)
        and 'A2: (static member Version: string)
        and 'A2: (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2: (static member SnapshotsInterval : int)
        and 'A3 :> Aggregate<'F>
        and 'E3 :> Event<'A3>
        and 'E3 : (member Serialize: 'F)
        and 'E3 : (static member Deserialize: 'F -> Result<'E3, string>)
        and 'A3: (static member StorageName: string)
        and 'A3: (static member Version: string)
        and 'A3: (static member Deserialize: 'F -> Result<'A3, string>)
        and 'A3: (static member SnapshotsInterval : int)
        and 'A4 :> Aggregate<'F>
        and 'A4: (static member StorageName: string)
        and 'A4: (static member Version: string)
        >
        (aggregateId1: Guid)
        (aggregateId2: Guid)
        (aggregateId3: Guid)
        (eventStore: IEventStore<'F>)
        (messageSenders: StreamName -> MessageSender)
        (initialInstance: 'A4)
        (command1: AggregateCommand<'A1, 'E1>)
        (command2: AggregateCommand<'A2, 'E2>)
        (command3: AggregateCommand<'A3, 'E3>)
        =
            logger.Value.LogDebug (sprintf "runInitAndThreeAggregateCommands %A %A %A %A %A %A %A %A %A" 'A1.StorageName 'A2.StorageName 'A3.StorageName command1 command2 command3 aggregateId1 aggregateId2 aggregateId3)
            runInitAndThreeAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'A3, 'E3, 'F, 'A4> aggregateId1 aggregateId2 aggregateId3 eventStore messageSenders initialInstance Metadata.Empty command1 command2 command3

    let inline preExecuteAggregateCommandMd<'A, 'E, 'F
        when 'A :> Aggregate<'F>
        and 'E :> Event<'A>
        and 'A : (static member Deserialize: 'F -> Result<'A, string>) 
        and 'A : (static member StorageName: string) 
        and 'A : (static member Version: string)
        and 'A : (static member SnapshotsInterval: int)
        and 'E : (static member Deserialize: 'F -> Result<'E, string>)
        and 'E : (member Serialize: 'F)
        >
        (aggregateId: Guid)
        (storage: IEventStore<'F>)
        (messageSender: string -> MessageSender) // Todo: not used. Drop this parameter
        (md: Metadata)
        (command: AggregateCommand<'A, 'E>)
        =
            logger.Value.LogDebug (sprintf "runAggregateCommand %A,  %A, id: %A" 'A.StorageName command  aggregateId)
            
            result {
                let! eventId, state = getAggregateFreshState<'A, 'E, 'F> aggregateId storage
                let! newState, events =
                    state
                    |> unbox
                    |> command.Execute
                    
                let result  =
                    {
                      AggregateId = aggregateId
                      EventId = eventId
                      NewState = box newState
                      // NewState = newState
                      SerializedEvents = events |>> (fun x -> x.Serialize)
                      Metadata = md
                      Version = 'A.Version
                      StorageName = 'A.StorageName
                      SnapshotsInterval = 'A.SnapshotsInterval
                      EventType = typeof<'E>
                    }
                return result
            }
            
    let storeEvents (eventStore: IEventStore<'F>) (messageSender: StramName -> MessageSender) (block: PreExecutedAggregateCommand<_, _>) =
        logger.Value.LogDebug (sprintf "storeAggregateBlock %A,  %A, id: %A" block.StorageName block.AggregateId block.AggregateId)
        result {
            let! ids =
                block.SerializedEvents
                |> eventStore.AddAggregateEventsMd block.EventId block.Version block.StorageName block.AggregateId block.Metadata
            return ids  
        }
    
    let storeMultipleEvents (eventStore: IEventStore<'F>) (eventBroker: string -> MessageSender) (blocks: List<PreExecutedAggregateCommand<_, _>>) =
        logger.Value.LogDebug (sprintf "storeAggregateBlock %A,  %A, id: %A" blocks.Head.StorageName blocks.Head.AggregateId blocks.Head.AggregateId)
        result {
            do!
                blocks.Length > 0
                |> Result.ofBool "blocks length must be greater than 0"
            let md = blocks.Head.Metadata    
            let parameters =
                blocks
                |> List.map (fun b -> b.EventId, b.SerializedEvents, b.Version, b.StorageName, b.AggregateId)
            
            let! newLastStateIdsList =
                eventStore.MultiAddAggregateEventsMd md parameters
            return newLastStateIdsList    
        }
    
    let inline runAggregateCommandMd<'A, 'E, 'F
        when 'A :> Aggregate<'F>
        and 'E :> Event<'A>
        and 'A : (static member Deserialize: 'F -> Result<'A, string>) 
        and 'A : (static member StorageName: string) 
        and 'A : (static member Version: string)
        and 'A : (static member SnapshotsInterval: int)
        and 'E : (static member Deserialize: 'F -> Result<'E, string>)
        and 'E : (member Serialize: 'F)
        >
        (aggregateId: Guid)
        (storage: IEventStore<'F>)
        (messageSenders: string -> MessageSender)
        (md: Metadata)
        (command: AggregateCommand<'A, 'E>)
        =
            logger.Value.LogDebug (sprintf "runAggregateCommandRefactor %A,  %A, id: %A" 'A.StorageName command  aggregateId)
            result {
                // duplication here as the preExecute does similar stuff
                let! (eventId, state) = getAggregateFreshState<'A, 'E, 'F> aggregateId storage
                let! newState, events =
                    state
                    |> unbox
                    |> command.Execute
            
                let! executedCommand = preExecuteAggregateCommandMd<'A, 'E, 'F> aggregateId storage messageSenders md command
                let! ids = storeEvents storage messageSenders executedCommand
               
                let queueName = 'A.Version + 'A.StorageName
                let sender = messageSenders queueName
                
                let message =
                    MessageType<'A, 'E>.Events {InitEventId = eventId; EndEventId = ids |> List.last; Events = events}
                let aggregateMessage =
                    {
                        AggregateId = aggregateId
                        Message = message
                    }.Serialize
                
                AggregateCache2.Instance.Memoize2 (executedCommand.NewState |> unbox |> Ok) (ids |> List.last, aggregateId)
                let _ = mkAggregateSnapshotIfIntervalPassed2<'A, 'E, 'F> storage aggregateId (executedCommand.NewState |> unbox) (ids |> List.last)
                let sent =
                    sender aggregateMessage //todo: try to handle properly the fire-and-forget concept (perhaps with error handling) 
                return ()
            }
    
    [<Obsolete("use runAggregateCommandMd instead")>]
    let inline runAggregateCommandMdBack<'A, 'E, 'F
        when 'A :> Aggregate<'F>
        and 'E :> Event<'A>
        and 'A : (static member Deserialize: 'F -> Result<'A, string>) 
        and 'A : (static member StorageName: string) 
        and 'A : (static member Version: string)
        and 'A : (static member SnapshotsInterval: int)
        and 'E : (static member Deserialize: 'F -> Result<'E, string>)
        and 'E : (member Serialize: 'F)
        >
        (aggregateId: Guid)
        (storage: IEventStore<'F>)
        (eventBroker: IEventBroker<'F>)
        (md: Metadata)
        (command: AggregateCommand<'A, 'E>)
        =
            logger.Value.LogDebug (sprintf "runAggregateCommand %A,  %A, id: %A" 'A.StorageName command  aggregateId)
            let command = fun () ->
                result {
                    let! eventId, state = getAggregateFreshState<'A, 'E, 'F> aggregateId storage
                    let! newState, events =
                        state
                        |> unbox
                        |> command.Execute
                    let! ids =
                        events |>> _.Serialize
                        |> storage.AddAggregateEventsMd eventId 'A.Version 'A.StorageName aggregateId md
                   
                    AggregateCache2.Instance.Memoize2 (newState |> box |> Ok) ((ids |> List.last, aggregateId))
                    
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A, 'E, 'F> storage aggregateId newState (ids |> List.last)
                    return ()
                }
        #if USING_MAILBOXPROCESSOR        
            let processor = MailBoxProcessors.Processors.Instance.GetProcessor (sprintf "%s_%s" 'A.StorageName (aggregateId.ToString()))
            MailBoxProcessors.postToTheProcessor processor command
        #else    
            command ()
        #endif    
            
    let inline runAggregateCommand<'A, 'E, 'F
        when 'A :> Aggregate<'F>
        and 'E :> Event<'A>
        and 'A : (static member Deserialize: 'F -> Result<'A, string>) 
        and 'A : (static member StorageName: string) 
        and 'A : (static member Version: string)
        and 'A : (static member SnapshotsInterval: int)
        and 'E : (static member Deserialize: 'F -> Result<'E, string>)
        and 'E : (member Serialize: 'F)
        >
        (aggregateId: Guid)
        (storage: IEventStore<'F>)
        (messageSender: StreamName -> MessageSender) 
        (command: AggregateCommand<'A, 'E>)
        =
            logger.Value.LogDebug (sprintf "runAggregateCommand %A,  %A, id: %A" 'A.StorageName command aggregateId)
            runAggregateCommandMd<'A, 'E, 'F> aggregateId storage messageSender Metadata.Empty command
    
     
    // the "force" version of running N Commands has been improved and so
    // it is safe to use them in place of the non-force version
    // even though some aggregateId is repeated in parameters
    // i.e. more commands hit the same aggregate (particularly
    // tricky as the aggregate state and so the behavior of a command may
    // depend on the result of some other commands)
    
    let inline forceRunNAggregateCommandsMd<'A1, 'E1, 'F
        when 'A1 :> Aggregate<'F>
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        >
        (aggregateIds: List<Guid>)
        (eventStore: IEventStore<'F>)
        (messageSenders: StreamName -> MessageSender) 
        (md: Metadata)
        (commands: List<AggregateCommand<'A1, 'E1>>)
        =
            logger.Value.LogDebug "forceRunNAggregateCommands" 
            let commands = fun () ->
                result {
                    let aggregateIdsWithCommands =
                        List.zip aggregateIds commands
                        |> List.groupBy fst
                        |> List.map (fun (id, cmds) -> (id, cmds |> List.map snd))
                   
                    let! uniqueInitialStates =
                        aggregateIdsWithCommands
                        |> List.traverseResultM (fun (id, cmds) -> getAggregateFreshState<'A1, 'E1, 'F> id eventStore)
                   
                    let uniqueStatesOnly =
                        uniqueInitialStates
                        |>> fun (_, state) -> state
                        
                    let multiCommands =
                        aggregateIdsWithCommands
                        |>> fun (_, cmds) -> cmds
                    
                    let statesAndMultiCommands =
                        List.zip uniqueStatesOnly multiCommands    
                    
                    let! newStatesAndEvents =
                        statesAndMultiCommands
                        |> List.traverseResultM (fun (state, cmds) -> foldCommands (state |> unbox) cmds)
                        
                    let newEvents =
                        newStatesAndEvents
                        |>> snd
                    
                    let newStates =
                        newStatesAndEvents
                        |>> fst
                        
                    let serializedNewEvents =
                        newEvents
                        |>> fun x -> x |>> fun (z: 'E1) -> z.Serialize
                        
                    let initialStatesEventIds =
                        uniqueInitialStates
                        |>> fst
                        
                    let uniqueAggregateIds =
                        aggregateIdsWithCommands
                        |>> fst
                        
                    let currentEventIdsEventsAndAggregateIds =
                        List.zip3 initialStatesEventIds serializedNewEvents uniqueAggregateIds
                        |>> fun (currentStateEventId, events, id) -> (currentStateEventId, events, 'A1.Version, 'A1.StorageName, id)
                        
                    let! dbEventIds =
                        currentEventIdsEventsAndAggregateIds
                        |> eventStore.MultiAddAggregateEventsMd md
                    
                    for i in 0 .. (uniqueAggregateIds.Length - 1) do
                        AggregateCache2.Instance.Memoize2 (newStates.[i] |> box |> Ok) (dbEventIds.[i] |> List.last, uniqueAggregateIds.[i])
                        mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore uniqueAggregateIds.[i] newStates.[i] (dbEventIds.[i] |> List.last) |> ignore
                        
                    let queueName = 'A1.Version + 'A1.StorageName
                    let sender = messageSenders queueName
                    
                    let aggregateIdsAndEvents =
                        List.zip uniqueAggregateIds newEvents
                    
                    let aggregateMessages =
                        List.zip3 initialStatesEventIds aggregateIdsAndEvents dbEventIds
                        |>> fun (currentStateEventId, (aggregateId, events), dbEventIds) ->
                            {
                                AggregateId = aggregateId
                                Message = MessageType<'A1, 'E1>.Events { InitEventId = currentStateEventId; EndEventId = dbEventIds |> List.last; Events = events  }
                            }.Serialize
                   
                    let sending =
                        aggregateMessages
                        |> List.iter (fun x -> sender x |> ignore)
                        
                    return ()    
                }
            
        #if USING_MAILBOXPROCESSOR    
            let lookupName = 'A1.StorageName
            let processor = MailBoxProcessors.Processors.Instance.GetProcessor lookupName
            MailBoxProcessors.postToTheProcessor processor commands
        #else    
            commands ()
        #endif
        
    let inline forceRunNAggregateCommands<'A1, 'E1, 'F
        when 'A1 :> Aggregate<'F>
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        >
        (aggregateIds: List<Guid>)
        (eventStore: IEventStore<'F>)
        (messageSender: StreamName -> MessageSender) 
        (commands: List<AggregateCommand<'A1, 'E1>>)
        =
            logger.Value.LogDebug "forceRunNAggregateCommands"
            forceRunNAggregateCommandsMd<'A1, 'E1, 'F> aggregateIds eventStore messageSender Metadata.Empty commands
                
    let inline runNAggregateCommandsMd<'A1, 'E1, 'F
        when 'A1 :> Aggregate<'F>
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        >
        (aggregateIds: List<Guid>)
        (eventStore: IEventStore<'F>)
        (messageSenders: StramName -> MessageSender)
        (md: Metadata)
        (commands: List<AggregateCommand<'A1, 'E1>>)
        =
            logger.Value.LogDebug "runNAggregateCommands"
            let aggregateIdsAreUnique = aggregateIds |> List.distinct |> List.length = aggregateIds.Length
            if (not aggregateIdsAreUnique) then
                Error "aggregateIds are not unique"
            else
            let commands = fun () ->
                result {
                    let! states =
                        aggregateIds
                        |> List.traverseResultM (fun id -> getAggregateFreshState<'A1, 'E1, 'F> id eventStore)
                    let states' = 
                        states 
                        |>> fun (_, state) -> state
                    let lastEventIds =
                        states
                        |>> fun (eventId, _) -> eventId
                    let statesAndCommands =
                        List.zip states' commands
                    let! stateEvents =
                        statesAndCommands
                        |>> fun (state, command) -> (command.Execute (state |> unbox))
                        |> List.traverseResultM id
                    let newStates =
                        stateEvents 
                        |>> fun (state, _) -> state
                    let events =
                        stateEvents
                        |>> fun (_, events) -> events
                        
                    let serializedEvents =
                        events 
                        |>> fun x -> x |>> fun (z: 'E1) -> z.Serialize
                    let currentStateEventIdEventsAndAggregateIds =
                        List.zip3 lastEventIds serializedEvents aggregateIds
                        |>> fun (eventId, events, id) -> (eventId, events, 'A1.Version, 'A1.StorageName, id)
                    let! storedEventIds =
                        eventStore.MultiAddAggregateEventsMd md currentStateEventIdEventsAndAggregateIds
                    
                    for i in 0..(aggregateIds.Length - 1) do
                        AggregateCache2.Instance.Memoize2 (newStates.[i] |> box |> Ok) (storedEventIds.[i] |> List.last, aggregateIds.[i])
                        mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore aggregateIds.[i] newStates.[i] (storedEventIds.[i] |> List.last) |> ignore
                        
                    let queueName = 'A1.Version + 'A1.StorageName
                    let sender = messageSenders queueName
                    
                    let aggregateIdsAndEvents =
                        List.zip aggregateIds events
                   
                    let aggregateMessages =
                        List.zip3 lastEventIds aggregateIdsAndEvents storedEventIds
                        |> List.map (fun (eventId, (aggregateId, events), storedEventIds) ->
                            {
                                AggregateId = aggregateId
                                Message = MessageType<'A1, 'E1>.Events {InitEventId = eventId; EndEventId = storedEventIds |> List.last; Events = events}
                            }.Serialize
                        )
                        
                    let sending =
                        aggregateMessages
                        |> List.iter (fun x -> sender x |> ignore) 
                        
                    return ()    
                }
        #if USING_MAILBOXPROCESSOR    
            let lookupName = 'A1.StorageName
            let processor = MailBoxProcessors.Processors.Instance.GetProcessor lookupName
            MailBoxProcessors.postToTheProcessor processor commands
        #else    
            commands ()
        #endif    
                
    let inline runNAggregateCommands<'A1, 'E1, 'F
        when 'A1 :> Aggregate<'F>
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        >
        (aggregateIds: List<Guid>)
        (eventStore: IEventStore<'F>)
        (messageSender: StreamName -> MessageSender)
        (commands: List<AggregateCommand<'A1, 'E1>>)
        =
            logger.Value.LogDebug "runNAggregateCommands"
            runNAggregateCommandsMd<'A1, 'E1, 'F> aggregateIds eventStore messageSender Metadata.Empty commands
    
    let inline runTwoAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'F
        when 'A1 :> Aggregate<'F>
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        and 'A2 :> Aggregate<'F>
        and 'E2 :> Event<'A2>
        and 'E2 : (member Serialize: 'F)
        and 'E2 : (static member Deserialize: 'F -> Result<'E2, string>)
        and 'A2 : (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2 : (static member SnapshotsInterval: int)
        and 'A2 : (static member StorageName: string)
        and 'A2 : (static member Version: string)
        >
        (aggregateId1: Guid)
        (aggregateId2: Guid)
        (eventStore: IEventStore<'F>)
        (messageSenders: string -> MessageSender)     
        (md: Metadata)
        (command1: AggregateCommand<'A1, 'E1>)
        (command2: AggregateCommand<'A2, 'E2>)
        =
            logger.Value.LogDebug "runTwoAggregateCommandsMd"
            result {
                let! firstExecutedCommand =  preExecuteAggregateCommandMd<'A1, 'E1, 'F> aggregateId1 eventStore messageSenders md command1
                let! secondExecutedCommand = preExecuteAggregateCommandMd<'A2, 'E2, 'F> aggregateId2 eventStore messageSenders md command2
                
                let! (_, stateA1) = getAggregateFreshState<'A1, 'E1, 'F> aggregateId1 eventStore //  |> unbox
                let! (_, stateA2) = getAggregateFreshState<'A2, 'E2, 'F> aggregateId2 eventStore // |> unbox
                
                let stateA1 = stateA1 |> unbox
                let stateA2 = stateA2 |> unbox
                
                let! eventsA1 = command1.Execute stateA1
                let! eventsA2 = command2.Execute stateA2
                
                let! (_, eventsA1) = command1.Execute stateA1
                let! (_, eventsA2) = command2.Execute stateA2
                let! ids =
                    storeMultipleEvents eventStore messageSenders
                        [firstExecutedCommand
                         secondExecutedCommand]
                AggregateCache2.Instance.Memoize2 (firstExecutedCommand.NewState |> Ok) ((ids.[0] |> List.last, aggregateId1))
                AggregateCache2.Instance.Memoize2 (secondExecutedCommand.NewState |> Ok) ((ids.[1] |> List.last, aggregateId2))
                let queueNameA1 = 'A1.Version + 'A1.StorageName
                let queueNameA2 = 'A2.Version + 'A2.StorageName
                let senderA1 = messageSenders queueNameA1
                let senderA2 = messageSenders queueNameA2
                
                let messageA1 =
                    MessageType<'A1, 'E1>.Events
                        {
                            InitEventId = firstExecutedCommand.EventId
                            EndEventId = ids.[0] |> List.last
                            Events = eventsA1
                        }
                let messageA2 =
                    MessageType<'A2, 'E2>.Events
                        {
                            InitEventId = secondExecutedCommand.EventId
                            EndEventId = ids.[1] |> List.last
                            Events = eventsA2
                        }
                
                let aggregateMessage =
                    {
                        AggregateId = aggregateId1
                        Message = messageA1
                    }.Serialize
                    
                let aggregateMessage2 =
                    {
                        AggregateId = aggregateId2
                        Message = messageA2
                    }.Serialize
               
                let sent1 = senderA1 aggregateMessage
                let sent2 = senderA2 aggregateMessage2    
                
                let _ = mkAggregateSnapshotIfIntervalPassed3<'F>
                                eventStore
                                aggregateId1
                                firstExecutedCommand.Version
                                firstExecutedCommand.StorageName
                                (ids.[0] |> List.last)
                                firstExecutedCommand.SnapshotsInterval
                                (firstExecutedCommand.NewState :?> Aggregate<'F>).Serialize
                                 
                let _ = mkAggregateSnapshotIfIntervalPassed3<'F>
                                eventStore
                                aggregateId1
                                'A2.Version
                                'A2.StorageName
                                (ids.[1] |> List.last)
                                secondExecutedCommand.SnapshotsInterval
                                (secondExecutedCommand.NewState :?> Aggregate<'F>).Serialize
                return ()
            }
 
    let convertToTargetType (targetType: Type) (value: obj) =
        try
            // For value types, use Convert.ChangeType
            if targetType.IsValueType || targetType = typeof<string> then
                Convert.ChangeType(value, targetType)
            else
                // For reference types, you can use direct cast if the type matches
                // or implement custom conversion logic
                value
        with
        | _ -> 
            // Handle conversion failure
            failwithf "Failed to convert %A to type %s" value targetType.FullName
 
     
    let inline runPreExecutedAggregateCommands<'F> (preExecutedAggregateCommands: List<PreExecutedAggregateCommand<_,'F>>) (eventStore: IEventStore<'F>) (eventBroker: string -> MessageSender) =
        logger.Value.LogDebug "runPreExecutedCommands"
        result {
            let! storedIds =
                storeMultipleEvents eventStore eventBroker
                    preExecutedAggregateCommands
            for i in 0..(preExecutedAggregateCommands.Length - 1) do
                AggregateCache2.Instance.Memoize2
                    (preExecutedAggregateCommands.[i].NewState |> Ok) (storedIds.[i] |> List.last, preExecutedAggregateCommands.[i].AggregateId)
            
            for i in 0..(preExecutedAggregateCommands.Length - 1) do
                mkAggregateSnapshotIfIntervalPassed3<'F>
                    eventStore
                    preExecutedAggregateCommands.[i].AggregateId
                    preExecutedAggregateCommands.[i].Version
                    preExecutedAggregateCommands.[i].StorageName
                    (storedIds.[i] |> List.last)
                    preExecutedAggregateCommands.[i].SnapshotsInterval
                    (preExecutedAggregateCommands.[i].NewState :?> Aggregate<'F>).Serialize
                |> ignore
            
            // wip
            let queueNames =
                preExecutedAggregateCommands
                |> List.map (fun x -> x.Version + x.StorageName)
            
            // let eventTypes =
            //     preExecutedAggregateCommands
            //     |> List.map (fun x -> x.EventType)
            //
            // let initEventIdEndEventIdAndAndSerializedEvents =
            //     List.zip3 (storedIds |>> List.last) (preExecutedAggregateCommands |>> _.EventId) (preExecutedAggregateCommands |>> _.SerializedEvents)
            //     
            // let deserializers = preExecutedAggregateCommands |>> _.EventType.GetProperty("Deserialize") //, BindingFlags.Public ||| BindingFlags.Static)
            // let initEventIdAndEventIdAndDeserializers =
            //     List.zip initEventIdEndEventIdAndAndSerializedEvents deserializers
            //
            // let eventMessages =
            //     initEventIdAndEventIdAndDeserializers
            //     |> List.map (fun ((initEventId, eventId, serializedEvents), deserializer) ->
            //         let deserializedEvents = serializedEvents |> List.map (fun x -> deserializer.GetMethod.Invoke(null, ([x |> box] |> List.toArray)) :?> 'E)
            //         (initEventId, eventId, deserializedEvents)
            //     )
            //     
            // let eventMessages' =
            //     eventMessages
            //     |>
            //     List.map
            //         (fun (x, y, z)  ->
            //             {
            //                 InitEventId = x
            //                 EndEventId = y
            //                 Events = z |> unbox
            //             } 
            //         )
            //
            // let eventMessagesWithTypes =
            //     List.zip eventMessages' eventTypes
            //     // |> List.map (fun (x, y) -> {InitEventId = x.InitEventId; EndEventId = x.EndEventId; Events = x.Events |> List.map (fun x' -> x' :?> )  })
            //     // |> List.map (fun (x, y) -> {InitEventId = x.InitEventId; EndEventId = x.EndEventId; Events = x.Events |> List.map (fun x' -> convertToTargetType y x')  })
             
            // let senders =
            //     queueNames
            //     |> List.map eventBroker
            
            return ()
        }
     
    let inline runPreExecutedAggregateCommands2<'F> (preExecutedAggregateCommands: List<PreExecutedAggregateCommand<_,'F>>) (eventStore: IEventStore<'F>) (eventBroker: string -> MessageSender) =
        logger.Value.LogDebug "runPreExecutedCommands"
        result {
            let! storedIds =
                storeMultipleEvents eventStore eventBroker
                    preExecutedAggregateCommands
            for i in 0..(preExecutedAggregateCommands.Length - 1) do
                AggregateCache2.Instance.Memoize2
                    (preExecutedAggregateCommands.[i].NewState |> Ok) (storedIds.[i] |> List.last, preExecutedAggregateCommands.[i].AggregateId)
            
            for i in 0..(preExecutedAggregateCommands.Length - 1) do
                mkAggregateSnapshotIfIntervalPassed3<'F>
                    eventStore
                    preExecutedAggregateCommands.[i].AggregateId
                    preExecutedAggregateCommands.[i].Version
                    preExecutedAggregateCommands.[i].StorageName
                    (storedIds.[i] |> List.last)
                    preExecutedAggregateCommands.[i].SnapshotsInterval
                    (preExecutedAggregateCommands.[i].NewState :?> Aggregate<'F>).Serialize
                |> ignore
            
            // wip
            // let queueNames =
            //     preExecutedAggregateCommands
            //     |> List.map (fun x -> x.StorageName + x.Version)

            let result =
                storedIds |>> List.last
            return result    
        }         
    [<Obsolete("Use runTwoAggregateCommandsMd instead")>]
    let inline runTwoAggregateCommandsMdBack<'A1, 'E1, 'A2, 'E2, 'F
        when 'A1 :> Aggregate<'F>
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        and 'A2 :> Aggregate<'F>
        and 'E2 :> Event<'A2>
        and 'E2 : (member Serialize: 'F)
        and 'E2 : (static member Deserialize: 'F -> Result<'E2, string>)
        and 'A2 : (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2 : (static member SnapshotsInterval: int)
        and 'A2 : (static member StorageName: string)
        and 'A2 : (static member Version: string)
        >
        (aggregateId1: Guid)
        (aggregateId2: Guid)
        (eventStore: IEventStore<'F>)
        (eventBroker: IEventBroker<'F>)
        (md: Metadata)
        (command1: AggregateCommand<'A1, 'E1>)
        (command2: AggregateCommand<'A2, 'E2>)
        =
            let commands = fun () ->
                result {
                    let! eventId1, state1 = getAggregateFreshState<'A1, 'E1, 'F> aggregateId1 eventStore
                    let! eventId2, state2 = getAggregateFreshState<'A2, 'E2, 'F> aggregateId2 eventStore
                    
                    let! newState1, events1 =
                        state1
                        |> unbox
                        |> command1.Execute
                    
                    let! newState2, events2 =
                        state2
                        |> unbox
                        |> command2.Execute    
                    
                    let! newLastStateIdsList =
                        eventStore.MultiAddAggregateEventsMd
                            md 
                            [
                                (eventId1, (events1 |>> _.Serialize), 'A1.Version, 'A1.StorageName, aggregateId1)
                                (eventId2, (events2 |>> _.Serialize), 'A2.Version, 'A2.StorageName, aggregateId2)
                            ]
                    AggregateCache2.Instance.Memoize2 (newState1 |> box |> Ok) ((newLastStateIdsList.[0] |> List.last, aggregateId1))
                    AggregateCache2.Instance.Memoize2 (newState2 |> box |> Ok) ((newLastStateIdsList.[1] |> List.last, aggregateId2))
                   
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore aggregateId1 newState1 (newLastStateIdsList.[0] |> List.last)
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A2, 'E2, 'F> eventStore aggregateId2 newState2 (newLastStateIdsList.[1] |> List.last)
                    
                    return ()     
                }
        #if USING_MAILBOXPROCESSOR
            let lookupName = sprintf "%s_%s" 'A1.StorageName  'A2.StorageName
            MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) commands
        #else
            commands () 
        #endif    
   
    // can check potantially cross-aggregates invariants (i.e. related to something different than 'A1 and 'A2)
    let inline runTwoAggregateCommandsCheckingCrossAggregatesConstraintsMd<'A1, 'E1, 'A2, 'E2, 'F
        when 'A1 :> Aggregate<'F>
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        and 'A2 :> Aggregate<'F>
        and 'E2 :> Event<'A2>
        and 'E2 : (member Serialize: 'F)
        and 'E2 : (static member Deserialize: 'F -> Result<'E2, string>)
        and 'A2 : (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2 : (static member SnapshotsInterval: int)
        and 'A2 : (static member StorageName: string)
        and 'A2 : (static member Version: string)>
        
        (aggregateId1: Guid)
        (aggregateId2: Guid)
        (eventStore: IEventStore<'F>)
        (messageSenders: StreamName -> MessageSender)
        (md: Metadata)
        (command1: AggregateCommand<'A1, 'E1>)
        (command2: AggregateCommand<'A2, 'E2>)
        (crossAggregatesConstraint:'A1*'A2 -> Result<unit, string>)
        =
            let commands = fun () ->
                result {
                    let! eventId1, state1 = getAggregateFreshState<'A1, 'E1, 'F> aggregateId1 eventStore
                    let! eventId2, state2 = getAggregateFreshState<'A2, 'E2, 'F> aggregateId2 eventStore
                    
                    let! newState1, events1 =
                        state1
                        |> unbox
                        |> command1.Execute
                    
                    let! newState2, events2 =
                        state2
                        |> unbox
                        |> command2.Execute
                    
                    let! _ =
                        crossAggregatesConstraint (newState1, newState2)
                        
                    let! newLastStateIdsList =
                        eventStore.MultiAddAggregateEventsMd
                            md 
                            [
                                (eventId1, (events1 |>> _.Serialize), 'A1.Version, 'A1.StorageName, aggregateId1)
                                (eventId2, (events2 |>> _.Serialize), 'A2.Version, 'A2.StorageName, aggregateId2)
                            ]
                    AggregateCache2.Instance.Memoize2 (newState1 |> box |> Ok) ((newLastStateIdsList.[0] |> List.last, aggregateId1))
                    AggregateCache2.Instance.Memoize2 (newState2 |> box |> Ok) ((newLastStateIdsList.[1] |> List.last, aggregateId2))

                    let queueA1 = 'A1.Version + 'A1.StorageName
                    let queueA2 = 'A2.Version + 'A2.StorageName
                    let messageA1 =
                        MessageType<'A1, 'E1>.Events
                            {
                                InitEventId = eventId1
                                EndEventId = newLastStateIdsList.[0] |> List.last
                                Events = events1
                            }
                    let messageA2 =
                        MessageType<'A2, 'E2>.Events
                            {
                                InitEventId = eventId2
                                EndEventId = newLastStateIdsList.[1] |> List.last
                                Events = events2
                            }
                    let aggregateMessageA1 =
                        {
                            AggregateId = aggregateId1
                            Message = messageA1
                        }.Serialize
                    let aggregateMessageA2 =
                        {
                            AggregateId = aggregateId2
                            Message = messageA2
                        }.Serialize
                    
                    let a1Sender = messageSenders queueA1
                    let a2Sender = messageSenders queueA2
                    a1Sender aggregateMessageA1
                    a2Sender aggregateMessageA2
                    
                    // let! _ = a1Sender aggregateMessageA1
                    // let! _ = a2Sender aggregateMessageA2
                                        
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore aggregateId1 newState1 (newLastStateIdsList.[0] |> List.last)
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A2, 'E2, 'F> eventStore aggregateId2 newState2 (newLastStateIdsList.[1] |> List.last)
                    
                    return ()     
                }
        #if USING_MAILBOXPROCESSOR
            let lookupName = sprintf "%s_%s" 'A1.StorageName  'A2.StorageName
            MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) commands
        #else
            commands () 
        #endif    
            
    let inline runTwoAggregateCommands<'A1, 'E1, 'A2, 'E2, 'F
        when 'A1 :> Aggregate<'F>
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        and 'A2 :> Aggregate<'F>
        and 'E2 :> Event<'A2>
        and 'E2 : (member Serialize: 'F)
        and 'E2 : (static member Deserialize: 'F -> Result<'E2, string>)
        and 'A2 : (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2 : (static member SnapshotsInterval: int)
        and 'A2 : (static member StorageName: string)
        and 'A2 : (static member Version: string)
        >
        (aggregateId1: Guid)
        (aggregateId2: Guid)
        (eventStore: IEventStore<'F>)
        (messageSender: string -> MessageSender)
        (command1: AggregateCommand<'A1, 'E1>)
        (command2: AggregateCommand<'A2, 'E2>)
        =
            runTwoAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'F> aggregateId1 aggregateId2 eventStore messageSender String.Empty command1 command2
   
    // the "force" version of forcing running N Commands has been improved and so
    // it is safe to use them in place of the non-force version
    // using the same aggregateId in more different commands
    // note going to avoid eventbroker calls. Other system
    
    let inline forceRunTwoNAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'F
        when 'A1 :> Aggregate<'F>
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        and 'A2 :> Aggregate<'F>
        and 'E2 :> Event<'A2>
        and 'E2 : (member Serialize: 'F)
        and 'E2 : (static member Deserialize: 'F -> Result<'E2, string>)
        and 'A2 : (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2 : (static member SnapshotsInterval: int)
        and 'A2 : (static member StorageName: string)
        and 'A2 : (static member Version: string)
        >
        (aggregateIds1: List<Guid>)
        (aggregateIds2: List<Guid>)
        (eventStore: IEventStore<'F>)
        (messageSenders: string -> MessageSender)
        (md: Metadata)
        (command1: List<AggregateCommand<'A1, 'E1>>)
        (command2: List<AggregateCommand<'A2, 'E2>>)
        =
            logger.Value.LogDebug "forceRunTwoNAggregateCommands"
            let commands = fun () ->
                result {
                    let aggregateIdsWithCommands1 =
                        List.zip aggregateIds1 command1
                        |> List.groupBy fst
                        |> List.map (fun (id, cmds) -> id, cmds |> List.map snd)    
                   
                    let uniqueAggregateIds1 =
                        aggregateIdsWithCommands1
                        |>> fst
                        
                    let aggregateIdsWithCommands2 =
                        List.zip aggregateIds2 command2
                        |> List.groupBy fst
                        |> List.map (fun (id, cmds) -> id, cmds |> List.map snd)    
                       
                    let uniqueAggregateIds2 =
                        aggregateIdsWithCommands2
                        |>> fst
                        
                    let! uniqueInitialstates1 =
                        aggregateIdsWithCommands1
                        |> List.traverseResultM (fun (id, _) -> getAggregateFreshState<'A1, 'E1, 'F> id eventStore)
                        
                    let! uniqueInitialstates2 =
                        aggregateIdsWithCommands2
                        |> List.traverseResultM (fun (id, _) -> getAggregateFreshState<'A2, 'E2, 'F> id eventStore)
                        
                    let uniqueInitialStatesOnly1 =
                        uniqueInitialstates1 
                        |>> fun (_, state) -> state
                        
                    let uniqueInitialStatesOnly2 =
                        uniqueInitialstates2 
                        |>> fun (_, state) -> state
                        
                    let multicommands1 =
                        aggregateIdsWithCommands1
                        |>> fun (_, cmds) -> cmds
                        
                    let multicommands2 =
                        aggregateIdsWithCommands2
                        |>> fun (_, cmds) -> cmds
                        
                    let initialStatesAndMultiCommands1 =
                        List.zip uniqueInitialStatesOnly1 multicommands1
                    let initialStatesAndMultiCommands2 =
                        List.zip uniqueInitialStatesOnly2 multicommands2
                  
                    let! newStatesAndEvents1 =
                        initialStatesAndMultiCommands1
                        |> List.traverseResultM (fun (state, commands) -> foldCommands (state |> unbox) commands)
                        
                    let! newStatesAndEvents2 =
                        initialStatesAndMultiCommands2
                        |> List.traverseResultM (fun (state, commands) -> foldCommands (state |> unbox) commands)
                        
                    let generatedEvents1 =
                        newStatesAndEvents1
                        |>> snd
                    
                    let newStates1 =
                        newStatesAndEvents1
                        |>> fst
                        
                    let newStates2 =
                        newStatesAndEvents2
                        |>> fst    
                        
                    let generatedEvents2 =
                        newStatesAndEvents2
                        |>> snd
                        
                    let serializedEvents1 =
                        generatedEvents1
                        |>> fun x -> x |>> fun (z: 'E1) -> z.Serialize
                    
                    let serializedEvents2 =
                        generatedEvents2
                        |>> fun x -> x |>> fun (z: 'E2) -> z.Serialize
                        
                    let initialStateEventIds1 =
                        uniqueInitialstates1
                        |>> fst
                        
                    let initialStateEventIds2 =
                        uniqueInitialstates2
                        |>> fst
                    
                    let aggregateIds1 =
                        aggregateIdsWithCommands1
                        |>> fst
                        
                    let aggregateIds2 =
                        aggregateIdsWithCommands2
                        |>> fst
                    
                    let initialEventIds1Events1AndAggregateIds1 =
                        List.zip3 initialStateEventIds1 serializedEvents1 aggregateIds1
                        |>> fun (eventId, events, id) -> (eventId, events, 'A1.Version, 'A1.StorageName, id)
                    
                    let initialEventIds2EventIds2AndAggregateIds2 =
                        List.zip3 initialStateEventIds2 serializedEvents2 aggregateIds2
                        |>> fun (eventId, events, id) -> (eventId, events, 'A2.Version, 'A2.StorageName, id)
                    
                    let allPacked = initialEventIds1Events1AndAggregateIds1 @ initialEventIds2EventIds2AndAggregateIds2
                    
                    let! dbNewStatesEventIds =
                        allPacked
                        |> eventStore.MultiAddAggregateEventsMd md
                       
                    let newDbBasedEventIds1 =
                        dbNewStatesEventIds
                        |> List.take uniqueAggregateIds1.Length
                    let newDbBasedEventIds2 =
                        dbNewStatesEventIds
                        |> List.skip uniqueAggregateIds1.Length    
                  
                    let queueNameA1 = 'A1.Version + 'A1.StorageName
                    let queueNameA2 = 'A2.Version + 'A2.StorageName
                    let senderA1 = messageSenders queueNameA1
                    let senderA2 = messageSenders queueNameA2
                    
                    let aggregateMessagesA1 =
                        List.zip3 initialStateEventIds1 newStatesAndEvents1 newDbBasedEventIds1
                        |> List.map
                            (fun (eventId, (state, events), storedEventIds) ->
                                {
                                    AggregateId = state.Id
                                    Message = MessageType<'A1, 'E1>.Events {InitEventId = eventId; EndEventId = storedEventIds |> List.last; Events = events} // EndEventId should be the last like eventIds1'.[i] |> List.last
                                }.Serialize
                            
                            )
                        
                    let aggregateMessagesA2 =
                        List.zip3 initialStateEventIds2 newStatesAndEvents2 newDbBasedEventIds2
                        |> List.map
                            (fun (eventId, (state, events), storedEventIds) ->
                                {
                                    AggregateId = state.Id
                                    Message = MessageType<'A2, 'E2>.Events {InitEventId = eventId; EndEventId = storedEventIds |> List.last; Events = events} // TODO: foxus EndEventId should be the last like eventIds1'.[i] |> List.last
                                }.Serialize
                            )
                    
                    let _ =
                        aggregateMessagesA1
                        |> List.iter (fun message -> senderA1 message |> ignore)
                        
                    let _ =
                        aggregateMessagesA2
                        |> List.iter (fun message -> senderA2 message |> ignore)
                     
                    let doCacheResults = 
                        fun () ->
                            for i in 0 .. (uniqueAggregateIds1.Length - 1) do
                                AggregateCache2.Instance.Memoize2 (newStates1.[i] |> box |> Ok) (newDbBasedEventIds1.[i] |> List.last, uniqueAggregateIds1.[i])
                                mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore uniqueAggregateIds1.[i] newStates1.[i] (newDbBasedEventIds1.[i] |> List.last) |> ignore
                                
                            for i in 0 .. (uniqueAggregateIds2.Length - 1) do
                                AggregateCache2.Instance.Memoize2 (newStates2.[i] |> box |> Ok) (newDbBasedEventIds2.[i] |> List.last, uniqueAggregateIds2.[i])
                                mkAggregateSnapshotIfIntervalPassed2<'A2, 'E2, 'F> eventStore uniqueAggregateIds2.[i] newStates2.[i] (newDbBasedEventIds2.[i] |> List.last) |> ignore
                                
                    doCacheResults ()
                    
                    let allIds = uniqueAggregateIds1 @ uniqueAggregateIds2
                    let duplicatedIds =
                        allIds
                        |> List.groupBy id
                        |> List.filter (fun (_, l) -> l.Length > 1)
                        |> List.map (fun (id, _) -> id)
                    
                    let _ =
                        aggregateIds1 |> List.iter (fun id ->
                            if (duplicatedIds |> List.contains id) then
                                AggregateCache2.Instance.Clean id
                        )
                        aggregateIds2 |> List.iter (fun id ->
                            if (duplicatedIds |> List.contains id) then
                                AggregateCache2.Instance.Clean id
                        )
                        
                    return ()
                }
        #if USING_MAILBOXPROCESSOR
            let lookupName = sprintf "%s_%s" 'A1.StorageName 'A2.StorageName // aggregateIds
            MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) commands
        #else    
            commands ()
        #endif    
            
    let inline forceRunTwoNAggregateCommands<'A1, 'E1, 'A2, 'E2, 'F
        when 'A1 :> Aggregate<'F>
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        and 'A2 :> Aggregate<'F>
        and 'E2 :> Event<'A2>
        and 'E2 : (member Serialize: 'F)
        and 'E2 : (static member Deserialize: 'F -> Result<'E2, string>)
        and 'A2 : (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2 : (static member SnapshotsInterval: int)
        and 'A2 : (static member StorageName: string)
        and 'A2 : (static member Version: string)
        >
        (aggregateIds1: List<Guid>)
        (aggregateIds2: List<Guid>)
        (eventStore: IEventStore<'F>)
        (messageSenders: string -> MessageSender)
        (command1: List<AggregateCommand<'A1, 'E1>>)
        (command2: List<AggregateCommand<'A2, 'E2>>)
        =
            logger.Value.LogDebug "forceRunTwoNAggregateCommands"
            forceRunTwoNAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'F> aggregateIds1 aggregateIds2 eventStore messageSenders String.Empty command1 command2
   
    let inline runTwoNAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'F
        when 'A1 :> Aggregate<'F>
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        and 'A2 :> Aggregate<'F>
        and 'E2 :> Event<'A2>
        and 'E2 : (member Serialize: 'F)
        and 'E2 : (static member Deserialize: 'F -> Result<'E2, string>)
        and 'A2 : (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2 : (static member SnapshotsInterval: int)
        and 'A2 : (static member StorageName: string)
        and 'A2 : (static member Version: string)
        >
        (aggregateIds1: List<Guid>)
        (aggregateIds2: List<Guid>)
        (eventStore: IEventStore<'F>)
        (messageSenders: string -> MessageSender)
        (md: Metadata)
        (command1: List<AggregateCommand<'A1, 'E1>>)
        (command2: List<AggregateCommand<'A2, 'E2>>)
        =
            logger.Value.LogDebug "runTwoNAggregateCommands"
            let aggregateId1AreUnique = aggregateIds1 |> List.distinct |> List.length = aggregateIds1.Length
            let aggregateId2AreUnique = aggregateIds2 |> List.distinct |> List.length = aggregateIds2.Length
            if (not aggregateId1AreUnique) then
                Error "aggregateIds1 are not unique"
            else if (not aggregateId2AreUnique) then
                Error "aggregateIds2 are not unique"
            else
            let commands = fun () ->
                result {
                    let! states1 =
                        aggregateIds1
                        |> List.traverseResultM (fun id -> getAggregateFreshState<'A1, 'E1, 'F> id eventStore)
                    let! states2 =
                        aggregateIds2
                        |> List.traverseResultM (fun id -> getAggregateFreshState<'A2, 'E2, 'F> id eventStore)
                        
                    let states1' =
                        states1 
                        |>> fun (_, state) -> state
                    let states2' =
                        states2 
                        |>> fun (_, state) -> state
                        
                    let eventIds1 =
                        states1
                        |>> fun (eventId, _) -> eventId
                    let eventIds2 =
                        states2
                        |>> fun (eventId, _) -> eventId
                        
                    let statesAndCommands1 =
                        List.zip states1' command1
                    let statesAndCommands2 =
                        List.zip states2' command2
                        
                    let! events1 =
                        statesAndCommands1
                        |>> fun (state, command) -> command.Execute (state |> unbox)
                        |> List.traverseResultM id
                    let! events2 =
                        statesAndCommands2
                        |>> fun (state, command) -> command.Execute (state |> unbox)
                        |> List.traverseResultM id
                    let serializedEvents1 =
                        events1 
                        |>> fun (_, x) -> x |>> fun (z: 'E1) -> z.Serialize
                    let serializedEvents2 =
                        events2 
                        |>> fun (_, x) -> x |>> fun (z: 'E2) -> z.Serialize
                        
                    let! statesAndEvents1 =
                        statesAndCommands1
                        |>> fun (state, command) -> (command.Execute (state |> unbox))
                        |> List.traverseResultM id
                    let! statesAndEvents2 =
                        statesAndCommands2
                        |>> fun (state, command) -> (command.Execute (state |> unbox))
                        |> List.traverseResultM id

                    let newStates1: List<'A1> =
                        statesAndEvents1
                        |>> fun (state, _) -> state
                        
                    let newStates2: List<'A2> =
                        statesAndEvents2
                        |>> fun (state, _) -> state    
                        
                    let packParametersForDb1 =
                        List.zip3 eventIds1 serializedEvents1 aggregateIds1
                        |>> fun (eventId, events, id) -> (eventId, events, 'A1.Version, 'A1.StorageName, id)
                    let packParametersForDb2 =
                        List.zip3 eventIds2 serializedEvents2 aggregateIds2
                        |>> fun (eventId, events, id) -> (eventId, events, 'A2.Version, 'A2.StorageName, id)
                        
                    let allPacked = packParametersForDb1 @ packParametersForDb2
                    let! eventIds =
                        allPacked
                        |> eventStore.MultiAddAggregateEventsMd md
                        
                    let eventIds1' = eventIds |> List.take aggregateIds1.Length
                    let eventIds2' = eventIds |> List.skip aggregateIds1.Length
                    
                    let queueNameA1 = 'A1.Version + 'A1.StorageName
                    let queueNameA2 = 'A2.Version + 'A2.StorageName
                    let senderA1 = messageSenders queueNameA1
                    let senderA2 = messageSenders queueNameA2
                    
                    let aggregateMessagesA1 =
                        List.zip3 eventIds1 events1 eventIds1'
                        |> List.map
                               (fun (eventId, (state, events), storedEventIds) ->
                                    {
                                        AggregateId = state.Id
                                        Message = MessageType<'A1, 'E1>.Events {InitEventId = eventId; EndEventId = storedEventIds |> List.last; Events = events} // EndEventId should be the last like eventIds1'.[i] |> List.last 
                                    }.Serialize
                               )
                        
                    let aggregateMessagesA2 =
                        List.zip3 eventIds2 events2 eventIds2'
                        |> List.map
                               (fun (eventId, (state, events), storedEventIds) ->
                                    {
                                        AggregateId = state.Id
                                        Message = MessageType<'A2, 'E2>.Events {InitEventId = eventId; EndEventId = storedEventIds |> List.last; Events = events} // TODO: foxus EndEventId should be the last like eventIds1'.[i] |> List.last 
                                    }.Serialize
                               )
                    let _ =
                        aggregateMessagesA1
                        |> List.iter (fun message -> senderA1 message |> ignore)
                    
                    let _ =
                        aggregateMessagesA2
                        |> List.iter (fun message -> senderA2 message |> ignore)
                            
                    for i in 0..(aggregateIds1.Length - 1) do
                        AggregateCache2.Instance.Memoize2 (newStates1.[i] |> box |> Ok) ((eventIds1'.[i] |> List.last, aggregateIds1.[i]))
                        mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore aggregateIds1.[i] newStates1.[i] (eventIds1'.[i] |> List.last) |> ignore
                    
                    for i in 0..(aggregateIds2.Length - 1) do
                       AggregateCache2.Instance.Memoize2 (newStates2.[i] |> box |> Ok) ((eventIds2'.[i] |> List.last, aggregateIds2.[i]))
                       mkAggregateSnapshotIfIntervalPassed2<'A2, 'E2, 'F> eventStore aggregateIds2.[i] newStates2.[i] (eventIds2'.[i] |> List.last) |> ignore
                        
                    return ()
                }
        #if USING_MAILBOXPROCESSOR   
            let lookupName = sprintf "%s_%s" 'A1.StorageName 'A2.StorageName // aggregateIds
            MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) commands
        #else
            commands ()
        #endif
        
    let inline runTwoNAggregateCommands<'A1, 'E1, 'A2, 'E2, 'F
        when 'A1 :> Aggregate<'F>
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        and 'A2 :> Aggregate<'F>
        and 'E2 :> Event<'A2>
        and 'E2 : (member Serialize: 'F)
        and 'E2 : (static member Deserialize: 'F -> Result<'E2, string>)
        and 'A2 : (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2 : (static member SnapshotsInterval: int)
        and 'A2 : (static member StorageName: string)
        and 'A2 : (static member Version: string)
        >
        (aggregateIds1: List<Guid>)
        (aggregateIds2: List<Guid>)
        (eventStore: IEventStore<'F>)
        (messageSenders: string -> MessageSender)
        (command1: List<AggregateCommand<'A1, 'E1>>)
        (command2: List<AggregateCommand<'A2, 'E2>>)
        =
            logger.Value.LogDebug "runTwoNAggregateCommands"
            runTwoNAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'F> aggregateIds1 aggregateIds2 eventStore messageSenders Metadata.Empty command1 command2
    
    let inline forceRunThreeNAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'A3, 'E3, 'F
        when 'A1 :> Aggregate<'F>
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        and 'A2 :> Aggregate<'F>
        and 'E2 :> Event<'A2>
        and 'E2 : (member Serialize: 'F)
        and 'E2 : (static member Deserialize: 'F -> Result<'E2, string>)
        and 'A2 : (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2 : (static member SnapshotsInterval: int)
        and 'A2 : (static member StorageName: string)
        and 'A2 : (static member Version: string)
        and 'A3 :> Aggregate<'F>
        and 'E3 :> Event<'A3>
        and 'E3 : (member Serialize: 'F)
        and 'E3 : (static member Deserialize: 'F -> Result<'E3, string>)
        and 'A3 : (static member Deserialize: 'F -> Result<'A3, string>)
        and 'A3 : (static member SnapshotsInterval: int)
        and 'A3 : (static member StorageName: string)
        and 'A3 : (static member Version: string)
        >
        (aggregateIds1: List<Guid>)
        (aggregateIds2: List<Guid>)
        (aggregateIds3: List<Guid>)
        (eventStore: IEventStore<'F>)
        (messageSenders: StreamName -> MessageSender)
        (md: Metadata)
        (command1: List<AggregateCommand<'A1, 'E1>>)
        (command2: List<AggregateCommand<'A2, 'E2>>)
        (command3: List<AggregateCommand<'A3, 'E3>>)
        =
            logger.Value.LogDebug "forceRunThreeNAggregateCommands"
            let commands = fun () ->
                result {
                        
                    let aggregateIdsWithCommands1 =
                        List.zip aggregateIds1 command1
                        |> List.groupBy fst
                        |> List.map (fun (id, cmds) -> id, cmds |> List.map snd)
                   
                    let uniqueAggregateIds1 =
                        aggregateIdsWithCommands1
                        |>> fst
                   
                    let aggregateIdsWithCommands2 =
                        List.zip aggregateIds2 command2
                        |> List.groupBy fst
                        |> List.map (fun (id, cmds) -> id, cmds |> List.map snd)     
                   
                    let uniqueAggregateIds2 =
                        aggregateIdsWithCommands2
                        |>> fst
                    
                    let aggregateIdsWithCommands3 =
                        List.zip aggregateIds3 command3
                        |> List.groupBy fst
                        |> List.map (fun (id, cmds) -> id, cmds |> List.map snd)
                    
                    let uniqueAggregateIds3 =
                        aggregateIdsWithCommands3
                        |>> fst    
                    
                    let! uniqueInitialStates1 =
                        uniqueAggregateIds1
                        |> List.traverseResultM (fun id -> getAggregateFreshState<'A1, 'E1, 'F> id eventStore)
                        
                    let! uniqueInitialStates2 =
                        uniqueAggregateIds2
                        |> List.traverseResultM (fun id -> getAggregateFreshState<'A2, 'E2, 'F> id eventStore)
                    
                    let! uniqueInitialStates3 =
                        uniqueAggregateIds3
                        |> List.traverseResultM (fun id -> getAggregateFreshState<'A3, 'E3, 'F> id eventStore)
                        
                    let uniqueInitialStatesOnly1 =
                        uniqueInitialStates1
                        |>> fun (_, state) -> state
                    
                    let uniqueInitialStatesOnly2 =
                        uniqueInitialStates2
                        |>> fun (_, state) -> state
                        
                    let uniqueInitialStatesOnly3 =
                        uniqueInitialStates3
                        |>> fun (_, state) -> state
                   
                    let multiCommands1 =
                        aggregateIdsWithCommands1
                        |>> fun (_, cmds) -> cmds
                    
                    let multiCommands2 =
                        aggregateIdsWithCommands2
                        |>> fun (_, cmds) -> cmds
                   
                    let multiCommands3 =
                        aggregateIdsWithCommands3
                        |>> fun (_, cmds) -> cmds
                    
                    let initialStatesAndMultiCommands1 =
                        List.zip uniqueInitialStatesOnly1 multiCommands1
                    
                    let initialStatesAndMultiCommands2 =
                        List.zip uniqueInitialStatesOnly2 multiCommands2
                    
                    let initialStatesAndMultiCommands3 =
                        List.zip uniqueInitialStatesOnly3 multiCommands3
                    
                    let! newStatesAndEvents1 =
                        initialStatesAndMultiCommands1
                        |> List.traverseResultM (fun (state, cmds) -> foldCommands (state |> unbox) cmds)
                       
                    let! newStatesAndEvents2 =
                        initialStatesAndMultiCommands2
                        |> List.traverseResultM (fun (state, cmds) -> foldCommands (state |> unbox) cmds)
                    
                    let! newStatesAndEvents3 =
                        initialStatesAndMultiCommands3
                        |> List.traverseResultM (fun (state, cmds) -> foldCommands (state |> unbox) cmds)
                    
                    let newStates1 =
                        newStatesAndEvents1
                        |>> fst
                    
                    let newStates2 =
                        newStatesAndEvents2
                        |>> fst
                    
                    let newStates3 =
                        newStatesAndEvents3
                        |>> fst    
                        
                    let generatedEvents1 =
                        newStatesAndEvents1
                        |>> snd
                    
                    let generatedEvents2 =
                        newStatesAndEvents2
                        |>> snd
                    
                    let generatedEvents3 =
                        newStatesAndEvents3
                        |>> snd
                   
                    let serEvents1 =
                        generatedEvents1
                        |>> fun  x -> x |>> fun (z: 'E1) -> z.Serialize
                    
                    let serEvents2 =
                        generatedEvents2
                        |>> fun x -> x |>> fun (z: 'E2) -> z.Serialize
                    
                    let serEvents3 =
                        generatedEvents3
                        |>> fun x -> x |>> fun (z: 'E3) -> z.Serialize
                    
                    let initialEventIds1 =
                        uniqueInitialStates1
                        |>> fst
                    let initialEventIds2 =
                        uniqueInitialStates2
                        |>> fst
                    let initialEventIds3 =
                        uniqueInitialStates3
                        |>> fst
                    
                    let packParametersForDb1 =
                        List.zip3 initialEventIds1 serEvents1 uniqueAggregateIds1
                        |>> fun (eventId, events, id) -> (eventId, events, 'A1.Version, 'A1.StorageName, id)
                    let packParametersForDb2 =
                        List.zip3 initialEventIds2 serEvents2 uniqueAggregateIds2
                        |>> fun (eventId, events, id) -> (eventId, events, 'A2.Version, 'A2.StorageName, id)
                    let packParametersForDb3 =
                        List.zip3 initialEventIds3 serEvents3 uniqueAggregateIds3
                        |>> fun (eventId, events, id) -> (eventId, events, 'A3.Version, 'A3.StorageName, id)
                    
                    let allPacked = packParametersForDb1 @ packParametersForDb2 @ packParametersForDb3
                    
                    let! dbEventIds =
                        allPacked
                        |> eventStore.MultiAddAggregateEventsMd md
                    
                    let newDbBasedEventIds1 =
                        dbEventIds
                        |> List.take uniqueAggregateIds1.Length
                    let newDbBasedEventIds2 =
                        dbEventIds
                        |> List.skip uniqueAggregateIds1.Length
                        |> List.take uniqueAggregateIds2.Length
                    let newDbBasedEventIds3 =
                        dbEventIds
                        |> List.skip (uniqueAggregateIds1.Length + uniqueAggregateIds2.Length)
               
                    let doCaches =
                        fun () ->
                            for i in 0 .. (uniqueAggregateIds1.Length - 1) do
                                AggregateCache2.Instance.Memoize2 (newStates1.[i] |> box |> Ok) ((newDbBasedEventIds1.[i] |> List.last, aggregateIds1.[i]))
                                mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore aggregateIds1.[i] newStates1.[i] (newDbBasedEventIds1.[i] |> List.last) |> ignore
                            
                            for i in 0 .. (uniqueAggregateIds2.Length - 1) do
                                AggregateCache2.Instance.Memoize2 (newStates2.[i] |> box |> Ok) ((newDbBasedEventIds2.[i] |> List.last, aggregateIds2.[i]))
                                mkAggregateSnapshotIfIntervalPassed2<'A2, 'E2, 'F> eventStore aggregateIds2.[i] newStates2.[i] (newDbBasedEventIds2.[i] |> List.last) |> ignore
                            
                            for i in 0 .. (uniqueAggregateIds3.Length - 1) do
                                AggregateCache2.Instance.Memoize2 (newStates3.[i] |> box |> Ok) ((newDbBasedEventIds3.[i] |> List.last, aggregateIds3.[i]))
                                mkAggregateSnapshotIfIntervalPassed2<'A3, 'E3, 'F> eventStore aggregateIds3.[i] newStates3.[i] (newDbBasedEventIds3.[i] |> List.last) |> ignore
                                
                    
                    doCaches ()        
                    
                    let allIds = aggregateIds1 @ aggregateIds2 @ aggregateIds3
                    let duplicateIds =
                        allIds
                        |> List.groupBy id
                        |> List.filter (fun (_, l) -> l.Length > 1)
                        |> List.map (fun (id, _) -> id)
                    
                    let _ =
                        aggregateIds1 |> List.iter (fun id ->
                            if (duplicateIds |> List.contains id) then
                                AggregateCache2.Instance.Clean id
                        )
                        aggregateIds2 |> List.iter (fun id ->
                            if (duplicateIds |> List.contains id) then
                                AggregateCache2.Instance.Clean id
                        )
                        aggregateIds3 |> List.iter (fun id ->
                            if (duplicateIds |> List.contains id) then
                                AggregateCache2.Instance.Clean id
                        )
                        
                    let a1Queue = 'A1.Version + 'A1.StorageName
                    let a2Queue = 'A2.Version + 'A2.StorageName
                    let a3Queue = 'A3.Version + 'A3.StorageName
                   
                    let a1Sender = messageSenders a1Queue 
                    let a2Sender = messageSenders a2Queue 
                    let a3Sender = messageSenders a3Queue
                    
                    let aggregateIdsAndEventsA1 = List.zip uniqueAggregateIds1 generatedEvents1
                    let aggregateIdsAndEventsA2 = List.zip uniqueAggregateIds2 generatedEvents2
                    let aggregateIdsAndEventsA3 = List.zip uniqueAggregateIds3 generatedEvents3
                    
                    let aggregateMessagesA1 =
                        List.zip3 initialEventIds1 aggregateIdsAndEventsA1 newDbBasedEventIds1
                        |>> fun (initialEventId, (aggregateId, events), newDbBasedEventId) ->
                            {
                                AggregateId = aggregateId
                                Message = MessageType<'A1, 'E1>.Events { InitEventId = initialEventId; Events = events; EndEventId = newDbBasedEventId |> List.last }
                            }.Serialize
                    
                    let aggregateMessagesA2 =
                        List.zip3 initialEventIds2 aggregateIdsAndEventsA2 newDbBasedEventIds2
                        |>> fun (initialEventId, (aggregateId, events), newDbBasedEventId) ->
                            {
                                AggregateId = aggregateId
                                Message = MessageType<'A2, 'E2>.Events { InitEventId = initialEventId; Events = events; EndEventId = newDbBasedEventId |> List.last }
                            }.Serialize
                    
                    let aggregateMessagesA3 =
                        List.zip3 initialEventIds3 aggregateIdsAndEventsA3 newDbBasedEventIds3
                        |>> fun (initialEventId, (aggregateId, events), newDbBasedEventId) ->
                            {
                                AggregateId = aggregateId
                                Message = MessageType<'A3, 'E3>.Events { InitEventId = initialEventId; Events = events; EndEventId = newDbBasedEventId |> List.last }
                            }.Serialize
                   
                    let sendingA1 =
                        aggregateMessagesA1
                        |> List.map (fun x ->
                            a1Sender x |> ignore     
                        )
                    let sendingA2 =
                        aggregateMessagesA2
                        |> List.map (fun x ->
                            a2Sender x |> ignore
                        )
                    let sendingA3 =
                        aggregateMessagesA3
                        |> List.map (fun x ->
                            a3Sender x |> ignore
                        )
                    
                    return ()
                }
        
        #if USING_MAILBOXPROCESSOR 
            let lookupName = sprintf "%s_%s_%s" 'A1.StorageName 'A2.StorageName 'A3.StorageName // aggregateIds
            MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) commands
        #else
            commands ()
        #endif    
                
    let inline forceRunThreeNAggregateCommands<'A1, 'E1, 'A2, 'E2, 'A3, 'E3, 'F
        when 'A1 :> Aggregate<'F>
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        and 'A2 :> Aggregate<'F>
        and 'E2 :> Event<'A2>
        and 'E2 : (member Serialize: 'F)
        and 'E2 : (static member Deserialize: 'F -> Result<'E2, string>)
        and 'A2 : (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2 : (static member SnapshotsInterval: int)
        and 'A2 : (static member StorageName: string)
        and 'A2 : (static member Version: string)
        and 'A3 :> Aggregate<'F>
        and 'E3 :> Event<'A3>
        and 'E3 : (member Serialize: 'F)
        and 'E3 : (static member Deserialize: 'F -> Result<'E3, string>)
        and 'A3 : (static member Deserialize: 'F -> Result<'A3, string>)
        and 'A3 : (static member SnapshotsInterval: int)
        and 'A3 : (static member StorageName: string)
        and 'A3 : (static member Version: string)
        >
        (aggregateIds1: List<Guid>)
        (aggregateIds2: List<Guid>)
        (aggregateIds3: List<Guid>)
        (eventStore: IEventStore<'F>)
        (messageSenders: StreamName -> MessageSender)
        (command1: List<AggregateCommand<'A1, 'E1>>)
        (command2: List<AggregateCommand<'A2, 'E2>>)
        (command3: List<AggregateCommand<'A3, 'E3>>)
        =
            logger.Value.LogDebug "runThreeNAggregateCommands"
            forceRunThreeNAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'A3, 'E3, 'F> aggregateIds1 aggregateIds2 aggregateIds3 eventStore messageSenders Metadata.Empty command1 command2 command3

    let inline runThreeNAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'A3, 'E3, 'F
        when 'A1 :> Aggregate<'F>
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        and 'A2 :> Aggregate<'F>
        and 'E2 :> Event<'A2>
        and 'E2 : (member Serialize: 'F)
        and 'E2 : (static member Deserialize: 'F -> Result<'E2, string>)
        and 'A2 : (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2 : (static member SnapshotsInterval: int)
        and 'A2 : (static member StorageName: string)
        and 'A2 : (static member Version: string)
        and 'A3 :> Aggregate<'F>
        and 'E3 :> Event<'A3>
        and 'E3 : (member Serialize: 'F)
        and 'E3 : (static member Deserialize: 'F -> Result<'E3, string>)
        and 'A3 : (static member Deserialize: 'F -> Result<'A3, string>)
        and 'A3 : (static member SnapshotsInterval: int)
        and 'A3 : (static member StorageName: string)
        and 'A3 : (static member Version: string)
        >
        (aggregateIds1: List<Guid>)
        (aggregateIds2: List<Guid>)
        (aggregateIds3: List<Guid>)
        (eventStore: IEventStore<'F>)
        (messageSenders: StreamName -> MessageSender)
        (md: Metadata)
        (command1: List<AggregateCommand<'A1, 'E1>>)
        (command2: List<AggregateCommand<'A2, 'E2>>)
        (command3: List<AggregateCommand<'A3, 'E3>>)
        =
            logger.Value.LogDebug "runThreeNAggregateCommands"
            let aggregateId1AreUnique = aggregateIds1 |> List.distinct |> List.length = aggregateIds1.Length
            let aggregateId2AreUnique = aggregateIds2 |> List.distinct |> List.length = aggregateIds2.Length
            let aggregateId3AreUnique = aggregateIds3 |> List.distinct |> List.length = aggregateIds3.Length
            if (not aggregateId1AreUnique) then
                Error "aggregateIds1 are not unique"
            else if (not aggregateId2AreUnique) then
                Error "aggregateIds2 are not unique"
            else if (not aggregateId3AreUnique) then
                Error "aggregateIds3 are not unique"
            else
                let commands = fun () ->
                    result {
                        let! states1 =
                            aggregateIds1
                            |> List.traverseResultM (fun id -> getAggregateFreshState<'A1, 'E1, 'F> id eventStore)
                        let! states2 =
                            aggregateIds2
                            |> List.traverseResultM (fun id -> getAggregateFreshState<'A2, 'E2, 'F> id eventStore)
                        let! states3 =
                            aggregateIds3
                            |> List.traverseResultM (fun id -> getAggregateFreshState<'A3, 'E3, 'F> id eventStore)
                            
                        let states1' =
                            states1 
                            |>> fun (_, state) -> state
                        let states2' =
                            states2 
                            |>> fun (_, state) -> state
                        let states3' =
                            states3 
                            |>> fun (_, state) -> state
                            
                        let eventIds1 =
                            states1
                            |>> fun (eventId, _) -> eventId
                        let eventIds2 =
                            states2
                            |>> fun (eventId, _) -> eventId
                        let eventIds3 =
                            states3
                            |>> fun (eventId, _) -> eventId
                            
                        let statesAndCommands1 =
                            List.zip states1' command1
                        let statesAndCommands2 =
                            List.zip states2' command2
                        let statesAndCommands3 =
                            List.zip states3' command3
                            
                        let! events1 =
                            statesAndCommands1
                            |>> fun (state, command) -> command.Execute (state |> unbox)
                            |> List.traverseResultM id
                        let! events2 =
                            statesAndCommands2
                            |>> fun (state, command) -> command.Execute (state |> unbox)
                            |> List.traverseResultM id
                        let! events3 =
                            statesAndCommands3
                            |>> fun (state, command) -> command.Execute (state |> unbox)
                            |> List.traverseResultM id
                        let serializedEvents1 =
                            events1 
                            |>> fun (_, x) -> x |>> fun (z: 'E1) -> z.Serialize
                        let serializedEvents2 =
                            events2 
                            |>> fun (_, x) -> x |>> fun (z: 'E2) -> z.Serialize
                        let serializedEvents3 =
                            events3 
                            |>> fun (_, x) -> x |>> fun (z: 'E3) -> z.Serialize

                        let! statesAndEvents1 =
                            statesAndCommands1
                            |>> fun (state, command) -> (command.Execute (state |> unbox))
                            |> List.traverseResultM id
                        let! statesAndEvents2 =
                            statesAndCommands2
                            |>> fun (state, command) -> (command.Execute (state |> unbox))
                            |> List.traverseResultM id
                        let! statesAndEvents3 =
                            statesAndCommands3
                            |>> fun (state, command) -> (command.Execute (state |> unbox))
                            |> List.traverseResultM id

                        let newStates1: List<'A1> =
                            statesAndEvents1
                            |>> fun (state, _) -> state
                        let newStates2: List<'A2> =
                            statesAndEvents2
                            |>> fun (state, _) -> state
                        let newStates3: List<'A3> =
                            statesAndEvents3
                            |>> fun (state, _) -> state

                        let packParametersForDb1 =
                            List.zip3 eventIds1 serializedEvents1 aggregateIds1
                            |>> fun (eventId, events, id) -> (eventId, events, 'A1.Version, 'A1.StorageName, id)
                        let packParametersForDb2 =
                            List.zip3 eventIds2 serializedEvents2 aggregateIds2
                            |>> fun (eventId, events, id) -> (eventId, events, 'A2.Version, 'A2.StorageName, id)
                        let packParametersForDb3 =
                            List.zip3 eventIds3 serializedEvents3 aggregateIds3
                            |>> fun (eventId, events, id) -> (eventId, events, 'A3.Version, 'A3.StorageName, id)

                        let allPacked = packParametersForDb1 @ packParametersForDb2 @ packParametersForDb3
                        let! eventIds =
                            allPacked
                            |> eventStore.MultiAddAggregateEventsMd md
                        let eventIds1 = eventIds |> List.take aggregateIds1.Length
                        let eventIds2 = eventIds |> List.skip aggregateIds1.Length |> List.take aggregateIds2.Length
                        let eventIds3 = eventIds |> List.skip (aggregateIds1.Length + aggregateIds2.Length)

                        for i in 0..(aggregateIds1.Length - 1) do
                            AggregateCache2.Instance.Memoize2 (newStates1.[i] |> box |> Ok) ((eventIds1.[i] |> List.last, aggregateIds1.[i]))
                            mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore aggregateIds1.[i] newStates1.[i] (eventIds1.[i] |> List.last) |> ignore
                        for i in 0..(aggregateIds2.Length - 1) do
                            AggregateCache2.Instance.Memoize2 (newStates2.[i] |> box |> Ok) ((eventIds2.[i] |> List.last, aggregateIds2.[i]))
                            mkAggregateSnapshotIfIntervalPassed2<'A2, 'E2, 'F> eventStore aggregateIds2.[i] newStates2.[i] (eventIds2.[i] |> List.last) |> ignore
                        for i in 0..(aggregateIds3.Length - 1) do
                            AggregateCache2.Instance.Memoize2 (newStates3.[i] |> box |> Ok) ((eventIds3.[i] |> List.last, aggregateIds3.[i]))
                            mkAggregateSnapshotIfIntervalPassed2<'A3, 'E3, 'F> eventStore aggregateIds3.[i] newStates3.[i] (eventIds3.[i] |> List.last) |> ignore

                        return ()
                    }
            #if USING_MAILBOXPROCESSOR     
                let lookupName = sprintf "%s_%s_%s" 'A1.StorageName 'A2.StorageName 'A3.StorageName // aggregateIds
                MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) commands
            #else    
                commands ()
            #endif    
    let inline runThreeNAggregateCommands<'A1, 'E1, 'A2, 'E2, 'A3, 'E3, 'F
        when 'A1 :> Aggregate<'F>
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        and 'A2 :> Aggregate<'F>
        and 'E2 :> Event<'A2>
        and 'E2 : (member Serialize: 'F)
        and 'E2 : (static member Deserialize: 'F -> Result<'E2, string>)
        and 'A2 : (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2 : (static member SnapshotsInterval: int)
        and 'A2 : (static member StorageName: string)
        and 'A2 : (static member Version: string)
        and 'A3 :> Aggregate<'F>
        and 'E3 :> Event<'A3>
        and 'E3 : (member Serialize: 'F)
        and 'E3 : (static member Deserialize: 'F -> Result<'E3, string>)
        and 'A3 : (static member Deserialize: 'F -> Result<'A3, string>)
        and 'A3 : (static member SnapshotsInterval: int)
        and 'A3 : (static member StorageName: string)
        and 'A3 : (static member Version: string)
        >
        (aggregateIds1: List<Guid>)
        (aggregateIds2: List<Guid>)
        (aggregateIds3: List<Guid>)
        (eventStore: IEventStore<'F>)
        (messageSenders: StreamName -> MessageSender)
        (command1: List<AggregateCommand<'A1, 'E1>>)
        (command2: List<AggregateCommand<'A2, 'E2>>)
        (command3: List<AggregateCommand<'A3, 'E3>>)
        =
            logger.Value.LogDebug "runThreeNAggregateCommands"
            runThreeNAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'A3, 'E3, 'F> aggregateIds1 aggregateIds2 aggregateIds3 eventStore messageSenders Metadata.Empty command1 command2 command3
    
    let inline runThreeAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'A3, 'E3, 'F
        when 'A1 :> Aggregate<'F>
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        and 'A2 :> Aggregate<'F>
        and 'E2 :> Event<'A2>
        and 'E2 : (member Serialize: 'F)
        and 'E2 : (static member Deserialize: 'F -> Result<'E2, string>)
        and 'A2 : (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2 : (static member SnapshotsInterval: int)
        and 'A2 : (static member StorageName: string)
        and 'A2 : (static member Version: string)
        and 'A3 :> Aggregate<'F>
        and 'E3 :> Event<'A3>
        and 'E3 : (member Serialize: 'F)
        and 'E3 : (static member Deserialize: 'F -> Result<'E3, string>)
        and 'A3 : (static member Deserialize: 'F -> Result<'A3, string>)
        and 'A3 : (static member SnapshotsInterval: int)
        and 'A3 : (static member StorageName: string)
        and 'A3 : (static member Version: string)
        >
        (aggregateId1: Guid)
        (aggregateId2: Guid)
        (aggregateId3: Guid)
        (eventStore: IEventStore<'F>)
        (messageSenders: string -> MessageSender)
        (metadata: Metadata)
        (command1: AggregateCommand<'A1, 'E1>)
        (command2: AggregateCommand<'A2, 'E2>)
        (command3: AggregateCommand<'A3, 'E3>)
        =
            logger.Value.LogDebug "runThreeAggregateCommandsMdRefactor"
            result
                {
                    let! firstExecutedCommand = preExecuteAggregateCommandMd<'A1, 'E1, 'F> aggregateId1 eventStore messageSenders metadata command1
                    let! secondExecutedCommand = preExecuteAggregateCommandMd<'A2, 'E2, 'F> aggregateId2 eventStore messageSenders metadata command2
                    let! thirdExecutedCommand = preExecuteAggregateCommandMd<'A3, 'E3, 'F> aggregateId3 eventStore messageSenders metadata command3
                    let! ids =
                        storeMultipleEvents eventStore messageSenders
                            [firstExecutedCommand
                             secondExecutedCommand
                             thirdExecutedCommand]
                    AggregateCache2.Instance.Memoize2 (firstExecutedCommand.NewState |> Ok) ((ids.[0] |> List.last, aggregateId1))
                    AggregateCache2.Instance.Memoize2 (secondExecutedCommand.NewState |> Ok) ((ids.[1] |> List.last, aggregateId2))
                    AggregateCache2.Instance.Memoize2 (thirdExecutedCommand.NewState |> Ok) ((ids.[2] |> List.last, aggregateId3))
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore aggregateId1 (firstExecutedCommand.NewState |> unbox) (ids.[0] |> List.last)
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A2, 'E2, 'F> eventStore aggregateId2 (secondExecutedCommand.NewState |> unbox) (ids.[1] |> List.last)
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A3, 'E3, 'F> eventStore aggregateId3 (thirdExecutedCommand.NewState |> unbox) (ids.[2] |> List.last)
                    return ()
                }
            
            
    [<Obsolete("Use runThreeAggregateCommandsMd instead")>]
    let inline runThreeAggregateCommandsMdBack<'A1, 'E1, 'A2, 'E2, 'A3, 'E3, 'F
        when 'A1 :> Aggregate<'F>
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        and 'A2 :> Aggregate<'F>
        and 'E2 :> Event<'A2>
        and 'E2 : (member Serialize: 'F)
        and 'E2 : (static member Deserialize: 'F -> Result<'E2, string>)
        and 'A2 : (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2 : (static member SnapshotsInterval: int)
        and 'A2 : (static member StorageName: string)
        and 'A2 : (static member Version: string)
        and 'A3 :> Aggregate<'F>
        and 'E3 :> Event<'A3>
        and 'E3 : (member Serialize: 'F)
        and 'E3 : (static member Deserialize: 'F -> Result<'E3, string>)
        and 'A3 : (static member Deserialize: 'F -> Result<'A3, string>)
        and 'A3 : (static member SnapshotsInterval: int)
        and 'A3 : (static member StorageName: string)
        and 'A3 : (static member Version: string)
        >
        (aggregateId1: Guid)
        (aggregateId2: Guid)
        (aggregateId3: Guid)
        (eventStore: IEventStore<'F>)
        (eventBroker: IEventBroker<'F>)
        (md: Metadata)
        (command1: AggregateCommand<'A1, 'E1>)
        (command2: AggregateCommand<'A2, 'E2>)
        (command3: AggregateCommand<'A3, 'E3>)
        =
            logger.Value.LogDebug "runThreeAggregateCommands"
            let commands = fun () ->
                result {
                    let! id1, state1 = getAggregateFreshState<'A1, 'E1, 'F> aggregateId1 eventStore
                    let! id2, state2 = getAggregateFreshState<'A2, 'E2, 'F> aggregateId2 eventStore
                    let! id3, state3 = getAggregateFreshState<'A3, 'E3, 'F> aggregateId3 eventStore

                    let! newState1, events1 =
                        state1
                        |> unbox
                        |> command1.Execute
                    let! newState2, events2 =
                        state2
                        |> unbox
                        |> command2.Execute
                    let! newState3, events3 =
                        state3
                        |> unbox
                        |> command3.Execute

                    let! idLists =
                        eventStore.MultiAddAggregateEventsMd
                            md    
                            [
                                (id1, events1 |>> _.Serialize, 'A1.Version, 'A1.StorageName, aggregateId1)
                                (id2, events2 |>> _.Serialize, 'A2.Version, 'A2.StorageName, aggregateId2)
                                (id3, events3 |>> _.Serialize, 'A3.Version, 'A3.StorageName, aggregateId3)
                            ]
                            
                    AggregateCache2.Instance.Memoize2 (newState1 |> box |> Ok) (idLists.[0] |> List.last, aggregateId1)
                    AggregateCache2.Instance.Memoize2 (newState2 |> box |> Ok) (idLists.[1] |> List.last, aggregateId2)
                    AggregateCache2.Instance.Memoize2 (newState3 |> box |> Ok) (idLists.[2] |> List.last, aggregateId3)

                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore aggregateId1 newState1 (idLists.[0] |> List.last)
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A2, 'E2, 'F> eventStore aggregateId2 newState2 (idLists.[1] |> List.last)
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A3, 'E3, 'F> eventStore aggregateId3 newState3 (idLists.[2] |> List.last)

                    return ()
                }
            // using the aggregateIds to determine the name of the mailboxprocessor can be overkill: revise this ASAP
        #if USING_MAILBOXPROCESSOR    
            let lookupName = sprintf "%s_%s_%s" 'A1.StorageName 'A2.StorageName 'A3.StorageName // aggregateIds
            MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) commands
        #else
            commands ()
        #endif    
            
    let inline runThreeAggregateCommands<'A1, 'E1, 'A2, 'E2, 'A3, 'E3, 'F
        when 'A1 :> Aggregate<'F>
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        and 'A2 :> Aggregate<'F>
        and 'E2 :> Event<'A2>
        and 'E2 : (member Serialize: 'F)
        and 'E2 : (static member Deserialize: 'F -> Result<'E2, string>)
        and 'A2 : (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2 : (static member SnapshotsInterval: int)
        and 'A2 : (static member StorageName: string)
        and 'A2 : (static member Version: string)
        and 'A3 :> Aggregate<'F>
        and 'E3 :> Event<'A3>
        and 'E3 : (member Serialize: 'F)
        and 'E3 : (static member Deserialize: 'F -> Result<'E3, string>)
        and 'A3 : (static member Deserialize: 'F -> Result<'A3, string>)
        and 'A3 : (static member SnapshotsInterval: int)
        and 'A3 : (static member StorageName: string)
        and 'A3 : (static member Version: string)
        >
        (aggregateId1: Guid)
        (aggregateId2: Guid)
        (aggregateId3: Guid)
        (eventStore: IEventStore<'F>)
        (messageSenders: string -> MessageSender)
        (command1: AggregateCommand<'A1, 'E1>)
        (command2: AggregateCommand<'A2, 'E2>)
        (command3: AggregateCommand<'A3, 'E3>)
        =
            logger.Value.LogDebug "runThreeAggregateCommands"
            runThreeAggregateCommandsMd aggregateId1 aggregateId2 aggregateId3 eventStore messageSenders Metadata.Empty command1 command2 command3

    let inline runTwoCommandsMd<'A1, 'A2, 'E1, 'E2, 'F
        when 'A1: (static member Zero: 'A1)
        and 'A1: (static member StorageName: string)
        and 'A1: (member Serialize: 'F)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A2: (static member Zero: 'A2)
        and 'A2: (static member StorageName: string)
        and 'A2: (member Serialize: 'F)
        and 'A2: (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A1: (static member Version: string)
        and 'A2: (static member Version: string)
        and 'A1: (static member SnapshotsInterval : int)
        and 'A2: (static member SnapshotsInterval : int)
        and 'E1 :> Event<'A1>
        and 'E2 :> Event<'A2> 
        and 'E1: (static member Deserialize: 'F -> Result<'E1, string>)
        and 'E1: (member Serialize: 'F)
        and 'E2: (static member Deserialize: 'F -> Result<'E2, string>)
        and 'E2: (member Serialize: 'F)
        >
            (eventStore: IEventStore<'F>)
            (eventBroker: IEventBroker<'F>) 
            (md: Metadata)
            (command1: Command<'A1, 'E1>) 
            (command2: Command<'A2, 'E2>)
            =
            logger.Value.LogDebug (sprintf "runTwoCommands %A %A" command1 command2)
            let commands = fun () ->
                result {

                    let! eventId1, state1 = getFreshState<'A1, 'E1, 'F> eventStore
                    let! eventId2, state2 = getFreshState<'A2, 'E2, 'F> eventStore

                    let! newState1, events1 =
                        state1
                        |> command1.Execute
                    let! newState2, events2 =
                        state2
                        |> command2.Execute

                    let! idLists =
                        eventStore.MultiAddEventsMd
                            md
                            [
                                (eventId1, events1 |>> _.Serialize, 'A1.Version, 'A1.StorageName)
                                (eventId2, events2 |>> _.Serialize, 'A2.Version, 'A2.StorageName)
                            ]
                    StateCache2<'A1>.Instance.Memoize2 newState1 (idLists.[0] |> List.last)
                    StateCache2<'A2>.Instance.Memoize2 newState2 (idLists.[1] |> List.last)
                    
                    let _ = mkSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore newState1 (idLists.[0] |> List.last)
                    let _ = mkSnapshotIfIntervalPassed2<'A2, 'E2, 'F> eventStore newState2 (idLists.[1] |> List.last)
                     
                    return ()
                }
                    
            let lookupNames = sprintf "%s_%s" 'A1.StorageName 'A2.StorageName
        #if USING_MAILBOXPROCESSOR    
            let processor = MailBoxProcessors.Processors.Instance.GetProcessor lookupNames
            MailBoxProcessors.postToTheProcessor processor commands
        #else
            commands ()
        #endif

    let inline runTwoCommands<'A1, 'A2, 'E1, 'E2, 'F
        when 'A1: (static member Zero: 'A1)
        and 'A1: (static member StorageName: string)
        and 'A1: (member Serialize: 'F)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A2: (static member Zero: 'A2)
        and 'A2: (static member StorageName: string)
        and 'A2: (member Serialize: 'F)
        and 'A2: (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A1: (static member Version: string)
        and 'A2: (static member Version: string)
        and 'A1: (static member SnapshotsInterval : int)
        and 'A2: (static member SnapshotsInterval : int)
        and 'E1 :> Event<'A1>
        and 'E2 :> Event<'A2> 
        and 'E1: (static member Deserialize: 'F -> Result<'E1, string>)
        and 'E1: (member Serialize: 'F)
        and 'E2: (static member Deserialize: 'F -> Result<'E2, string>)
        and 'E2: (member Serialize: 'F)
        >
        (eventStore: IEventStore<'F>)
        (eventBroker: IEventBroker<'F>) 

        (command1: Command<'A1, 'E1>) 
        (command2: Command<'A2, 'E2>)
        =
            logger.Value.LogDebug (sprintf "runTwoCommands %A %A" command1 command2)
            runTwoCommandsMd<'A1, 'A2, 'E1, 'E2, 'F> eventStore eventBroker Metadata.Empty command1 command2

    let inline runThreeCommandsMd<'A1, 'A2, 'A3, 'E1, 'E2, 'E3, 'F
        when 'A1: (static member Zero: 'A1)
        and 'A1: (static member StorageName: string)
        and 'A1: (member Serialize: 'F)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A2: (static member Zero: 'A2)
        and 'A2: (static member StorageName: string)
        and 'A2: (member Serialize: 'F)
        and 'A2: (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A3: (static member Zero: 'A3)
        and 'A3: (static member StorageName: string)
        and 'A3: (member Serialize: 'F)
        and 'A3: (static member Deserialize: 'F -> Result<'A3, string>)
        and 'A1: (static member Version: string)
        and 'A2: (static member Version: string)
        and 'A3: (static member Version: string)
        and 'A1: (static member SnapshotsInterval : int)
        and 'A2: (static member SnapshotsInterval : int)
        and 'A3: (static member SnapshotsInterval : int)
        and 'E1 :> Event<'A1>
        and 'E2 :> Event<'A2> 
        and 'E3 :> Event<'A3>
        and 'E1: (static member Deserialize: 'F -> Result<'E1, string>)
        and 'E1: (member Serialize: 'F)
        and 'E2: (static member Deserialize: 'F -> Result<'E2, string>)
        and 'E2: (member Serialize: 'F)
        and 'E3: (static member Deserialize: 'F -> Result<'E3, string>)
        and 'E3: (member Serialize: 'F)
        > 
            (storage: IEventStore<'F>)
            (eventBroker: IEventBroker<'F>)
            (md: Metadata)
            (command1: Command<'A1, 'E1>) 
            (command2: Command<'A2, 'E2>) 
            (command3: Command<'A3, 'E3>) 
            =
            logger.Value.LogDebug (sprintf "runThreeCommands %A %A %A" command1 command2 command3)
            
            let commands = fun () ->
                result {

                    let! eventId1, state1 = getFreshState<'A1, 'E1, 'F> storage
                    let! eventId2, state2 = getFreshState<'A2, 'E2, 'F> storage
                    let! eventId3, state3 = getFreshState<'A3, 'E3, 'F> storage

                    let! newState1, events1 =
                        state1
                        |> command1.Execute
                    let! newState2, events2 =
                        state2
                        |> command2.Execute
                    let! newState3, events3 =
                        state3
                        |> command3.Execute

                    let! idLists =
                        storage.MultiAddEventsMd
                            md  
                            [
                                (eventId1, events1 |>> _.Serialize, 'A1.Version, 'A1.StorageName)
                                (eventId2, events2 |>> _.Serialize, 'A2.Version, 'A2.StorageName)
                                (eventId3, events3 |>> _.Serialize, 'A3.Version, 'A3.StorageName)
                            ]
                            
                    StateCache2<'A1>.Instance.Memoize2 newState1 (idLists.[0] |> List.last)
                    StateCache2<'A2>.Instance.Memoize2 newState2 (idLists.[1] |> List.last)
                    StateCache2<'A3>.Instance.Memoize2 newState3 (idLists.[2] |> List.last)
                    
                    let _ = mkSnapshotIfIntervalPassed2<'A1, 'E1, 'F> storage newState1 (idLists.[0] |> List.last)
                    let _ = mkSnapshotIfIntervalPassed2<'A2, 'E2, 'F> storage newState2 (idLists.[1] |> List.last)
                    let _ = mkSnapshotIfIntervalPassed2<'A3, 'E3, 'F> storage newState3 (idLists.[2] |> List.last)

                    return ()
                } 
            let lookupNames = sprintf "%s_%s_%s" 'A1.StorageName 'A2.StorageName 'A3.StorageName
        #if USING_MAILBOXPROCESSOR 
            let processor = MailBoxProcessors.Processors.Instance.GetProcessor lookupNames
            MailBoxProcessors.postToTheProcessor processor commands
        #else
            commands ()
        #endif
            
    let inline runThreeCommands<'A1, 'A2, 'A3, 'E1, 'E2, 'E3, 'F
        when 'A1: (static member Zero: 'A1)
        and 'A1: (static member StorageName: string)
        and 'A1: (member Serialize: 'F)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A2: (static member Zero: 'A2)
        and 'A2: (static member StorageName: string)
        and 'A2: (member Serialize: 'F)
        and 'A2: (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A3: (static member Zero: 'A3)
        and 'A3: (static member StorageName: string)
        and 'A3: (member Serialize: 'F)
        and 'A3: (static member Deserialize: 'F -> Result<'A3, string>)
        and 'A1: (static member Version: string)
        and 'A2: (static member Version: string)
        and 'A3: (static member Version: string)
        and 'A1: (static member SnapshotsInterval : int)
        and 'A2: (static member SnapshotsInterval : int)
        and 'A3: (static member SnapshotsInterval : int)
        and 'E1 :> Event<'A1>
        and 'E2 :> Event<'A2> 
        and 'E3 :> Event<'A3>
        and 'E1: (static member Deserialize: 'F -> Result<'E1, string>)
        and 'E1: (member Serialize: 'F)
        and 'E2: (static member Deserialize: 'F -> Result<'E2, string>)
        and 'E2: (member Serialize: 'F)
        and 'E3: (static member Deserialize: 'F -> Result<'E3, string>)
        and 'E3: (member Serialize: 'F)
        > 
            (storage: IEventStore<'F>)
            (eventBroker: IEventBroker<'F>) 
            (command1: Command<'A1, 'E1>) 
            (command2: Command<'A2, 'E2>) 
            (command3: Command<'A3, 'E3>) 
            =
            logger.Value.LogDebug (sprintf "runThreeCommands %A %A %A" command1 command2 command3)
            runThreeCommandsMd<'A1, 'A2, 'A3, 'E1, 'E2, 'E3, 'F> storage eventBroker Metadata.Empty command1 command2 command3
            
    let inline GDPRResetSnapshotsAndEventsOfAnAggregate<'A, 'E, 'F
        when 'A:> Aggregate<'F>
        and 'A: (static member StorageName: string)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'A: (static member SnapshotsInterval : int)
        and 'A: (static member Version: string)
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'E: (member Serialize: 'F)
        >
        (aggregateId: Guid)
        (eventStore: IEventStore<'F>)
        (emptyGDPRState: 'A)
        (emptyGDPREvent: 'E)
        =
        logger.Value.LogDebug (sprintf "GDPRResetSnapshotsAndEventsOfAnAggregate %A" aggregateId)
        let reset = fun () ->
            result {
                let! _ = eventStore.GDPRReplaceSnapshotsAndEventsOfAnAggregate 'A.Version 'A.StorageName aggregateId emptyGDPRState.Serialize emptyGDPREvent.Serialize
                let! lastAggregateEventId = 
                    eventStore.TryGetLastAggregateEventId 'A.Version 'A.StorageName aggregateId
                    |> Result.ofOption (sprintf "GDPRResetSnapshotsAndEventsOfAnAggregate %s - %s" 'A.StorageName 'A.Version)
                let _ = AggregateCache2.Instance.Memoize2 (emptyGDPRState |> box |> Ok) (lastAggregateEventId, aggregateId)
                return ()
            }
        let lookupName = sprintf "%s" 'A.StorageName
    #if USING_MAILBOXPROCESSOR
        let processor = MailBoxProcessors.Processors.Instance.GetProcessor lookupName
        MailBoxProcessors.postToTheProcessor processor reset
    #else    
        reset ()
    #endif    
