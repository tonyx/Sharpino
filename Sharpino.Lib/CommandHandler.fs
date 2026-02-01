
namespace Sharpino

open System

open System.Reflection
open System.Threading
open FSharp.Core
open FSharpPlus

open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Hosting
open Sharpino.Cache
open Sharpino.Core
open Sharpino.RabbitMq
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
    let builder = Host.CreateApplicationBuilder()
    let myConfig = builder.Configuration
    let cancellationTokenSourceExpiration = myConfig.GetValue<int>("CancellationTokenSourceExpiration", 100000)
    let eventStoreTimeout = myConfig.GetValue<int>("EventStoreTimeout", 10000)
    
    type StramName = string

    // will play around D.I./host to make logging more flexible as something like follows
    // let host = Host.CreateApplicationBuilder().Build()
    // let factory: ILoggerFactory = LoggerFactory.Create(fun builder -> builder.AddConsole() |> ignore)
    // a the moment the logger is a ref to a null logger that can be setted properly
    
    type PreExecutedAggregateCommand<'A, 'F> =
        {
           AggregateId: Guid
           NewState: obj 
           EventId: EventId
           SerializedEvents: List<'F>
           Metadata: Metadata
           Version: string
           StorageName: string
           SnapshotsInterval: int
           EventType: Type
        }
                
    type UnitResult = ((unit -> unit) * AsyncReplyChannel<unit>)
   
    let loggerFactory = LoggerFactory.Create(fun b ->
        if myConfig.GetValue<bool>("Logging:Console", true) then
            b.AddConsole() |> ignore
        )
    let logger = loggerFactory.CreateLogger("Sharpino.CommandHandler")
     
    // let logger: Microsoft.Extensions.Logging.ILogger ref = ref NullLogger.Instance
    let setLogger (newLogger: ILogger) =
        failwith "deprecated. Config logger in appsettings"
    
    // this is not used anymore, as was able to queue the command processing messages of any specific stream. Keeping it
    // there to make it come back if needed
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

    // a stateviewer of contexts (single instance aggregates), based on the eventStore/storage will need this
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

    // a stateviewer of aggregates, based on the eventStore/storage will need this
    let inline getAggregateStorageFreshStateViewer<'A, 'E, 'F
        when 'A : (static member Deserialize: 'F -> Result<'A, string>) 
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

    // using variuos versions of mkSnapshotIfIntervalPassed instead. Leaving it to allow use from any app if needed
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
            logger.LogDebug (sprintf "mkSnapshot %A %A" 'A.Version 'A.StorageName)
            Async.RunSynchronously(
                async {
                    return
                        result
                            {
                                let! id, state = stateViewer ()
                                let serState = state.Serialize
                                let! result = storage.SetSnapshot 'A.Version (id, serState) 'A.StorageName
                                return result 
                            }
                }, Commons.generalAsyncTimeOut)
    
    let inline mkAggregateSnapshot<'A, 'E, 'F
        when 'E :> Event<'A>
        and 'A : (member Serialize : 'F)
        and 'A : (static member Deserialize: 'F -> Result<'A, string>) 
        and 'A : (static member StorageName: string) 
        and 'A : (static member Version: string) 
        and 'E : (static member Deserialize: 'F -> Result<'E, string>)
        and 'E : (member Serialize: 'F)
        > 
        (storage: IEventStore<'F>) 
        (aggregateId: AggregateId) =
            logger.LogDebug (sprintf "mkAggregateSnapshot %A" aggregateId)
            let stateViewer = getAggregateStorageFreshStateViewer<'A, 'E, 'F> storage
            Async.RunSynchronously 
                (async {
                    return
                        result
                            {
                                let! eventId, state = stateViewer aggregateId 
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
            logger.LogDebug "mkSnapshotIfIntervalPassed"
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
        when 'E :> Event<'A>
        and 'A : (member Serialize : 'F)
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
            logger.LogDebug "mkAggregateSnapshotIfIntervalPassed"
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
                                    if (lastEventId - snapEventId) > 'A.SnapshotsInterval then
                                        storage.SetAggregateSnapshot 'A.Version (aggregateId, eventId, state.Serialize) 'A.StorageName
                                    else
                                        () |> Ok
                                return! result
                            }
                }, Commons.generalAsyncTimeOut)
                
    // this looks the same as mkAggregateSnapshotIfIntervalPassed2 but it will avoid  generic
    let inline mkAggregateSnapshotIfIntervalPassed3<'F>
        (storage: IEventStore<'F>)
        (aggregateId: AggregateId)
        (storageVersion: string)
        (storageName: string)
        (eventId: EventId)
        (snapshotInterval: int)
        (state: 'F)  =
            logger.LogDebug "mkAggregateSnapshotIfIntervalPassed"
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
        
           
    // eventBroker is not considered as at the moment there is no message sending infrastracture for single instance (i.e. context) aggregates       
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
            logger.LogDebug (sprintf "runCommand %A\n" command)
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
            logger.LogDebug (sprintf "runCommand %A\n" command)
            runCommandMd eventStore eventBroker Metadata.Empty command
   
    // Setting initial states for aggregates is not necessarily also an event, but it could be
    // a command of a separate context. It happens they need to be transactional from the
    // point of view of the eventstore
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
        and 'A1 : (member Serialize: 'F)
        and 'A1 : (member Id: Guid)
        and 'A1 : (static member StorageName: string) 
        and 'A1 : (static member Version: string)
        >
        (storage: IEventStore<'F>)
        (messageSenders: MessageSenders)
        (initialInstance: 'A1)
        (md: Metadata)
        (command: Command<'A, 'E>)
        =
            logger.LogDebug (sprintf "runInitAndCommand %A %A" 'A.StorageName command)
            
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
                    
                    AggregateCache3.Instance.Memoize2 (0, initialInstance |> box) initialInstance.Id
                    
                    let _ =
                        let queueName = 'A1.Version + 'A1.StorageName
                        optionallySendInitialInstanceAsync<'A1, _> queueName messageSenders initialInstance.Id initialInstance
                    
                    return ()
                }
        #if USING_MAILBOXPROCESSOR        
            let processor = MailBoxProcessors.Processors.Instance.GetProcessor 'A.StorageName
            MailBoxProcessors.postToTheProcessor processor command
        #else
            command ()
        #endif    
    
    // just make a new aggregate instance and set it's initial state
    let inline runInit<'A1, 'E, 'F
        when 'E :> Event<'A1>
        and 'A1 : (member Id: Guid)
        and 'A1 : (member Serialize: 'F)
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'A1: (static member StorageName: string)
        and 'A1: (static member Version: string)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        >
        (eventStore: IEventStore<'F>)
        (messageSenders: MessageSenders)
        (initialInstance: 'A1) =
            logger.LogDebug (sprintf "runInit %A" 'A1.StorageName)
            result {
                let! _ = eventStore.SetInitialAggregateState initialInstance.Id 'A1.Version 'A1.StorageName initialInstance.Serialize
                
                AggregateCache3.Instance.Memoize2 (0, initialInstance |> box) initialInstance.Id
               
                let _ =
                    let queueName = 'A1.Version + 'A1.StorageName
                    optionallySendInitialInstanceAsync<'A1, 'E> queueName messageSenders initialInstance.Id initialInstance
                
                return ()
            }
            
    let inline runMultipleInit<'A1, 'E, 'F
        when 'E :> Event<'A1>
        and 'A1 : (member Id: Guid)
        and 'A1 : (member Serialize: 'F)
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'A1: (static member StorageName: string)
        and 'A1: (static member Version: string)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        >
        (eventStore: IEventStore<'F>)
        (messageSenders: MessageSenders)
        (initialInstances: ('A1)[]) =
            logger.LogDebug (sprintf "runInit %A" 'A1.StorageName)
            result {
                
                let idWithserializedAggregates =
                    initialInstances
                    |> Array.map (fun x -> x.Id, x.Serialize)
                let! _ = eventStore.SetInitialAggregateStates 'A1.Version 'A1.StorageName idWithserializedAggregates
                
                let _ =
                    initialInstances
                    |> Array.iter (fun x -> AggregateCache3.Instance.Memoize2 (0, x |> box) x.Id)
                
                // beware the mumber of threads here
                let _ =
                    let queueName = 'A1.Version + 'A1.StorageName
                    initialInstances
                    |> Array.iter (fun x -> optionallySendInitialInstanceAsync<'A1, 'E> queueName messageSenders x.Id x |> ignore)
                return ()
            }

    let inline runMultipleInitAsync<'A1, 'E, 'F
        when 'E :> Event<'A1>
        and 'A1 : (member Id: Guid)
        and 'A1 : (member Serialize: 'F)
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'A1: (static member StorageName: string)
        and 'A1: (static member Version: string)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        >
        (eventStore: IEventStore<'F>)
        (messageSenders: MessageSenders)
        (initialInstances: 'A1[])
        (ct: Option<CancellationToken>) =
            taskResult {
                use cts = CancellationTokenSource.CreateLinkedTokenSource
                              (defaultArg ct (new CancellationTokenSource(eventStoreTimeout)).Token)
                cts.CancelAfter(cancellationTokenSourceExpiration)
                
                let idWithserializedAggregates =
                    initialInstances
                    |>> (fun x -> x.Id, x.Serialize)
                let! res = eventStore.SetInitialAggregateStatesAsync('A1.Version, 'A1.StorageName, idWithserializedAggregates, cts.Token)
                let _ =
                    initialInstances
                    |> Array.iter (fun x -> AggregateCache3.Instance.Memoize2 (0, x |> box) x.Id)
                let _ =
                    let queueName = 'A1.Version + 'A1.StorageName
                    initialInstances
                    |> Array.iter (fun x -> optionallySendInitialInstanceAsync<'A1, 'E> queueName messageSenders x.Id x |> ignore)
                return res
            }

    let inline runInitAsync<'A1, 'E, 'F
        when 'E :> Event<'A1>
        and 'A1 : (member Id: Guid)
        and 'A1 : (member Serialize: 'F)
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'A1: (static member StorageName: string)
        and 'A1: (static member Version: string)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        >
        (eventStore: IEventStore<'F>)
        (messageSenders: MessageSenders)
        (initialInstance: 'A1)
        (ct: Option<CancellationToken>) =
            taskResult {
                use cts = CancellationTokenSource.CreateLinkedTokenSource
                              (defaultArg ct (new CancellationTokenSource(eventStoreTimeout)).Token)
                cts.CancelAfter(cancellationTokenSourceExpiration)
                let! res = eventStore.SetInitialAggregateStateAsync(initialInstance.Id, 'A1.Version, 'A1.StorageName, initialInstance.Serialize, cts.Token)
                let _ = AggregateCache3.Instance.Memoize2 (0, initialInstance |> box) initialInstance.Id
                let _ =
                    let queueName = 'A1.Version + 'A1.StorageName
                    optionallySendInitialInstanceAsync<'A1, 'E> queueName messageSenders initialInstance.Id initialInstance
                return res
            }
            
    // delete is a command which doesn't result in any event (not necessarily). It just flags the object as deleted
    let inline runDelete<'A1, 'E, 'F
        // when 'A1 :> Aggregate<'F> and 'E :> Event<'A1>
        when 'E :> Event<'A1>
        and 'A1 : (member Id: Guid)
        and 'A1 : (member Serialize: 'F)
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'A1: (static member StorageName: string)
        and 'A1: (static member Version: string)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        >
        (eventStore: IEventStore<'F>)
        (messageSenders: MessageSenders) 
        (id: AggregateId)
        (predicate: 'A1 -> bool)
        
        =
            logger.LogDebug (sprintf "runDelete %A" 'A1.StorageName)
            result {
                let! eventId, state =
                    StateView.getAggregateFreshState<'A1, 'E, 'F> id eventStore
                do!
                    predicate (state |> unbox)
                    |> Result.ofBool (sprintf "cannot delete aggregate with id %A of type %s as it is not safe according to the predicate" id 'A1.StorageName)
                
                let serializedState =
                    (state :?> 'A1).Serialize
               
                let! _ = eventStore.SnapshotAndMarkDeleted 'A1.Version 'A1.StorageName eventId id serializedState
                AggregateCache3.Instance.Clean id
                DetailsCache.Instance.RefreshDependentDetails id
                 
                let _ =
                    let queueName = 'A1.Version + 'A1.StorageName
                    optionallySendDeleteMessageAsync<'A1> queueName messageSenders id
                  
                return ()
            }
   
    // from here and beyond the techniques are to try to pass commands that mixes different objects
    // the convoluted sequence of generics is to help the static checking being effective
    // (but there is a way out, see the combination of preExecuteAggregateCommand and storeEvents which
    // are able to detach type dependencies by casting to object)
    let inline runDeleteAndAggregateCommandMd<'A1, 'E1, 'A2, 'E2, 'F
        when 'A1 : (member Id: Guid)
        and 'A1 : (member Serialize: 'F)
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
        and 'A2 : (member Id: Guid)
        and 'A2 : (member Serialize: 'F)
        and 'A2: (static member StorageName: string)
        and 'A2: (static member Version: string)
        and 'A2: (static member Deserialize: 'F -> Result<'A2, string>)
        >
        (eventStore: IEventStore<'F>)
        (messageSenders : MessageSenders)
        (md: Metadata)
        (id: AggregateId)
        (streamAggregateId: AggregateId)
        (command: AggregateCommand<'A2, 'E2>)
        (predicate: 'A1 -> bool) =
            logger.LogDebug (sprintf "runDelete %A" 'A1.StorageName)
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
                    
                AggregateCache3.Instance.Clean id
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
                        
                AggregateCache3.Instance.Memoize2 (ids |> List.last, newState |> box) streamAggregateId
                DetailsCache.Instance.RefreshDependentDetails id
                DetailsCache.Instance.RefreshDependentDetails streamAggregateId
               
                let _ =
                    optionallySendDeleteMessageAsync<'A1> ('A1.Version + 'A1.StorageName) messageSenders id
                let _ =
                    optionallySendAggregateEventsAsync<'A2, 'E2> ('A2.Version + 'A2.StorageName) messageSenders streamAggregateId events streamEventId (ids |> List.last)
                return ()
            }
    
    let inline runDeleteAndTwoAggregateCommandsMd<'A, 'E, 'A1, 'E1, 'A2, 'E2, 'F
        when 'A : (member Id: Guid)
        and 'A : (member Serialize: 'F)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)         
        and 'E :> Event<'A>
        and 'E : (member Serialize: 'F)
        and 'E : (static member Deserialize: 'F -> Result<'E, string>)
        and 'A1 : (member Id: Guid)
        and 'A1 : (member Serialize: 'F)
        and 'A1: (static member StorageName: string)
        and 'A1: (static member Version: string)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A2 : (member Id: Guid)
        and 'A2 : (member Serialize: 'F)
        and 'A2: (static member StorageName: string)
        and 'A2: (static member Version: string)
        and 'A2: (static member Deserialize: 'F -> Result<'A2, string>)
        and 'E2 :> Event<'A2>
        and 'E2 :(static member Deserialize: 'F -> Result<'E2, string>)
        and 'E2 : (member Serialize: 'F)>
        (eventStore: IEventStore<'F>)
        (messageSenders: MessageSenders)
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
               
                DetailsCache.Instance.RefreshDependentDetails aggregateId
                DetailsCache.Instance.RefreshDependentDetails aggregateId1
                DetailsCache.Instance.RefreshDependentDetails aggregateId2
                
                AggregateCache3.Instance.Clean aggregateId
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
                
                AggregateCache3.Instance.Memoize2 (newLastStateIdsList.[0] |> List.last, newStateA1 |> box) aggregateId1
                AggregateCache3.Instance.Memoize2 (newLastStateIdsList.[1] |> List.last, newStateA2 |> box) aggregateId2
                
                let _ = optionallySendDeleteMessageAsync<'A> ('A.StorageName + 'A.Version) messageSenders aggregateId
                let _ = optionallySendAggregateEventsAsync<'A1, 'E1> ('A1.StorageName + 'A1.Version) messageSenders aggregateId1 eventsA1 eventIdA1 (newLastStateIdsList.[0] |> List.last)
                let _ = optionallySendAggregateEventsAsync<'A2, 'E2> ('A2.StorageName + 'A2.Version) messageSenders aggregateId2 eventsA2 eventIdA2 (newLastStateIdsList.[1] |> List.last)
                
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
  
    let inline runDeleteAndNAggregateCommandsMd<'A, 'E, 'A1, 'E1, 'F
        when 'A : (member Id: Guid)
        and 'A : (member Serialize : 'F)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)         
        and 'E :> Event<'A>
        and 'E : (member Serialize: 'F)
        and 'E : (static member Deserialize: 'F -> Result<'E, string>)
        and 'A1 : (member Id: Guid)
        and 'A1 : (member Serialize : 'F)
        and 'A1: (static member StorageName: string)
        and 'A1: (static member Version: string)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1: (static member SnapshotsInterval: int)
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)>
        (eventStore: IEventStore<'F>)
        (messageSenders: MessageSenders) 
        (md: Metadata)
        (aggregateId: AggregateId)
        (aggregateIds1: List<AggregateId>)
        (command1: List<AggregateCommand<'A1, 'E1>>)
        (predicate: 'A -> bool) =
            logger.LogDebug "runDeleteAndNAggregateCommandsMd"
            result
                {
                    do! 
                        (aggregateIds1.Length = command1.Length)
                        |> Result.ofBool "aggregateIds and aggregate command length must correspond"
                        
                    let aggregateIdsWithCommands1 =
                        List.zip aggregateIds1 command1
                        |> List.groupBy fst
                        |> List.map (fun (id, cmds) -> id, cmds |> List.map snd)
                        
                    let uniqueAggregateIds1 =
                        aggregateIdsWithCommands1
                        |>> fst
                    
                    let! uniqueInitialstates1 =
                        aggregateIdsWithCommands1
                        |> List.traverseResultM (fun (id, _) -> getAggregateFreshState<'A1, 'E1, 'F> id eventStore)
                        
                    let uniqueInitialStatesOnly1 =
                        uniqueInitialstates1 
                        |>> fun (_, state) -> state
                        
                    let multicommands1 =
                        aggregateIdsWithCommands1
                        |>> fun (_, cmds) -> cmds
                        
                    let initialStatesAndMultiCommands1 =
                        List.zip uniqueInitialStatesOnly1 multicommands1
                        
                    let! newStatesAndEvents1 =
                        initialStatesAndMultiCommands1
                        |> List.traverseResultM (fun (state, commands) -> foldCommands (state |> unbox) commands)
                        
                    let newStates1 =
                        newStatesAndEvents1
                        |>> fst
                        
                    let generatedEvents1 =
                        newStatesAndEvents1
                        |>> snd
                        
                    let initialStateEventIds1 =
                        uniqueInitialstates1
                        |>> fst
                        
                    let serializedEvents1 =
                        generatedEvents1
                        |>> fun x -> x |>> fun (z: 'E1) -> z.Serialize
                        
                    let aggregateIds1 =
                        aggregateIdsWithCommands1
                        |>> fst
                        
                    let initialEventIds1Events1AndAggregateIds1 =
                        List.zip3 initialStateEventIds1 serializedEvents1 aggregateIds1
                        |>> fun (eventId, events, id) -> (eventId, events, 'A1.Version, 'A1.StorageName, id)
                       
                    let! eventId, toBeDeleted = getAggregateFreshState<'A, 'E, 'F> aggregateId eventStore
                    
                    do! predicate (toBeDeleted |> unbox)
                        |> Result.ofBool (sprintf "condition %A is not met" predicate)
                        
                    let _ = AggregateCache3.Instance.Clean aggregateId
                    
                    let! dbNewStatesEventIds =
                        let allPacked = initialEventIds1Events1AndAggregateIds1 
                        eventStore.SnapshotMarkDeletedAndMultiAddAggregateEventsMd
                            md
                            'A.Version
                            'A.StorageName
                            eventId
                            aggregateId
                            (toBeDeleted |> unbox<'A>).Serialize
                            allPacked
                            
                    DetailsCache.Instance.RefreshDependentDetails aggregateId
                    let _ =
                        for id in aggregateIds1 do
                            DetailsCache.Instance.RefreshDependentDetails id
                    
                    let doCacheResults =
                        fun () ->
                            for i in 0 .. (uniqueAggregateIds1.Length - 1) do
                                AggregateCache3.Instance.Memoize2 (dbNewStatesEventIds.[i] |> List.last, newStates1.[i] |> box) aggregateIds1.[i]
                                mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore uniqueAggregateIds1.[i] newStates1.[i] 'A1.SnapshotsInterval |> ignore
                            
                    doCacheResults ()
                    
                    let duplicatedIds =
                        aggregateIds1
                        |> List.groupBy id
                        |> List.filter (fun (_, ids) -> ids.Length > 1)
                        |> List.map (fun (id,_ ) -> id)
                   
                    let _ =
                        duplicatedIds
                        |> List.iter (fun id -> AggregateCache3.Instance.Clean id )
                        
                    let _ =
                        optionallySendDeleteMessageAsync<'A> ('A.Version + 'A.StorageName) messageSenders aggregateId
                        
                    let aggregateIdInitEventIdEndEventIdAndEventsA1 =
                        let initEventIdEndEventIdAndEventsA1 =
                            List.zip3 initialStateEventIds1 (dbNewStatesEventIds |>> List.last) generatedEvents1
                        List.zip aggregateIds1 initEventIdEndEventIdAndEventsA1
                        |>> fun (aggregateId, (initEventId, endEventId, events)) -> (aggregateId, initEventId, endEventId, events)
                        
                    let _ = optionallySendMultipleAggregateEventsAsync<'A1, 'E1> ('A1.Version + 'A1.StorageName) messageSenders aggregateIdInitEventIdEndEventIdAndEventsA1
                            
                    return ()
                }
    
    let inline runDeleteAndTwoNAggregateCommandsMd<'A, 'E, 'A1, 'E1, 'A2, 'E2, 'F
        when 'A : (member Id: Guid)
        and 'A : (member Serialize : 'F)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)         
        and 'E :> Event<'A>
        and 'E : (member Serialize: 'F)
        and 'E : (static member Deserialize: 'F -> Result<'E, string>)
        and 'A1 : (member Id: Guid)
        and 'A1 : (member Serialize : 'F)
        and 'A1: (static member StorageName: string)
        and 'A1: (static member Version: string)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1: (static member SnapshotsInterval: int)
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A2 : (member Id: Guid)
        and 'A2 : (member Serialize : 'F)
        and 'A2: (static member StorageName: string)
        and 'A2: (static member Version: string)
        and 'A2: (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2: (static member SnapshotsInterval: int)
        and 'E2 :> Event<'A2>
        and 'E2 :(static member Deserialize: 'F -> Result<'E2, string>)
        and 'E2 : (member Serialize: 'F)>
        (eventStore: IEventStore<'F>)
        (messageSenders: MessageSenders) 
        (md: Metadata)
        (aggregateId: AggregateId)
        (aggregateIds1: List<AggregateId>)
        (aggregateIds2: List<AggregateId>)
        (command1: List<AggregateCommand<'A1, 'E1>>)
        (command2: List<AggregateCommand<'A2, 'E2>>)
        (predicate: 'A -> bool) =
            
            logger.LogDebug "runDeleteAndTwoNAggregateCommandsMd"
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
                    
                    let! eventId, toBeDeleted = getAggregateFreshState<'A, 'E, 'F> aggregateId eventStore
                    
                    do! predicate (toBeDeleted |> unbox)
                        |> Result.ofBool (sprintf "condition %A is not met" predicate)
                    
                    let _ = AggregateCache3.Instance.Clean aggregateId
                    
                    let! dbNewStatesEventIds =
                        let allPacked = initialEventIds1Events1AndAggregateIds1 @ initialEventIds2EventIds2AndAggregateIds2
                        eventStore.SnapshotMarkDeletedAndMultiAddAggregateEventsMd
                            md
                            'A.Version
                            'A.StorageName
                            eventId
                            aggregateId
                            (toBeDeleted |> unbox<'A>).Serialize
                            allPacked
                    
                    let newDbBasedEventIds1 =
                        dbNewStatesEventIds
                        |> List.take uniqueAggregateIds1.Length
                    let newDbBasedEventIds2 =
                        dbNewStatesEventIds
                        |> List.skip uniqueAggregateIds1.Length
                    
                    let doCacheResults =
                        fun () ->
                            for i in 0 .. (uniqueAggregateIds1.Length - 1) do
                                AggregateCache3.Instance.Memoize2 (newDbBasedEventIds1.[i] |> List.last, newStates1.[i] |> box) uniqueAggregateIds1.[i]
                                mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore uniqueAggregateIds1.[i] newStates1.[i] (newDbBasedEventIds1.[i] |> List.last) |> ignore
                                
                            for i in 0 .. (uniqueAggregateIds2.Length - 1) do
                                AggregateCache3.Instance.Memoize2 (newDbBasedEventIds2.[i] |> List.last, newStates2.[i] |> box) uniqueAggregateIds2.[i]
                                mkAggregateSnapshotIfIntervalPassed2<'A2, 'E2, 'F> eventStore uniqueAggregateIds2.[i] newStates2.[i] (newDbBasedEventIds2.[i] |> List.last) |> ignore
                    doCacheResults ()
                    
                    let allIds = uniqueAggregateIds1 @ uniqueAggregateIds2
                    let duplicatedIds =
                        allIds
                        |> List.groupBy id
                        |> List.filter (fun (_, l) -> l.Length > 1)
                        |> List.map (fun (id, _) -> id)
                    
                    let _ =
                        duplicatedIds
                        |> List.iter (fun id ->
                            AggregateCache3.Instance.Clean id
                        )
                   
                    DetailsCache.Instance.RefreshDependentDetails aggregateId
                    
                    let _ =
                        aggregateIds1
                        |> List.iter  (fun id -> DetailsCache.Instance.RefreshDependentDetails id)
                    let _ =
                        aggregateIds2
                        |> List.iter  (fun id -> DetailsCache.Instance.RefreshDependentDetails id)
                    
                    let _ = optionallySendDeleteMessageAsync<'A> ('A.Version + 'A.StorageName) messageSenders aggregateId
                        
                    let aggregateIdInitEventIdEndEventIdAndEventsA1 =
                        let initEventIdEndEventIdAndEventsA1 =
                            List.zip3 initialStateEventIds1 (newDbBasedEventIds1 |>> List.last) generatedEvents1
                        List.zip aggregateIds1 initEventIdEndEventIdAndEventsA1
                        |>> fun (aggregateId, (initEventId, endEventId, events)) -> (aggregateId, initEventId, endEventId, events)
                        
                    let _ = optionallySendMultipleAggregateEventsAsync<'A1, 'E1> ('A1.Version + 'A1.StorageName) messageSenders aggregateIdInitEventIdEndEventIdAndEventsA1
                    
                    let aggregateIdInitEventIdEndEventIdAndEventsA2 =
                        let initEventIdEndEventIdAndEventsA2 =
                            List.zip3 initialStateEventIds2 (newDbBasedEventIds2 |>> List.last) generatedEvents2
                        List.zip aggregateIds2 initEventIdEndEventIdAndEventsA2
                        |>> fun (aggregateId, (initEventId, endEventId, events)) -> (aggregateId, initEventId, endEventId, events)
                        
                    let _ = optionallySendMultipleAggregateEventsAsync<'A2, 'E2> ('A2.Version + 'A2.StorageName) messageSenders aggregateIdInitEventIdEndEventIdAndEventsA2
                     
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
        and 'A1 : (member Serialize: 'F)
        and 'A1 : (member Id: Guid)
        and 'A1 : (static member StorageName: string) 
        and 'A1 : (static member Version: string)
        >
        (storage: IEventStore<'F>)
        (messageSenders: MessageSenders)
        (initialInstance: 'A1)
        (command: Command<'A, 'E>)
        =
            logger.LogDebug (sprintf "runInitAndCommand %A %A" 'A.StorageName command)
            runInitAndCommandMd<'A, 'E, 'A1, 'F> storage messageSenders initialInstance Metadata.Empty command
            
    let rec inline runInitAndAggregateCommandMd<'A1, 'E1, 'A2, 'F
        when 'A1 : (member Id: Guid)
        and 'A1 : (member Serialize: 'F)
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1: (static member StorageName: string)
        and 'A1: (static member Version: string)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1: (static member SnapshotsInterval : int)
        and 'A2 : (member Id: Guid)
        and 'A2 : (member Serialize: 'F)
        and 'A2: (static member StorageName: string)
        and 'A2: (static member Version: string)
        >
        (aggregateId: Guid)
        (storage: IEventStore<'F>)
        (messageSenders: MessageSenders) 
        (initialInstance: 'A2)
        (md: Metadata)
        (command: AggregateCommand<'A1, 'E1>)
        =
            logger.LogDebug (sprintf "runInitAndAggregateCommand %A %A" 'A1.StorageName command)
            let command = fun () ->
                result {
                    let! eventId, state = getAggregateFreshState<'A1, 'E1, 'F> aggregateId storage
                    let! newState, events =
                        state
                        |> unbox
                        |> command.Execute
                    let events' =
                        events 
                        |>> fun x -> x.Serialize
                    let! ids =
                        events' |> storage.SetInitialAggregateStateAndAddAggregateEventsMd eventId initialInstance.Id 'A2.Version 'A2.StorageName aggregateId initialInstance.Serialize 'A1.Version 'A1.StorageName md
                        
                    AggregateCache3.Instance.Memoize2 (0, initialInstance |> box) initialInstance.Id
                    AggregateCache3.Instance.Memoize2 (ids |> List.last, newState |> box) aggregateId
                    DetailsCache.Instance.RefreshDependentDetails aggregateId
                    
                    let _ =
                        mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> storage aggregateId newState (ids |> List.last)
                    let _ =
                        optionallySendAggregateEventsAsync<'A1, 'E1> ('A1.Version + 'A1.StorageName) messageSenders aggregateId events eventId (ids |> List.last)
                    let _ =
                        optionallySendInitialInstanceAsync<'A2, _> ('A2.Version + 'A2.StorageName) messageSenders initialInstance.Id initialInstance
                        
                    return ()    
                }
                
        #if USING_MAILBOXPROCESSOR         
            let processor = MailBoxProcessors.Processors.Instance.GetProcessor 'A1.StorageName
            MailBoxProcessors.postToTheProcessor processor command
        #else
            command ()
        #endif
    
    let inline runInitAndNAggregateCommandsMd<'A1, 'E1, 'A2, 'F
        when 'A1: (member Id: Guid)
        and 'A1: (member Serialize: 'F)
        and 'E1:> Event<'A1>
        and 'E1: (member Serialize: 'F)
        and 'E1: (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1: (static member StorageName: string)
        and 'A1: (static member Version: string)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1: (static member SnapshotsInterval : int)
        and 'A2: (member Id: Guid)
        and 'A2: (member Serialize: 'F)
        and 'A2: (static member StorageName: string)
        and 'A2: (static member Version: string)
        >
        (aggregateIds: List<Guid>)
        (storage: IEventStore<'F>)
        (messageSenders: MessageSenders) 
        (initialInstance: 'A2)
        (md: Metadata)
        (commands: List<AggregateCommand<'A1, 'E1>>)
        =
            logger.LogDebug (sprintf "runInitAndNAggregateCommands %A %A" 'A1.StorageName commands)
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
                        AggregateCache3.Instance.Memoize2 (eventIds.[i] |> List.last, newStates.[i] |> box) aggregateIds.[i]
                        mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> storage aggregateIds.[i] newStates.[i] (eventIds.[i] |> List.last) |> ignore
                     
                    let snapshotStreamName = sprintf "%s%s" 'A2.Version 'A2.StorageName
                    
                    let _ =
                        aggregateIds
                        |> List.iter (fun id -> DetailsCache.Instance.RefreshDependentDetails id)
                    
                    let _ =
                        optionallySendInitialInstanceAsync<'A2, _> snapshotStreamName messageSenders initialInstance.Id initialInstance
                    
                    let aggregateIdsInitialEventIdsFinalEventIdsAndEvents =
                        let initialEventIdsFinalEventsIdsAndEvents =
                            List.zip3 lastEventIds finalWrittenEventIds events
                        List.zip aggregateIds initialEventIdsFinalEventsIdsAndEvents
                        |>> fun (aggregateId, (initEventId, endEventId, events)) -> (aggregateId, initEventId, endEventId, events)
                    
                    let _ =
                        let eventStreamName = sprintf "%s%s" 'A1.Version 'A1.StorageName
                        optionallySendMultipleAggregateEventsAsync
                            eventStreamName messageSenders aggregateIdsInitialEventIdsFinalEventIdsAndEvents
                        
                    return ()
                }
        #if USING_MAILBOXPROCESSOR         
            let processor = MailBoxProcessors.Processors.Instance.GetProcessor 'A1.StorageName
            MailBoxProcessors.postToTheProcessor processor command
        #else
            command ()
        #endif
    
    let inline runInitAndAggregateCommand<'A1, 'E1, 'A2, 'F
        when 'A1 : (member Id: Guid)
        and 'A1 : (member Serialize: 'F)
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1: (static member StorageName: string)
        and 'A1: (static member Version: string)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1: (static member SnapshotsInterval : int)
        and 'A2 : (member Id: Guid)
        and 'A2 : (member Serialize: 'F)
        and 'A2: (static member StorageName: string)
        and 'A2: (static member Version: string)
        >
        (aggregateId: Guid)
        (storage: IEventStore<'F>)
        (messageSenders: MessageSenders) 
        (initialInstance: 'A2)
        (command: AggregateCommand<'A1, 'E1>)
        =
            logger.LogDebug (sprintf "runInitAndAggregateCommand %A %A" 'A1.StorageName command)
            runInitAndAggregateCommandMd<'A1, 'E1, 'A2, 'F> aggregateId storage messageSenders initialInstance String.Empty command
            
    let inline runInitAndTwoAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'F, 'A3
        when 'A1 : (member Id: Guid)
        and 'A1 : (member Serialize: 'F)
        and 'E1:> Event<'A1>
        and 'E1: (member Serialize: 'F)
        and 'E1: (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1: (static member StorageName: string)
        and 'A1: (static member Version: string)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1: (static member SnapshotsInterval : int)
        and 'A2 : (member Id: Guid)
        and 'A2 : (member Serialize: 'F)
        and 'E2:> Event<'A2>
        and 'E2: (member Serialize: 'F)
        and 'E2: (static member Deserialize: 'F -> Result<'E2, string>)
        and 'A2: (static member StorageName: string)
        and 'A2: (static member Version: string)
        and 'A2: (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2: (static member SnapshotsInterval : int)
        and 'A3 : (member Id: Guid)
        and 'A3 : (member Serialize: 'F)
        and 'A3: (static member StorageName: string)
        and 'A3: (static member Version: string)
        >
        (aggregateId1: Guid)
        (aggregateId2: Guid)
        (eventStore: IEventStore<'F>)
        (messageSenders: MessageSenders
         )
        (initialInstance: 'A3)
        (md: Metadata)
        (command1: AggregateCommand<'A1, 'E1>)
        (command2: AggregateCommand<'A2, 'E2>)
        =
            logger.LogDebug (sprintf "runInitAndTwoAggregateCommands %A %A %A %A %A %A" 'A1.StorageName 'A2.StorageName command1 command2 aggregateId1 aggregateId2)
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
                     
                    AggregateCache3.Instance.Memoize2 (ids.[0] |> List.last, newState1 |> box) aggregateId1
                    AggregateCache3.Instance.Memoize2 (ids.[1] |> List.last, newState2 |> box) aggregateId2
                    AggregateCache3.Instance.Memoize2 (0, initialInstance |> box) initialInstance.Id
                    
                    let _ =
                        DetailsCache.Instance.RefreshDependentDetails aggregateId1
                        DetailsCache.Instance.RefreshDependentDetails aggregateId2
                    
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore aggregateId1 newState1 (ids.[0] |> List.last)
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A2, 'E2, 'F> eventStore aggregateId2 newState2 (ids.[1] |> List.last)
                    
                    let _ = optionallySendInitialInstanceAsync<'A3, _> ('A3.Version + 'A3.StorageName) messageSenders aggregateId1 initialInstance
                    
                    let _ = optionallySendAggregateEventsAsync<'A1, 'E1> ('A1.Version + 'A1.StorageName) messageSenders aggregateId1 events1 (ids.[0] |> List.last)
                    let _ = optionallySendAggregateEventsAsync<'A2, 'E2> ('A2.Version + 'A2.StorageName) messageSenders aggregateId2 events2 (ids.[1] |> List.last)
                    
                    return ()
                }
        #if USING_MAILBOXPROCESSOR        
            let lookupName = sprintf "%s_%s" 'A1.StorageName 'A2.StorageName
            MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) command
        #else    
            command()
        #endif
            
    let inline runInitAndTwoAggregateCommands<'A1, 'E1, 'A2, 'E2, 'F, 'A3
        when 'A1 : (member Id: Guid)
        and 'A1: (member Serialize: 'F)
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1: (static member StorageName: string)
        and 'A1: (static member Version: string)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1: (static member SnapshotsInterval : int)
        and 'A2 : (member Id: Guid)
        and 'A2: (member Serialize: 'F)
        and 'E2:> Event<'A2>
        and 'E2: (member Serialize: 'F)
        and 'E2: (static member Deserialize: 'F -> Result<'E2, string>)
        and 'A2: (static member StorageName: string)
        and 'A2: (static member Version: string)
        and 'A2: (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2: (static member SnapshotsInterval : int)
        and 'A3 : (member Id: Guid)
        and 'A3: (member Serialize: 'F)
        and 'A3: (static member StorageName: string)
        and 'A3: (static member Version: string)
        >
        (aggregateId1: Guid)
        (aggregateId2: Guid)
        (eventStore: IEventStore<'F>)
        (messageSenders: MessageSenders)
        (initialInstance: 'A3)
        (command1: AggregateCommand<'A1, 'E1>)
        (command2: AggregateCommand<'A2, 'E2>)
        =
            logger.LogDebug (sprintf "runInitAndTwoAggregateCommands %A %A %A %A %A %A" 'A1.StorageName 'A2.StorageName command1 command2 aggregateId1 aggregateId2)
            runInitAndTwoAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'F, 'A3> aggregateId1 aggregateId2 eventStore messageSenders initialInstance String.Empty command1 command2
    
    let inline runInitAndThreeAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'A3, 'E3, 'F, 'A4
        when 'A1 : (member Id: Guid)
        and 'A1 : (member Serialize: 'F)
        and 'E1:> Event<'A1>
        and 'E1: (member Serialize: 'F)
        and 'E1: (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1: (static member StorageName: string)
        and 'A1: (static member Version: string)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1: (static member SnapshotsInterval : int)
        and 'A2 : (member Id: Guid)
        and 'A2 : (member Serialize: 'F)
        and 'E2:> Event<'A2>
        and 'E2: (member Serialize: 'F)
        and 'E2: (static member Deserialize: 'F -> Result<'E2, string>)
        and 'A2: (static member StorageName: string)
        and 'A2: (static member Version: string)
        and 'A2: (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2: (static member SnapshotsInterval : int)
        and 'A3 : (member Id: Guid)
        and 'A3 : (member Serialize: 'F)
        and 'E3:> Event<'A3>
        and 'E3: (member Serialize: 'F)
        and 'E3: (static member Deserialize: 'F -> Result<'E3, string>)
        and 'A3: (static member StorageName: string)
        and 'A3: (static member Version: string)
        and 'A3: (static member Deserialize: 'F -> Result<'A3, string>)
        and 'A3: (static member SnapshotsInterval : int)
        and 'A4 : (member Id: Guid)
        and 'A4: (member Serialize: 'F)
        and 'A4: (static member StorageName: string)
        and 'A4: (static member Version: string)
        >
        (aggregateId1: Guid)
        (aggregateId2: Guid)
        (aggregateId3: Guid)
        (eventStore: IEventStore<'F>)
        (messageSenders: MessageSenders)
        (initialInstance: 'A4)
        (md: Metadata)
        (command1: AggregateCommand<'A1, 'E1>)
        (command2: AggregateCommand<'A2, 'E2>)
        (command3: AggregateCommand<'A3, 'E3>)
        =
            logger.LogDebug (sprintf "runInitAndThreeAggregateCommands %A %A %A %A %A %A %A %A %A" 'A1.StorageName 'A2.StorageName 'A3.StorageName command1 command2 command3 aggregateId1 aggregateId2 aggregateId3)
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
                    
                    AggregateCache3.Instance.Memoize2 (ids.[0] |> List.last, newState1 |> box) aggregateId1
                    AggregateCache3.Instance.Memoize2 (ids.[1] |> List.last, newState2 |> box) aggregateId2
                    AggregateCache3.Instance.Memoize2 (ids.[2] |> List.last, newState3 |> box) aggregateId3
                    
                    let _ =
                        DetailsCache.Instance.RefreshDependentDetails aggregateId1
                        DetailsCache.Instance.RefreshDependentDetails aggregateId2
                        DetailsCache.Instance.RefreshDependentDetails aggregateId3
                    
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore aggregateId1 newState1 (ids.[0] |> List.last)
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A2, 'E2, 'F> eventStore aggregateId2 newState2 (ids.[1] |> List.last)
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A3, 'E3, 'F> eventStore aggregateId3 newState3 (ids.[2] |> List.last)
                    
                    let _ = optionallySendInitialInstanceAsync<'A4, _> ('A4.Version + 'A4.StorageName) messageSenders aggregateId1 initialInstance
                    let _ = optionallySendAggregateEventsAsync<'A1, 'E1> ('A1.Version + 'A1.StorageName) messageSenders aggregateId1 events1 (ids.[0] |> List.last)
                    let _ = optionallySendAggregateEventsAsync<'A2, 'E2> ('A2.Version + 'A2.StorageName) messageSenders aggregateId2 events2 (ids.[1] |> List.last)
                    let _ = optionallySendAggregateEventsAsync<'A3, 'E3> ('A3.Version + 'A3.StorageName) messageSenders aggregateId3 events3 (ids.[2] |> List.last)
                 
                    return ()
                }
        #if USING_MAILBOXPROCESSOR       
            let lookupName = sprintf "%s_%s_%s" 'A1.StorageName 'A2.StorageName 'A3.StorageName
            MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) command
        #else    
            command ()
        #endif     
            
    let inline runInitAndThreeAggregateCommands<'A1, 'E1, 'A2, 'E2, 'A3, 'E3, 'F, 'A4
        when 'A1 : (member Id: Guid)
        and 'A1: (member Serialize: 'F)
        and 'E1:> Event<'A1>
        and 'E1: (member Serialize: 'F)
        and 'E1: (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1: (static member StorageName: string)
        and 'A1: (static member Version: string)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1: (static member SnapshotsInterval : int)
        and 'A2 : (member Id: Guid)
        and 'A2: (member Serialize: 'F)
        and 'E2:> Event<'A2>
        and 'E2: (member Serialize: 'F)
        and 'E2: (static member Deserialize: 'F -> Result<'E2, string>)
        and 'A2: (static member StorageName: string)
        and 'A2: (static member Version: string)
        and 'A2: (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2: (static member SnapshotsInterval : int)
        and 'A3 : (member Id: Guid)
        and 'A3: (member Serialize: 'F)
        and 'E3:> Event<'A3>
        and 'E3: (member Serialize: 'F)
        and 'E3: (static member Deserialize: 'F -> Result<'E3, string>)
        and 'A3: (static member StorageName: string)
        and 'A3: (static member Version: string)
        and 'A3: (static member Deserialize: 'F -> Result<'A3, string>)
        and 'A3: (static member SnapshotsInterval : int)
        and 'A4 : (member Id: Guid)
        and 'A4: (member Serialize: 'F)
        and 'A4: (static member StorageName: string)
        and 'A4: (static member Version: string)
        >
        (aggregateId1: Guid)
        (aggregateId2: Guid)
        (aggregateId3: Guid)
        (eventStore: IEventStore<'F>)
        (messageSenders: MessageSenders)
        (initialInstance: 'A4)
        (command1: AggregateCommand<'A1, 'E1>)
        (command2: AggregateCommand<'A2, 'E2>)
        (command3: AggregateCommand<'A3, 'E3>)
        =
            logger.LogDebug (sprintf "runInitAndThreeAggregateCommands %A %A %A %A %A %A %A %A %A" 'A1.StorageName 'A2.StorageName 'A3.StorageName command1 command2 command3 aggregateId1 aggregateId2 aggregateId3)
            runInitAndThreeAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'A3, 'E3, 'F, 'A4> aggregateId1 aggregateId2 aggregateId3 eventStore messageSenders initialInstance Metadata.Empty command1 command2 command3

    
    // this is a way to pre-build a mixture of arbitrary aggregate commands of any different type
    // and then be able to execute them in a single transaction
    // the message sending part can't be part (must be handled at the application level)
    let inline preExecuteAggregateCommandMd<'A, 'E, 'F
        when 'A : (member Id: Guid)
        and 'A : (member Serialize: 'F)
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
        (messageSenders: MessageSenders) // Todo: not used. Drop this parameter
        (md: Metadata)
        (command: AggregateCommand<'A, 'E>)
        =
            logger.LogDebug (sprintf "preExecuteAggregateCommandMd %A,  %A, id: %A" 'A.StorageName command  aggregateId)
            
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
                      SerializedEvents = events |>> (fun x -> x.Serialize)
                      Metadata = md
                      Version = 'A.Version
                      StorageName = 'A.StorageName
                      SnapshotsInterval = 'A.SnapshotsInterval
                      EventType = typeof<'E>
                    }
                return result
            }
            
    let storeEvents (eventStore: IEventStore<'F>) (messageSenders: MessageSenders) (block: PreExecutedAggregateCommand<_, _>) =
        logger.LogDebug (sprintf "storeAggregateBlock %A,  %A, id: %A" block.StorageName block.AggregateId block.AggregateId)
        result {
            let! ids =
                block.SerializedEvents
                |> eventStore.AddAggregateEventsMd block.EventId block.Version block.StorageName block.AggregateId block.Metadata
            return ids  
        }
        
    let storeEventsAsync (eventStore: IEventStore<'F>) (block: PreExecutedAggregateCommand<_, _>, ct: CancellationToken) =
        logger.LogDebug (sprintf "storeAggregateBlock %A,  %A, id: %A" block.StorageName block.AggregateId block.AggregateId)
        taskResult {
            let ids =
                eventStore.AddAggregateEventsMdAsync (block.EventId, block.Version, block.StorageName, block.AggregateId, block.Metadata, block.SerializedEvents, ct)
            return! ids  
        }
    
    let storeMultipleEvents (eventStore: IEventStore<'F>) (eventBroker: MessageSenders) (blocks: List<PreExecutedAggregateCommand<_, _>>) =
        logger.LogDebug (sprintf "storeAggregateBlock %A,  %A, id: %A" blocks.Head.StorageName blocks.Head.AggregateId blocks.Head.AggregateId)
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
        when 'A : (member Id: Guid)
        and 'A : (member Serialize: 'F)
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
        (messageSenders: MessageSenders)
        (md: Metadata)
        (command: AggregateCommand<'A, 'E>)
        =
            logger.LogDebug (sprintf "runAggregateCommandRefactor %A,  %A, id: %A" 'A.StorageName command  aggregateId)
            result {
                let! (eventId, state) = getAggregateFreshState<'A, 'E, 'F> aggregateId storage
                let! _, events =
                    state
                    |> unbox
                    |> command.Execute
            
                let! executedCommand = preExecuteAggregateCommandMd<'A, 'E, 'F> aggregateId storage messageSenders md command
                
                let! ids = storeEvents storage messageSenders executedCommand
                AggregateCache3.Instance.Memoize2 (ids |> List.last, executedCommand.NewState |> box) aggregateId
                 
                let _ =
                    DetailsCache.Instance.RefreshDependentDetails aggregateId
                let _ =
                    mkAggregateSnapshotIfIntervalPassed2<'A, 'E, 'F> storage aggregateId (executedCommand.NewState |> unbox) (ids |> List.last)
                let _ =
                    optionallySendAggregateEventsAsync<'A, 'E> ('A.Version + 'A.StorageName) messageSenders aggregateId events eventId (ids |> List.last)
                    
                return ()
            }
            
    // I keep this one available to remind the 'old' way of doing it
    // [<Obsolete("use runAggregateCommandMd instead")>]
    // let inline runAggregateCommandMdBack<'A, 'E, 'F
    //     when 'A :> Aggregate<'F>
    //     and 'E :> Event<'A>
    //     and 'A : (static member Deserialize: 'F -> Result<'A, string>) 
    //     and 'A : (static member StorageName: string) 
    //     and 'A : (static member Version: string)
    //     and 'A : (static member SnapshotsInterval: int)
    //     and 'E : (static member Deserialize: 'F -> Result<'E, string>)
    //     and 'E : (member Serialize: 'F)
    //     >
    //     (aggregateId: Guid)
    //     (storage: IEventStore<'F>)
    //     (eventBroker: IEventBroker<'F>)
    //     (md: Metadata)
    //     (command: AggregateCommand<'A, 'E>)
    //     =
    //         logger.LogDebug (sprintf "runAggregateCommand %A,  %A, id: %A" 'A.StorageName command  aggregateId)
    //         let command = fun () ->
    //             result {
    //                 let! eventId, state = getAggregateFreshState<'A, 'E, 'F> aggregateId storage
    //                 let! newState, events =
    //                     state
    //                     |> unbox
    //                     |> command.Execute
    //                 let! ids =
    //                     events |>> _.Serialize
    //                     |> storage.AddAggregateEventsMd eventId 'A.Version 'A.StorageName aggregateId md
    //                
    //                 AggregateCache3.Instance.Memoize2 (eventId, newState |> box) aggregateId
    //                 
    //                 let _ = mkAggregateSnapshotIfIntervalPassed2<'A, 'E, 'F> storage aggregateId newState (ids |> List.last)
    //                 return ()
    //             }
    //     #if USING_MAILBOXPROCESSOR        
    //         let processor = MailBoxProcessors.Processors.Instance.GetProcessor (sprintf "%s_%s" 'A.StorageName (aggregateId.ToString()))
    //         MailBoxProcessors.postToTheProcessor processor command
    //     #else    
    //         command ()
    //     #endif    
            
    let inline runAggregateCommand<'A, 'E, 'F
        when 'A : (member Id: Guid)
        and 'A : (member Serialize: 'F)
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
        (messageSenders: MessageSenders) 
        (command: AggregateCommand<'A, 'E>)
        =
            logger.LogDebug (sprintf "runAggregateCommand %A,  %A, id: %A" 'A.StorageName command aggregateId)
            runAggregateCommandMd<'A, 'E, 'F> aggregateId storage messageSenders Metadata.Empty command
    
    // forceRun...Command will relax constraints of the related aggregate commands
    // allowing aggregateIds to be repeated (i.e. used by different commands in one shot)
    // Technically forceRun should be able to replace the companion run...Command
    
    let inline forceRunNAggregateCommandsMd<'A1, 'E1, 'F
        when 'A1 : (member Id: Guid)
        and 'A1 : (member Serialize: 'F)
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
        (messageSenders: MessageSenders) 
        (md: Metadata)
        (commands: List<AggregateCommand<'A1, 'E1>>)
        =
            logger.LogDebug "forceRunNAggregateCommandsMd" 
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
                        AggregateCache3.Instance.Memoize2 (dbEventIds.[i] |> List.last, newStates.[i] |> box) uniqueAggregateIds.[i]
                        mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore uniqueAggregateIds.[i] newStates.[i] (dbEventIds.[i] |> List.last) |> ignore
                    
                    let aggregateIdInitialEventIdEndEventIdAndEvents =
                        let initialEventIdEnEventIdAndEvents =
                            List.zip3 initialStatesEventIds (dbEventIds |>> List.last) newEvents
                        List.zip uniqueAggregateIds initialEventIdEnEventIdAndEvents
                        |>> fun (id, (initEventId, endEventId, events)) -> (id, initEventId, endEventId, events)
                   
                    let duplicatedIds =
                        aggregateIds
                        |> List.groupBy id 
                        |> List.filter (fun (_, x) -> x.Length > 1)
                        |> List.map fst
                    
                    let _ =
                        duplicatedIds
                        |> List.iter (fun id -> AggregateCache3.Instance.Clean id)
                    
                    let _ =
                        aggregateIds
                        |> List.iter (fun x -> DetailsCache.Instance.RefreshDependentDetails x)
                     
                    let _ =
                        optionallySendMultipleAggregateEventsAsync<'A1, 'E1> ('A1.Version + 'A1.StorageName) messageSenders aggregateIdInitialEventIdEndEventIdAndEvents
                    
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
        when 'A1 : (member Id: Guid)
        and 'A1 : (member Serialize: 'F)
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
        (messageSenders: MessageSenders) 
        (commands: List<AggregateCommand<'A1, 'E1>>)
        =
            logger.LogDebug "forceRunNAggregateCommands"
            forceRunNAggregateCommandsMd<'A1, 'E1, 'F> aggregateIds eventStore messageSenders Metadata.Empty commands
                
    let inline runNAggregateCommandsMd<'A1, 'E1, 'F
        when 'A1 : (member Id: Guid)
        and 'A1 : (member Serialize: 'F)
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
        (messageSenders: MessageSenders)
        (md: Metadata)
        (commands: List<AggregateCommand<'A1, 'E1>>)
        =
            logger.LogDebug "runNAggregateCommandsMd"
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
                        AggregateCache3.Instance.Memoize2 (storedEventIds.[i] |> List.last, newStates.[i] |> box) aggregateIds.[i]
                        mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore aggregateIds.[i] newStates.[i] (storedEventIds.[i] |> List.last) |> ignore
                    
                    let duplicatedIds =
                        aggregateIds
                        |> List.groupBy id
                        |> List.filter (fun (_, x) -> x.Length > 1)
                        |> List.map fst
                    
                    let _ =
                        duplicatedIds
                        |> List.iter (fun x -> AggregateCache3.Instance.Clean x)
                    
                    let _ =
                        aggregateIds
                        |> List.distinct
                        |> List.iter (fun x -> DetailsCache.Instance.RefreshDependentDetails x)
                     
                    let aggregateIdAndInitialEventIdEndEventIdAndEvents =
                        let initialEventIdEndEventIdAndEvents =
                            List.zip3 lastEventIds (storedEventIds |>> List.last) events
                        List.zip aggregateIds initialEventIdEndEventIdAndEvents
                        |>> fun (id, (eventId, storedEventIds, events)) -> (id, eventId, storedEventIds, events)
                    
                    let _ =
                        optionallySendMultipleAggregateEventsAsync<'A1, 'E1> ('A1.Version + 'A1.StorageName) messageSenders aggregateIdAndInitialEventIdEndEventIdAndEvents 
                    
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
        when 'A1 : (member Id: Guid)
        and 'A1 : (member Serialize: 'F)
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
        (messageSenders: MessageSenders)
        (commands: List<AggregateCommand<'A1, 'E1>>)
        =
            logger.LogDebug "runNAggregateCommands"
            runNAggregateCommandsMd<'A1, 'E1, 'F> aggregateIds eventStore messageSenders Metadata.Empty commands
   
    
    let inline runTwoAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'F
        when 'A1 : (member Id: Guid)
        and 'A1 : (member Serialize: 'F)
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        and 'A2 : (member Id: Guid)
        and 'A2 : (member Serialize: 'F)
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
        (messageSenders: MessageSenders)     
        (md: Metadata)
        (command1: AggregateCommand<'A1, 'E1>)
        (command2: AggregateCommand<'A2, 'E2>)
        =
            logger.LogDebug "runTwoAggregateCommandsMd"
            result {
                let! firstExecutedCommand =  preExecuteAggregateCommandMd<'A1, 'E1, 'F> aggregateId1 eventStore messageSenders md command1
                let! secondExecutedCommand = preExecuteAggregateCommandMd<'A2, 'E2, 'F> aggregateId2 eventStore messageSenders md command2
                
                let! _, stateA1 = getAggregateFreshState<'A1, 'E1, 'F> aggregateId1 eventStore
                let! _, stateA2 = getAggregateFreshState<'A2, 'E2, 'F> aggregateId2 eventStore
                
                let stateA1 = stateA1 |> unbox
                let stateA2 = stateA2 |> unbox
                
                let! _, eventsA1 = command1.Execute stateA1
                let! _, eventsA2 = command2.Execute stateA2
                let! ids =
                    storeMultipleEvents eventStore messageSenders
                        [firstExecutedCommand
                         secondExecutedCommand]
                
                AggregateCache3.Instance.Memoize2 (ids.[0] |> List.last, firstExecutedCommand.NewState |> box) aggregateId1
                AggregateCache3.Instance.Memoize2 (ids.[1] |> List.last, secondExecutedCommand.NewState |> box) aggregateId2
                
                let queueNameA1 = 'A1.Version + 'A1.StorageName
                let queueNameA2 = 'A2.Version + 'A2.StorageName
                
                let _ =
                    DetailsCache.Instance.RefreshDependentDetails aggregateId1
                let _ =
                    DetailsCache.Instance.RefreshDependentDetails aggregateId2
               
                
                let serializeProperty1 = firstExecutedCommand.NewState.GetType().GetProperty("Serialize")
                let serialized1 = serializeProperty1.GetValue(firstExecutedCommand.NewState) :?> 'F
                let _ = mkAggregateSnapshotIfIntervalPassed3<'F>
                                eventStore
                                aggregateId1
                                firstExecutedCommand.Version
                                firstExecutedCommand.StorageName
                                (ids.[0] |> List.last)
                                firstExecutedCommand.SnapshotsInterval
                                serialized1
                                
                let serializeProperty2 = firstExecutedCommand.NewState.GetType().GetProperty("Serialize")
                let serialized2 = serializeProperty2.GetValue(firstExecutedCommand.NewState) :?> 'F
                let _ = mkAggregateSnapshotIfIntervalPassed3<'F>
                                eventStore
                                aggregateId1
                                'A2.Version
                                'A2.StorageName
                                (ids.[1] |> List.last)
                                secondExecutedCommand.SnapshotsInterval
                                serialized2
               
                let _ = optionallySendAggregateEventsAsync<'A1, 'E1> queueNameA1 messageSenders aggregateId1 eventsA1 firstExecutedCommand.EventId (ids.[0] |> List.last)
                let _ = optionallySendAggregateEventsAsync<'A2, 'E2> queueNameA2 messageSenders aggregateId2 eventsA2 secondExecutedCommand.EventId (ids.[1] |> List.last)   
                
                return ()
            }
    
   
    // todo: this approach is an hack to retrieve again the source deserialized events
    // I should be able to avoid this trick by just using reflection and the serialize on the origin object themselves
    // on the preExecutedAggregateCommands (which is basd on the wrong assumption that to solve the problem the serialized must be part of it)
    let inline runPreExecutedAggregateCommands<'F>
        (preExecutedAggregateCommands: List<PreExecutedAggregateCommand<_,'F>>)
        (eventStore: IEventStore<'F>)
        (messageSenders: MessageSenders) =
        logger.LogDebug "runPreExecutedCommands"
        result {
            let! storedIds =
                storeMultipleEvents eventStore messageSenders
                    preExecutedAggregateCommands
            
            for i in 0..(preExecutedAggregateCommands.Length - 1) do
                AggregateCache3.Instance.Memoize2 (storedIds.[i] |> List.last, preExecutedAggregateCommands.[i].NewState |> box) preExecutedAggregateCommands.[i].AggregateId
                
            for i in 0 .. (preExecutedAggregateCommands.Length - 1) do
                DetailsCache.Instance.RefreshDependentDetails preExecutedAggregateCommands.[i].AggregateId
            
            for i in 0..(preExecutedAggregateCommands.Length - 1) do
                let serializeProperty = preExecutedAggregateCommands.[i].NewState.GetType().GetProperty("Serialize") 
                let serialized = serializeProperty.GetValue(preExecutedAggregateCommands.[i].NewState) :?> 'F
                mkAggregateSnapshotIfIntervalPassed3<'F>
                    eventStore
                    preExecutedAggregateCommands.[i].AggregateId
                    preExecutedAggregateCommands.[i].Version
                    preExecutedAggregateCommands.[i].StorageName
                    (storedIds.[i] |> List.last)
                    preExecutedAggregateCommands.[i].SnapshotsInterval
                    serialized
                |> ignore
            
            // note: 
            // this reckless unsafe operation must work because the involved types are actually typechecked outside
            // the scope of this call, so at the moment there is no specific treatment about theoretical runtime exceptions
            for i in 0..(preExecutedAggregateCommands.Length - 1) do
                let queueName = preExecutedAggregateCommands.[i].Version + preExecutedAggregateCommands.[i].StorageName
                let deserializer = preExecutedAggregateCommands.[i].EventType.GetMethod "Deserialize"
                let deserializedEvents =
                    preExecutedAggregateCommands.[i].SerializedEvents
                    |> List.map
                           (fun x -> deserializer.Invoke(null, [|x|]))
                           |> List.map 
                                (fun x ->
                                   (
                                    let resultType = x.GetType()
                                    let resultValueProperty = resultType.GetProperty("ResultValue")
                                    resultValueProperty.GetValue x 
                                    )
                                )
                                
                optionallySendTypelessAggregateEventsAsync
                    queueName
                    messageSenders
                    preExecutedAggregateCommands.[i].AggregateId
                    deserializedEvents
                    preExecutedAggregateCommands.[i].EventId
                    (storedIds.Item i |> List.last)
                    |> ignore
                    
            return ()
        }
     
    // this one is the same as runPreExecutedAggregateCommands returning the stored ids. Unify them when you decide to do it: no rush
    let inline runPreExecutedAggregateCommands2<'F> 
        (preExecutedAggregateCommands: List<PreExecutedAggregateCommand<_,'F>>)
        (eventStore: IEventStore<'F>)
        (messageSenders: MessageSenders) =
        logger.LogDebug "runPreExecutedCommands"
        result {
            let! storedIds =
                storeMultipleEvents eventStore messageSenders
                    preExecutedAggregateCommands
            
            for i in 0 ..(preExecutedAggregateCommands.Length - 1) do
                AggregateCache3.Instance.Memoize2 (storedIds.[i] |> List.last, preExecutedAggregateCommands.[i].NewState |> box) preExecutedAggregateCommands.[i].AggregateId
                
            for i in 0 .. (preExecutedAggregateCommands.Length - 1) do
                DetailsCache.Instance.RefreshDependentDetails preExecutedAggregateCommands.[i].AggregateId
            
            for i in 0..(preExecutedAggregateCommands.Length - 1) do
                let serializeProperty = preExecutedAggregateCommands.[i].NewState.GetType().GetProperty("Serialize") 
                let serialized = serializeProperty.GetValue(preExecutedAggregateCommands.[i].NewState) :?> 'F
                mkAggregateSnapshotIfIntervalPassed3<'F>
                    eventStore
                    preExecutedAggregateCommands.[i].AggregateId
                    preExecutedAggregateCommands.[i].Version
                    preExecutedAggregateCommands.[i].StorageName
                    (storedIds.[i] |> List.last)
                    preExecutedAggregateCommands.[i].SnapshotsInterval
                    serialized
                |> ignore

            // note: 
            // this reckless unsafe operation must work because the involved types are actually typechecked outside
            // the scope of this call, so at the moment there is no specific treatment about theoretical runtime exceptions
            for i in 0..(preExecutedAggregateCommands.Length - 1) do
                let queueName = preExecutedAggregateCommands.[i].Version + preExecutedAggregateCommands.[i].StorageName
                let deserializer = preExecutedAggregateCommands.[i].EventType.GetMethod "Deserialize"
                let deserializedEvents =
                    preExecutedAggregateCommands.[i].SerializedEvents
                    |> List.map
                           (fun x -> deserializer.Invoke(null, [|x|]))
                           |> List.map 
                                (fun x ->
                                   (
                                    let resultType = x.GetType()
                                    let resultValueProperty = resultType.GetProperty("ResultValue")
                                    resultValueProperty.GetValue x 
                                    )
                                )
                optionallySendTypelessAggregateEventsAsync
                    queueName
                    messageSenders
                    preExecutedAggregateCommands.[i].AggregateId
                    deserializedEvents
                    preExecutedAggregateCommands.[i].EventId
                    (storedIds.Item i |> List.last)
                    |> ignore
                
            let result =
                storedIds |>> List.last
            return result    
        }
     
    // proof of concept: This is a possible way of extend the library to check cross-aggregates invariants (i.e. related to something different than 'A1 and 'A2)
    // note: the crossAggregateConstriant may contains environment related to any other aggregate state (see the example in the tests)
    let inline runTwoAggregateCommandsCheckingCrossAggregatesConstraintsMd<'A1, 'E1, 'A2, 'E2, 'F
        when 'A1 : (member Id: Guid)
        and 'A1 : (member Serialize: 'F)
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        and 'A2 : (member Id: Guid)
        and 'A2 : (member Serialize: 'F)
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
        (messageSenders: MessageSenders)
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
                    
                    AggregateCache3.Instance.Memoize2 (newLastStateIdsList.[0] |> List.last, newState1 |> box) aggregateId1
                    AggregateCache3.Instance.Memoize2 (newLastStateIdsList.[1] |> List.last, newState2 |> box) aggregateId2
                    
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore aggregateId1 newState1 (newLastStateIdsList.[0] |> List.last)
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A2, 'E2, 'F> eventStore aggregateId2 newState2 (newLastStateIdsList.[1] |> List.last)
                    
                    let _ =
                        optionallySendAggregateEventsAsync<'A1, 'E1> ('A1.Version + 'A1.StorageName) messageSenders aggregateId1 events1 eventId1 (newLastStateIdsList.[0] |> List.last)
                    let _ =
                        optionallySendAggregateEventsAsync<'A2, 'E2> ('A2.Version + 'A2.StorageName) messageSenders aggregateId2 events2 eventId2 (newLastStateIdsList.[1] |> List.last)
                    
                    return ()     
                }
        #if USING_MAILBOXPROCESSOR
            let lookupName = sprintf "%s_%s" 'A1.StorageName  'A2.StorageName
            MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) commands
        #else
            commands () 
        #endif    
            
    let inline runTwoAggregateCommands<'A1, 'E1, 'A2, 'E2, 'F
        when 'A1 : (member Id: Guid)
        and 'A1 : (member Serialize: 'F)
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        and 'A2 : (member Id: Guid)
        and 'A2 : (member Serialize: 'F)
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
        (messageSenders: MessageSenders)
        (command1: AggregateCommand<'A1, 'E1>)
        (command2: AggregateCommand<'A2, 'E2>)
        =
            runTwoAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'F> aggregateId1 aggregateId2 eventStore messageSenders String.Empty command1 command2
   
    // any forceRunXX will relax the check on the uniqueness of the aggregateId passed
    let inline forceRunTwoNAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'F
        when 'A1 : (member Id: Guid)
        and 'A1 : (member Serialize: 'F)
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        and 'A2 : (member Id: Guid)
        and 'A2 : (member Serialize: 'F)
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
        (messageSenders: MessageSenders)
        (md: Metadata)
        (command1: List<AggregateCommand<'A1, 'E1>>)
        (command2: List<AggregateCommand<'A2, 'E2>>)
        =
            logger.LogDebug "forceRunTwoNAggregateCommands"
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
                    
                    let doCacheResults =
                        fun () ->
                            for i in 0 .. (uniqueAggregateIds1.Length - 1) do
                                AggregateCache3.Instance.Memoize2 (newDbBasedEventIds1.[i] |> List.last, newStates1.[i] |> box) uniqueAggregateIds1.[i]
                            for i in 0 .. (uniqueAggregateIds2.Length - 1) do
                                AggregateCache3.Instance.Memoize2 (newDbBasedEventIds2.[i] |> List.last, newStates2.[i] |> box) uniqueAggregateIds2.[i]
                    
                    doCacheResults ()
                    
                    let allIds = uniqueAggregateIds1 @ uniqueAggregateIds2
                    let duplicatedIds =
                        allIds
                        |> List.groupBy id
                        |> List.filter (fun (_, l) -> l.Length > 1)
                        |> List.map (fun (id, _) -> id)
                    
                    // the caching mechanism screws up if the same aggregateId is used in more than one command
                    // so we clean the cache for the duplicated ids
                    let _ =
                        duplicatedIds
                        |> List.iter AggregateCache3.Instance.Clean
                        
                    let _ =    
                        allIds
                        |> List.distinct
                        |> List.iter (fun x -> DetailsCache.Instance.RefreshDependentDetails x)
                       
                    // todo introduce snapshot mechanism here back only when any consistency issue is carefually checked
                    
                    let initEventIdEndEventIdAndEventA1 = List.zip3 initialStateEventIds1 (newDbBasedEventIds1 |>> List.last) generatedEvents1
                   
                    let aggregateIdInitEventIdEndEventIdAndEventA1 =
                        List.zip aggregateIds1 initEventIdEndEventIdAndEventA1
                        |>> fun (aggregateId, (initEventId, endEventId, events)) -> (aggregateId, initEventId, endEventId, events)
                    let initEventIdEndEventIdAndEventA2 = List.zip3 initialStateEventIds2 (newDbBasedEventIds2 |>> List.last) generatedEvents2
                    let aggregateIdInitEventIdEndEventIdAndEventA2 =
                        List.zip aggregateIds2 initEventIdEndEventIdAndEventA2
                        |>> fun (aggregateId, (initEventId, endEventId, events)) -> (aggregateId, initEventId, endEventId, events)
                    let _ = optionallySendMultipleAggregateEventsAsync<'A1, 'E1> ('A1.Version + 'A1.StorageName) messageSenders aggregateIdInitEventIdEndEventIdAndEventA1
                    let _ = optionallySendMultipleAggregateEventsAsync<'A2, 'E2> ('A2.Version + 'A2.StorageName) messageSenders aggregateIdInitEventIdEndEventIdAndEventA2
                       
                    return ()
                }
        #if USING_MAILBOXPROCESSOR
            let lookupName = sprintf "%s_%s" 'A1.StorageName 'A2.StorageName // aggregateIds
            MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) commands
        #else    
            commands ()
        #endif    
            
    let inline forceRunTwoNAggregateCommands<'A1, 'E1, 'A2, 'E2, 'F
        when 'A1 : (member Id: Guid)
        and 'A1 : (member Serialize: 'F)
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        and 'A2 : (member Id: Guid)
        and 'A2 : (member Serialize: 'F)
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
        (messageSenders: MessageSenders)
        (command1: List<AggregateCommand<'A1, 'E1>>)
        (command2: List<AggregateCommand<'A2, 'E2>>)
        =
            logger.LogDebug "forceRunTwoNAggregateCommands"
            forceRunTwoNAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'F> aggregateIds1 aggregateIds2 eventStore messageSenders String.Empty command1 command2
   
    let inline runTwoNAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'F
        when 'A1 : (member Id: Guid)
        and 'A1 : (member Serialize: 'F)
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        and 'A2 : (member Id: Guid)
        and 'A2 : (member Serialize: 'F)
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
        (messageSenders: MessageSenders)
        (md: Metadata)
        (command1: List<AggregateCommand<'A1, 'E1>>)
        (command2: List<AggregateCommand<'A2, 'E2>>)
        =
            logger.LogDebug "runTwoNAggregateCommands"
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
                       
                    for i in 0 .. (aggregateIds1.Length - 1) do
                        AggregateCache3.Instance.Memoize2 (eventIds1'.[i] |> List.last, newStates1.[i] |> box) aggregateIds1.[i]
                        mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore aggregateIds1.[i] newStates1.[i] (eventIds1'.[i] |> List.last) |> ignore
                    
                    for i in 0 .. (aggregateIds2.Length - 1) do
                        AggregateCache3.Instance.Memoize2 (eventIds2'.[i] |> List.last, newStates2.[i] |> box) aggregateIds2.[i]
                        mkAggregateSnapshotIfIntervalPassed2<'A2, 'E2, 'F> eventStore aggregateIds2.[i] newStates2.[i] (eventIds2'.[i] |> List.last) |> ignore
                        
                    let _ =    
                        aggregateIds1 @ aggregateIds2
                        |> List.iter (fun x -> DetailsCache.Instance.RefreshDependentDetails x)
                
                    let aggregateIdInitialEventIdEndEventIdAndEventsA1 =
                        let initialEventIdEndEventIdAndEventsA1 =
                            List.zip3 eventIds1 (eventIds1' |>> List.last) (events1 |>> snd)
                        List.zip aggregateIds1 initialEventIdEndEventIdAndEventsA1
                        |>> fun (id, (eventId, endEventId, events)) -> (id, eventId, endEventId, events)
                        
                    let aggregateIdInitialEventIdEndEventIdAndEventsA2 =
                        let initialEventIdEndEventIdAndEventsA2 =
                            List.zip3 eventIds2 (eventIds2' |>> List.last) (events2 |>> snd)
                        List.zip aggregateIds2 initialEventIdEndEventIdAndEventsA2
                        |>> fun (id, (eventId, endEventId, events)) -> (id, eventId, endEventId, events)
                    
                    let _ =
                        optionallySendMultipleAggregateEventsAsync<'A1, 'E1> ('A1.Version + 'A1.StorageName) messageSenders  aggregateIdInitialEventIdEndEventIdAndEventsA1
                    let _ =
                        optionallySendMultipleAggregateEventsAsync<'A2, 'E2> ('A2.Version + 'A2.StorageName) messageSenders  aggregateIdInitialEventIdEndEventIdAndEventsA2
                        
                    return ()
                }
        #if USING_MAILBOXPROCESSOR   
            let lookupName = sprintf "%s_%s" 'A1.StorageName 'A2.StorageName // aggregateIds
            MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) commands
        #else
            commands ()
        #endif
        
    let inline runTwoNAggregateCommands<'A1, 'E1, 'A2, 'E2, 'F
        when 'A1 : (member Id: Guid)
        and 'A1 : (member Serialize: 'F)
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        and 'A2 : (member Id: Guid)
        and 'A2 : (member Serialize: 'F)
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
        (messageSenders: MessageSenders)
        (command1: List<AggregateCommand<'A1, 'E1>>)
        (command2: List<AggregateCommand<'A2, 'E2>>)
        =
            logger.LogDebug "runTwoNAggregateCommands"
            runTwoNAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'F> aggregateIds1 aggregateIds2 eventStore messageSenders Metadata.Empty command1 command2
    
    let inline forceRunThreeNAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'A3, 'E3, 'F
        when 'A1 : (member Id: Guid)
        and 'A1 : (member Serialize: 'F)
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        and 'A2 : (member Id: Guid)
        and 'A2 : (member Serialize: 'F)
        and 'E2 :> Event<'A2>
        and 'E2 : (member Serialize: 'F)
        and 'E2 : (static member Deserialize: 'F -> Result<'E2, string>)
        and 'A2 : (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2 : (static member SnapshotsInterval: int)
        and 'A2 : (static member StorageName: string)
        and 'A2 : (static member Version: string)
        and 'A3 : (member Id: Guid)
        and 'A3 : (member Serialize: 'F)
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
        (messageSenders: MessageSenders)
        (md: Metadata)
        (command1: List<AggregateCommand<'A1, 'E1>>)
        (command2: List<AggregateCommand<'A2, 'E2>>)
        (command3: List<AggregateCommand<'A3, 'E3>>)
        =
            logger.LogDebug "forceRunThreeNAggregateCommandsMd"
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
                    
                    let initialStatesAndMultiCommands1 =
                        let uniqueInitialStatesOnly1 =
                            uniqueInitialStates1
                            |>> fun (_, state) -> state
                        let multiCommands1 =
                            aggregateIdsWithCommands1
                            |>> fun (_, cmds) -> cmds
                        List.zip uniqueInitialStatesOnly1 multiCommands1
                    
                    let initialStatesAndMultiCommands2 =
                        let uniqueInitialStatesOnly2 =
                            uniqueInitialStates2
                            |>> fun (_, state) -> state
                        let multiCommands2 =
                            aggregateIdsWithCommands2
                            |>> fun (_, cmds) -> cmds
                        List.zip uniqueInitialStatesOnly2 multiCommands2
                    
                    let initialStatesAndMultiCommands3 =
                        let uniqueInitialStatesOnly3 =
                            uniqueInitialStates3
                            |>> fun (_, state) -> state
                        let multiCommands3 =
                            aggregateIdsWithCommands3
                            |>> fun (_, cmds) -> cmds
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
                        
                    let generatedEvents1 =
                        newStatesAndEvents1
                        |>> snd
                    let generatedEvents2 =
                        newStatesAndEvents2
                        |>> snd
                    let generatedEvents3 =
                        newStatesAndEvents3
                        |>> snd
                    
                    let initialEventIds1 =
                        uniqueInitialStates1
                        |>> fst
                    let initialEventIds2 =
                        uniqueInitialStates2
                        |>> fst
                    let initialEventIds3 =
                        uniqueInitialStates3
                        |>> fst
                    
                    let! dbEventIds =
                        let packParametersForDb1 =
                            let serEvents1 =
                                generatedEvents1
                                |>> fun  x -> x |>> fun (z: 'E1) -> z.Serialize
                            List.zip3 initialEventIds1 serEvents1 uniqueAggregateIds1
                            |>> fun (eventId, events, id) -> (eventId, events, 'A1.Version, 'A1.StorageName, id)
                        let packParametersForDb2 =
                            let serEvents2 =
                                generatedEvents2
                                |>> fun x -> x |>> fun (z: 'E2) -> z.Serialize
                            List.zip3 initialEventIds2 serEvents2 uniqueAggregateIds2
                            |>> fun (eventId, events, id) -> (eventId, events, 'A2.Version, 'A2.StorageName, id)
                        let packParametersForDb3 =
                            let serEvents3 =
                                generatedEvents3
                                |>> fun x -> x |>> fun (z: 'E3) -> z.Serialize
                            List.zip3 initialEventIds3 serEvents3 uniqueAggregateIds3
                            |>> fun (eventId, events, id) -> (eventId, events, 'A3.Version, 'A3.StorageName, id)
                        
                        let allPacked = packParametersForDb1 @ packParametersForDb2 @ packParametersForDb3
                        
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
                        let newStates1 = newStatesAndEvents1 |>> fst
                        let newStates2 = newStatesAndEvents2 |>> fst
                        let newStates3 = newStatesAndEvents3 |>> fst    
                        fun () ->
                            for i in 0 .. (uniqueAggregateIds1.Length - 1) do
                                AggregateCache3.Instance.Memoize2 (newDbBasedEventIds1.[i] |> List.last, newStates1.[i] |> box) aggregateIds1.[i]
                                 
                            for i in 0 .. (uniqueAggregateIds2.Length - 1) do
                                AggregateCache3.Instance.Memoize2 (newDbBasedEventIds2.[i] |> List.last, newStates2.[i] |> box) aggregateIds2.[i]
                               
                            for i in 0 .. (uniqueAggregateIds3.Length - 1) do
                                AggregateCache3.Instance.Memoize2 (newDbBasedEventIds3.[i] |> List.last, newStates3.[i] |> box) aggregateIds3.[i]
                    doCaches () 
                    
                    let allIds = aggregateIds1 @ aggregateIds2 @ aggregateIds3
                    let duplicateIds =
                        allIds
                        |> List.groupBy id
                        |> List.filter (fun (_, l) -> l.Length > 1)
                        |> List.map (fun (id, _) -> id)
                    
                    // cache need invalidation for repeated ids
                    let _ =
                        duplicateIds
                        |> List.iter (fun id ->
                            AggregateCache3.Instance.Clean id
                        )
                        
                    let _ =
                        aggregateIds1 @ aggregateIds2 @ aggregateIds3
                        |> List.distinct
                        |> List.iter (fun id ->
                            DetailsCache.Instance.RefreshDependentDetails id
                        )
                    
                    // quick fix: avoid this computation if there are no message senders
                    match messageSenders with
                    | MessageSenders.NoSender ->
                        return ()
                    | _ ->    
                        let aggregateIdInitialEventIdEndEventIdAndEventsA1 =
                            let initialEventIdEndEventIdAndEventsA1 =
                                List.zip3 initialEventIds1 (newDbBasedEventIds1 |>> List.last) generatedEvents1
                            List.zip uniqueAggregateIds1 initialEventIdEndEventIdAndEventsA1
                            |>> fun (id, (eventId, endEventId, events)) -> (id, eventId, endEventId, events)
                        let aggregateIdInitialEventIdEndEventIdAndEventsA2 =
                            let initialEventIdEndEventIdAndEventsA2 =
                                List.zip3 initialEventIds2 (newDbBasedEventIds2 |>> List.last) generatedEvents2
                            List.zip uniqueAggregateIds2 initialEventIdEndEventIdAndEventsA2
                            |>> fun (id, (eventId, endEventId, events)) -> (id, eventId, endEventId, events)
                        let aggregateIdInitialEventIdEndEventIdAndEventsA3 =
                            let initialEventIdEndEventIdAndEventsA3 =
                                List.zip3 initialEventIds3 (newDbBasedEventIds3 |>> List.last) generatedEvents3
                            List.zip uniqueAggregateIds3 initialEventIdEndEventIdAndEventsA3
                            |>> fun (id, (eventId, endEventId, events)) -> (id, eventId, endEventId, events)
                           
                        let _ =
                            optionallySendMultipleAggregateEventsAsync<'A1, 'E1> ('A1.Version + 'A1.StorageName) messageSenders aggregateIdInitialEventIdEndEventIdAndEventsA1
                        let _ =
                            optionallySendMultipleAggregateEventsAsync<'A2, 'E2> ('A2.Version + 'A2.StorageName) messageSenders aggregateIdInitialEventIdEndEventIdAndEventsA2
                        let _ =
                            optionallySendMultipleAggregateEventsAsync<'A3, 'E3> ('A3.Version + 'A3.StorageName) messageSenders aggregateIdInitialEventIdEndEventIdAndEventsA3
                        
                        return ()
                }
        
        #if USING_MAILBOXPROCESSOR 
            let lookupName = sprintf "%s_%s_%s" 'A1.StorageName 'A2.StorageName 'A3.StorageName // aggregateIds
            MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) commands
        #else
            commands ()
        #endif    
                
    let inline forceRunThreeNAggregateCommands<'A1, 'E1, 'A2, 'E2, 'A3, 'E3, 'F
        when 'A1 : (member Id: Guid)
        and 'A1 : (member Serialize: 'F)
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        and 'A2 : (member Id: Guid)
        and 'A2 : (member Serialize: 'F)
        and 'E2 :> Event<'A2>
        and 'E2 : (member Serialize: 'F)
        and 'E2 : (static member Deserialize: 'F -> Result<'E2, string>)
        and 'A2 : (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2 : (static member SnapshotsInterval: int)
        and 'A2 : (static member StorageName: string)
        and 'A2 : (static member Version: string)
        and 'A3 : (member Id: Guid)
        and 'A3 : (member Serialize: 'F)
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
        (messageSenders: MessageSenders)
        (command1: List<AggregateCommand<'A1, 'E1>>)
        (command2: List<AggregateCommand<'A2, 'E2>>)
        (command3: List<AggregateCommand<'A3, 'E3>>)
        =
            logger.LogDebug "runThreeNAggregateCommands"
            forceRunThreeNAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'A3, 'E3, 'F> aggregateIds1 aggregateIds2 aggregateIds3 eventStore messageSenders Metadata.Empty command1 command2 command3

    let inline runThreeNAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'A3, 'E3, 'F
        when 'A1 : (member Id: Guid)
        and 'A1 : (member Serialize: 'F)
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        and 'A2 : (member Id: Guid)
        and 'A2 : (member Serialize: 'F)
        and 'E2 :> Event<'A2>
        and 'E2 : (member Serialize: 'F)
        and 'E2 : (static member Deserialize: 'F -> Result<'E2, string>)
        and 'A2 : (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2 : (static member SnapshotsInterval: int)
        and 'A2 : (static member StorageName: string)
        and 'A2 : (static member Version: string)
        and 'A3 : (member Id: Guid)
        and 'A3 : (member Serialize: 'F)
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
        (messageSenders: MessageSenders)
        (md: Metadata)
        (command1: List<AggregateCommand<'A1, 'E1>>)
        (command2: List<AggregateCommand<'A2, 'E2>>)
        (command3: List<AggregateCommand<'A3, 'E3>>)
        =
            logger.LogDebug "runThreeNAggregateCommands"
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
                        let eventIds1' = eventIds |> List.take aggregateIds1.Length
                        let eventIds2' = eventIds |> List.skip aggregateIds1.Length |> List.take aggregateIds2.Length
                        let eventIds3' = eventIds |> List.skip (aggregateIds1.Length + aggregateIds2.Length)
                            
                        for i in 0..(aggregateIds1.Length - 1) do
                            AggregateCache3.Instance.Memoize2 (eventIds1'.[i] |> List.last, newStates1.[i] |> box) aggregateIds1.[i]
                            mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore aggregateIds1.[i] newStates1.[i] (eventIds1'.[i] |> List.last) |> ignore
                        for i in 0..(aggregateIds2.Length - 1) do
                            AggregateCache3.Instance.Memoize2 (eventIds2'.[i] |> List.last, newStates2.[i] |> box) aggregateIds2.[i]
                            mkAggregateSnapshotIfIntervalPassed2<'A2, 'E2, 'F> eventStore aggregateIds2.[i] newStates2.[i] (eventIds2'.[i] |> List.last) |> ignore
                        for i in 0..(aggregateIds3.Length - 1) do
                            AggregateCache3.Instance.Memoize2 (eventIds3'.[i] |> List.last, newStates3.[i] |> box) aggregateIds3.[i]
                            mkAggregateSnapshotIfIntervalPassed2<'A3, 'E3, 'F> eventStore aggregateIds3.[i] newStates3.[i] (eventIds3'.[i] |> List.last) |> ignore
                        
                        let aggregateId1InitialEventIdEndEventIdAndEventsA1 =
                            let initialEventIdEndEventIdAndEventsA1 =
                                List.zip3 eventIds1 (eventIds1' |>> List.last) (events1 |>> snd)
                            List.zip aggregateIds1 initialEventIdEndEventIdAndEventsA1
                            |>> fun (id, (eventId, endEventId, events)) -> (id, eventId, endEventId, events)
                        let aggregateId2InitialEventIdEndEventIdAndEventsA2 =
                            let initialEventIdEndEventIdAndEventsA2 =
                                List.zip3 eventIds2 (eventIds2' |>> List.last) (events2 |>> snd)
                            List.zip aggregateIds2 initialEventIdEndEventIdAndEventsA2
                            |>> fun (id, (eventId, endEventId, events)) -> (id, eventId, endEventId, events)
                        let aggregateId3InitialEventIdEndEventIdAndEventsA3 =
                            let initialEventIdEndEventIdAndEventsA3 =
                                List.zip3 eventIds3 (eventIds3' |>> List.last) (events3 |>> snd)
                            List.zip aggregateIds3 initialEventIdEndEventIdAndEventsA3
                            |>> fun (id, (eventId, endEventId, events)) -> (id, eventId, endEventId, events)
                        
                        let _ =
                            optionallySendMultipleAggregateEventsAsync<'A1, 'E1> ('A1.Version + 'A1.StorageName) messageSenders aggregateId1InitialEventIdEndEventIdAndEventsA1
                        let _ =
                            optionallySendMultipleAggregateEventsAsync<'A2, 'E2> ('A2.Version + 'A2.StorageName) messageSenders aggregateId2InitialEventIdEndEventIdAndEventsA2
                        let _ =
                            optionallySendMultipleAggregateEventsAsync<'A3, 'E3> ('A3.Version + 'A3.StorageName) messageSenders aggregateId3InitialEventIdEndEventIdAndEventsA3
                            
                        let _ =
                            aggregateIds1 @ aggregateIds2 @ aggregateIds3
                            |> List.distinct
                            |> List.iter DetailsCache.Instance.RefreshDependentDetails
                         
                        return ()
                    }
            #if USING_MAILBOXPROCESSOR     
                let lookupName = sprintf "%s_%s_%s" 'A1.StorageName 'A2.StorageName 'A3.StorageName // aggregateIds
                MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) commands
            #else    
                commands ()
            #endif    
    let inline runThreeNAggregateCommands<'A1, 'E1, 'A2, 'E2, 'A3, 'E3, 'F
        when 'A1 : (member Id: Guid)
        and 'A1 : (member Serialize: 'F)
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        and 'A2 : (member Id: Guid)
        and 'A2 : (member Serialize: 'F)
        and 'E2 :> Event<'A2>
        and 'E2 : (member Serialize: 'F)
        and 'E2 : (static member Deserialize: 'F -> Result<'E2, string>)
        and 'A2 : (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2 : (static member SnapshotsInterval: int)
        and 'A2 : (static member StorageName: string)
        and 'A2 : (static member Version: string)
        and 'A3 : (member Id: Guid)
        and 'A3 : (member Serialize: 'F)
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
        (messageSenders: MessageSenders)
        (command1: List<AggregateCommand<'A1, 'E1>>)
        (command2: List<AggregateCommand<'A2, 'E2>>)
        (command3: List<AggregateCommand<'A3, 'E3>>)
        =
            logger.LogDebug "runThreeNAggregateCommands"
            runThreeNAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'A3, 'E3, 'F> aggregateIds1 aggregateIds2 aggregateIds3 eventStore messageSenders Metadata.Empty command1 command2 command3
    
    let inline runThreeAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'A3, 'E3, 'F
        when 'A1 : (member Id: Guid)
        and 'A1 : (member Serialize: 'F)
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        and 'A2 : (member Id: Guid)
        and 'A2 : (member Serialize: 'F)
        and 'E2 :> Event<'A2>
        and 'E2 : (member Serialize: 'F)
        and 'E2 : (static member Deserialize: 'F -> Result<'E2, string>)
        and 'A2 : (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2 : (static member SnapshotsInterval: int)
        and 'A2 : (static member StorageName: string)
        and 'A2 : (static member Version: string)
        and 'A3 : (member Id: Guid)
        and 'A3 : (member Serialize: 'F)
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
        (messageSenders: MessageSenders)
        (metadata: Metadata)
        (command1: AggregateCommand<'A1, 'E1>)
        (command2: AggregateCommand<'A2, 'E2>)
        (command3: AggregateCommand<'A3, 'E3>)
        =
            logger.LogDebug "runThreeAggregateCommandsMdRefactor"
            result
                {
                    let! a1ExecutedCommand =  preExecuteAggregateCommandMd<'A1, 'E1, 'F> aggregateId1 eventStore messageSenders metadata command1
                    let! a2ExecutedCommand = preExecuteAggregateCommandMd<'A2, 'E2, 'F> aggregateId2 eventStore messageSenders metadata command2
                    let! a3ExecutedCommand =  preExecuteAggregateCommandMd<'A3, 'E3, 'F> aggregateId3 eventStore messageSenders metadata command3
                    let! ids =
                        storeMultipleEvents eventStore messageSenders
                            [a1ExecutedCommand
                             a2ExecutedCommand
                             a3ExecutedCommand]
                    
                    AggregateCache3.Instance.Memoize2 (ids.[0] |> List.last, a1ExecutedCommand.NewState |> box) aggregateId1
                    AggregateCache3.Instance.Memoize2 (ids.[1] |> List.last, a2ExecutedCommand.NewState |> box) aggregateId2
                    AggregateCache3.Instance.Memoize2 (ids.[2] |> List.last, a3ExecutedCommand.NewState |> box) aggregateId3
                    
                    [aggregateId1; aggregateId2; aggregateId3]
                    |> List.iter DetailsCache.Instance.RefreshDependentDetails
                    
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore aggregateId1 (a1ExecutedCommand.NewState |> unbox) (ids.[0] |> List.last)
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A2, 'E2, 'F> eventStore aggregateId2 (a2ExecutedCommand.NewState |> unbox) (ids.[1] |> List.last)
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A3, 'E3, 'F> eventStore aggregateId3 (a3ExecutedCommand.NewState |> unbox) (ids.[2] |> List.last)
                        
                    let oriEventsA1 =
                        a1ExecutedCommand.SerializedEvents |>> ('E1.Deserialize >> Result.get) // unplausible error
                    let oriEventsA2 =
                        a2ExecutedCommand.SerializedEvents |>> ('E2.Deserialize >> Result.get)
                    let oriEventsA3 =
                        a3ExecutedCommand.SerializedEvents |>> ('E3.Deserialize >> Result.get)
                    
                    let _ =
                        optionallySendAggregateEventsAsync<'A1, 'E1> ('A1.Version + 'A1.StorageName) messageSenders aggregateId1 oriEventsA1 a1ExecutedCommand.EventId (ids.[0] |> List.last)
                    let _ =
                        optionallySendAggregateEventsAsync<'A2, 'E2> ('A2.Version + 'A2.StorageName) messageSenders aggregateId2 oriEventsA2 a2ExecutedCommand.EventId (ids.[1] |> List.last)
                    let _ =
                        optionallySendAggregateEventsAsync<'A3, 'E3> ('A3.Version + 'A3.StorageName) messageSenders aggregateId3 oriEventsA3 a3ExecutedCommand.EventId (ids.[2] |> List.last)
                    
                    return ()
                }
            
    let inline runThreeAggregateCommands<'A1, 'E1, 'A2, 'E2, 'A3, 'E3, 'F
        when 'A1 : (member Id: Guid)
        and 'A1 : (member Serialize: 'F)
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: 'F)
        and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        and 'A2 : (member Id: Guid)
        and 'A2 : (member Serialize: 'F)
        and 'E2 :> Event<'A2>
        and 'E2 : (member Serialize: 'F)
        and 'E2 : (static member Deserialize: 'F -> Result<'E2, string>)
        and 'A2 : (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A2 : (static member SnapshotsInterval: int)
        and 'A2 : (static member StorageName: string)
        and 'A2 : (static member Version: string)
        and 'A3 : (member Id: Guid)
        and 'A3 : (member Serialize: 'F)
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
        (messageSenders: MessageSenders)
        (command1: AggregateCommand<'A1, 'E1>)
        (command2: AggregateCommand<'A2, 'E2>)
        (command3: AggregateCommand<'A3, 'E3>)
        =
            logger.LogDebug "runThreeAggregateCommands"
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
            logger.LogDebug (sprintf "runTwoCommands %A %A" command1 command2)
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
            logger.LogDebug (sprintf "runTwoCommands %A %A" command1 command2)
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
            logger.LogDebug (sprintf "runThreeCommands %A %A %A" command1 command2 command3)
            
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
            logger.LogDebug (sprintf "runThreeCommands %A %A %A" command1 command2 command3)
            runThreeCommandsMd<'A1, 'A2, 'A3, 'E1, 'E2, 'E3, 'F> storage eventBroker Metadata.Empty command1 command2 command3
            
    // this needs to be extended to snapshots flagged as 'deleted'        
    let inline GDPRResetSnapshotsAndEventsOfAnAggregate<'A, 'E, 'F
        when 'A: (member Id: Guid)
        and 'A: (member Serialize: 'F)
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
        logger.LogDebug (sprintf "GDPRResetSnapshotsAndEventsOfAnAggregate %A" aggregateId)
        let reset = fun () ->
            result {
                let! _ = eventStore.GDPRReplaceSnapshotsAndEventsOfAnAggregate 'A.Version 'A.StorageName aggregateId emptyGDPRState.Serialize emptyGDPREvent.Serialize
                let! lastAggregateEventId = 
                    eventStore.TryGetLastAggregateEventId 'A.Version 'A.StorageName aggregateId
                    |> Result.ofOption (sprintf "GDPRResetSnapshotsAndEventsOfAnAggregate %s - %s" 'A.StorageName 'A.Version)
                let _ = AggregateCache3.Instance.Memoize2 (lastAggregateEventId, emptyGDPRState |> box) aggregateId
                return ()
            }
        let lookupName = sprintf "%s" 'A.StorageName
    #if USING_MAILBOXPROCESSOR
        let processor = MailBoxProcessors.Processors.Instance.GetProcessor lookupName
        MailBoxProcessors.postToTheProcessor processor reset
    #else    
        reset ()
    #endif    
