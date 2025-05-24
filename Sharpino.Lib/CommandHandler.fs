
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

open FsToolkit.ErrorHandling

// the "md" version of any function is the one that takes a metadata parameter
// the md requires an extra text md field in any event and a proper new funcion on the db side
// like  insert_md{Version}{AggregateStorageName}_aggregate_event_and_return_id
// I rather duplicate the code than make it more complex
// after all what we are going for is leaving only the md version and keep the
// non-md only for backward compatibility
module CommandHandler =

    // will play around DI to improve logging
    // let host = Host.CreateApplicationBuilder().Build()
    //
    // let factory: ILoggerFactory = LoggerFactory.Create(fun builder -> builder.AddConsole() |> ignore)
    
    type UnitResult = ((unit -> unit) * AsyncReplyChannel<unit>)
   
    let logger: Microsoft.Extensions.Logging.ILogger ref = ref NullLogger.Instance
    let setLogger (newLogger: ILogger) =
        logger := newLogger
    
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
            fun (id: Guid) -> getAggregateFreshState<'A, 'E, 'F> id eventStore 

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
                                    if ((lastEventId - snapEventId)) > 'A.SnapshotsInterval || snapEventId = 0 then
                                        let result = storage.SetSnapshot 'A.Version (eventId, state.Serialize) 'A.StorageName
                                        result
                                    else
                                        () |> Ok
                            }
                }, Commons.generalAsyncTimeOut)    
            
    // let inline mkAggregateSnapshotIfIntervalPassed<'A, 'E, 'F
    //     when 'A :> Aggregate<'F> 
    //     and 'E :> Event<'A>
    //     and 'A : (static member Deserialize: 'F -> Result<'A, string>) 
    //     and 'A : (static member StorageName: string)
    //     and 'A : (static member SnapshotsInterval : int)
    //     and 'A : (static member Version: string) 
    //     and 'E : (static member Deserialize: 'F -> Result<'E, string>)
    //     and 'E : (member Serialize: 'F)
    //     >
    //     (storage: IEventStore<'F>)
    //     (aggregateId: AggregateId) =
    //         logger.Value.LogDebug "mkAggregateSnapshotIfIntervalPassed"
    //         Async.RunSynchronously
    //             (async {
    //                 return
    //                     ResultCE.result
    //                         {
    //                             let lastEventId = 
    //                                 storage.TryGetLastAggregateEventId 'A.Version 'A.StorageName aggregateId
    //                                 |> Option.defaultValue 0
    //                             let snapEventId = storage.TryGetLastAggregateSnapshotEventId 'A.Version 'A.StorageName aggregateId |> Option.defaultValue 0
    //                             let result =
    //                                 if ((lastEventId - snapEventId)) >= 'A.SnapshotsInterval || snapEventId = 0 then
    //                                     mkAggregateSnapshot<'A, 'E, 'F > storage aggregateId 
    //                                 else
    //                                     () |> Ok
    //                             return! result
    //                         }
    //             }, Commons.generalAsyncTimeOut)
   
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
                        ResultCE.result
                            {
                                let lastEventId = 
                                    storage.TryGetLastAggregateEventId 'A.Version 'A.StorageName aggregateId
                                    |> Option.defaultValue 0
                                let snapEventId = storage.TryGetLastAggregateSnapshotEventId 'A.Version 'A.StorageName aggregateId |> Option.defaultValue 0
                                let result =
                                    if ((lastEventId - snapEventId)) >= 'A.SnapshotsInterval || snapEventId = 0 then
                                        storage.SetAggregateSnapshot 'A.Version (aggregateId, eventId, state.Serialize) 'A.StorageName
                                        // mkAggregateSnapshot<'A, 'E, 'F > storage aggregateId 
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
                        
                    let events' =
                        events
                        |>> fun x -> x.Serialize
                    let! ids =
                        events' |> eventStore.AddEventsMd eventId 'A.Version 'A.StorageName md
                    
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
        (eventBroker: IEventBroker<'F>)
        (initialInstance: 'A1)
        (md: Metadata)
        (command: Command<'A, 'E>)
        =
            logger.Value.LogDebug (sprintf "runInitAndCommand %A %A" 'A.StorageName command)
            
            let command = fun () ->
                result {
                    let! (eventId, state) = getFreshState<'A, 'E, 'F> storage
                    let! (newState, events)= 
                        state
                        |> command.Execute
                        
                    let events' =
                        events
                        |>> fun x -> x.Serialize
                    let! ids =
                        events' |> storage.SetInitialAggregateStateAndAddEventsMd eventId initialInstance.Id 'A1.Version 'A1.StorageName initialInstance.Serialize 'A.Version 'A.StorageName md

                    StateCache2<'A>.Instance.Memoize2 newState (ids |> List.last)
                    let _ = mkSnapshotIfIntervalPassed2<'A, 'E, 'F> storage newState (ids |> List.last)
                    AggregateCache<'A1,'F>.Instance.Memoize2 (initialInstance |> Ok) (0, initialInstance.Id)
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
        (eventBroker: IEventBroker<'F>)
        (initialInstance: 'A1) =
            logger.Value.LogDebug (sprintf "runInit %A" 'A1.StorageName)
            result {
                let! notAlreadyExists =
                    (StateView.getAggregateFreshState<'A1, 'E, 'F> initialInstance.Id eventStore
                    |> Result.isError) // it must be true that this is an error to continue
                    |> Result.ofBool (sprintf "Aggregate with id %A of type %s already exists" initialInstance.Id 'A1.StorageName)
                        
                let! _ = eventStore.SetInitialAggregateState initialInstance.Id 'A1.Version 'A1.StorageName initialInstance.Serialize
                AggregateCache<'A1, 'F>.Instance.Memoize2 (initialInstance |> Ok) (0, initialInstance.Id)
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
        (eventBroker: IEventBroker<'F>)
        (initialInstance: 'A1)
        (command: Command<'A, 'E>)
        =
            logger.Value.LogDebug (sprintf "runInitAndCommand %A %A" 'A.StorageName command)
            runInitAndCommandMd<'A, 'E, 'A1, 'F> storage eventBroker initialInstance Metadata.Empty command
            
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
        (eventBroker: IEventBroker<'F>)
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
                        |> command.Execute
                    let  events' =
                        events 
                        |>> fun x -> x.Serialize
                    let! ids =
                        events' |> storage.SetInitialAggregateStateAndAddAggregateEventsMd eventId initialInstance.Id 'A2.Version 'A2.StorageName aggregateId initialInstance.Serialize 'A1.Version 'A1.StorageName md
                        
                    AggregateCache<'A1, 'F>.Instance.Memoize2 (newState |> Ok) ((ids |> List.last, aggregateId))
                    AggregateCache<'A2, 'F>.Instance.Memoize2 (initialInstance |> Ok) (0, initialInstance.Id)
            
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> storage aggregateId newState (ids |> List.last)
                    
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
        (eventBroker: IEventBroker<'F>)
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
                        |>> fun (state, command) -> (command.Execute state)
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
                        |> storage.SetInitialAggregateStateAndMultiAddAggregateEventsMd initialInstance.Id 'A2.Version 'A2.StorageName initialInstance.Serialize "" 
                        
                    for i in 0..(aggregateIds.Length - 1) do
                        AggregateCache<'A1, 'F>.Instance.Memoize2 (newStates.[i] |> Ok) (eventIds.[i] |> List.last, aggregateIds.[i])
                        mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> storage aggregateIds.[i] newStates.[i] (eventIds.[i] |> List.last) |> ignore
                    AggregateCache<'A2, 'F>.Instance.Memoize2 (initialInstance |> Ok)  (0, initialInstance.Id)
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
        (eventBroker: IEventBroker<'F>)
        (initialInstance: 'A2)
        (command: AggregateCommand<'A1, 'E1>)
        =
            logger.Value.LogDebug (sprintf "runInitAndAggregateCommand %A %A" 'A1.StorageName command)
            runInitAndAggregateCommandMd<'A1, 'E1, 'A2, 'F> aggregateId storage eventBroker initialInstance String.Empty command
            
    let inline runInitAndTwoAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'F, 'A3
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
        (eventBroker: IEventBroker<'F>)
        (initialInstance: 'A3)
        (md: Metadata)
        (command1: AggregateCommand<'A1, 'E1>)
        (command2: AggregateCommand<'A2, 'E2>)
        =
            logger.Value.LogDebug (sprintf "runInitAndTwoAggregateCommands %A %A %A %A %A %A" 'A1.StorageName 'A2.StorageName command1 command2 aggregateId1 aggregateId2)
            let command = fun () ->
                result {
                    let! (eventId1, state1) = getAggregateFreshState<'A1, 'E1, 'F> aggregateId1 eventStore
                    let! (eventId2, state2) = getAggregateFreshState<'A2, 'E2, 'F> aggregateId2 eventStore
                    let! (newState1, events1) =
                        state1
                        |> command1.Execute
                    let! (newState2, events2) =
                        state2
                        |> command2.Execute
                    let events1' =
                        events1
                        |>> fun x -> x.Serialize
                    let events2' =
                        events2
                        |>> fun x -> x.Serialize
                    let multiEvents =
                        [
                            (eventId1, events1', 'A1.Version, 'A1.StorageName, aggregateId1)
                            (eventId2, events2', 'A2.Version, 'A2.StorageName, aggregateId2)
                        ]
                    let! ids =
                        eventStore.SetInitialAggregateStateAndMultiAddAggregateEventsMd initialInstance.Id 'A3.Version 'A3.StorageName initialInstance.Serialize md multiEvents
                    
                    AggregateCache<'A1, 'F>.Instance.Memoize2 (newState1 |> Ok) ((ids.[0] |> List.last, aggregateId1))
                    AggregateCache<'A2, 'F>.Instance.Memoize2 (newState2 |> Ok) ((ids.[1] |> List.last, aggregateId2))
                    AggregateCache<'A3, 'F>.Instance.Memoize2 (initialInstance |> Ok) (0, initialInstance.Id)
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore aggregateId1 newState1 (ids.[0] |> List.last)
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A2, 'E2, 'F> eventStore aggregateId2 newState2 (ids.[1] |> List.last)
                    
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
        (eventBroker: IEventBroker<'F>)
        (initialInstance: 'A3)
        (command1: AggregateCommand<'A1, 'E1>)
        (command2: AggregateCommand<'A2, 'E2>)
        =
            logger.Value.LogDebug (sprintf "runInitAndTwoAggregateCommands %A %A %A %A %A %A" 'A1.StorageName 'A2.StorageName command1 command2 aggregateId1 aggregateId2)
            runInitAndTwoAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'F, 'A3> aggregateId1 aggregateId2 eventStore eventBroker initialInstance String.Empty command1 command2
    
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
        (eventBroker: IEventBroker<'F>)
        (initialInstance: 'A4)
        (md: Metadata)
        (command1: AggregateCommand<'A1, 'E1>)
        (command2: AggregateCommand<'A2, 'E2>)
        (command3: AggregateCommand<'A3, 'E3>)
        =
            logger.Value.LogDebug (sprintf "runInitAndThreeAggregateCommands %A %A %A %A %A %A %A %A %A" 'A1.StorageName 'A2.StorageName 'A3.StorageName command1 command2 command3 aggregateId1 aggregateId2 aggregateId3)
            let command = fun () ->
                result {
                    let! (eventId1, state1) = getAggregateFreshState<'A1, 'E1, 'F> aggregateId1 eventStore
                    let! (eventId2, state2) = getAggregateFreshState<'A2, 'E2, 'F> aggregateId2 eventStore
                    let! (eventId3, state3) = getAggregateFreshState<'A3, 'E3, 'F> aggregateId3 eventStore
                    let! (newState1, events1) =
                        state1
                        |> command1.Execute
                    let! (newState2, events2) =
                        state2
                        |> command2.Execute
                    let! (newState3, events3) =
                        state3
                        |> command3.Execute
                    let serEvents1 =
                        events1 
                        |>> fun x -> x.Serialize
                    let serEvents2 =
                        events2 
                        |>> fun x -> x.Serialize
                    let serEvents3 =
                        events3 
                        |>> fun x -> x.Serialize
                    let multiEvents =
                        [
                            (eventId1, serEvents1, 'A1.Version, 'A1.StorageName, aggregateId1)
                            (eventId2, serEvents2, 'A2.Version, 'A2.StorageName, aggregateId2)
                            (eventId3, serEvents3, 'A3.Version, 'A3.StorageName, aggregateId3)
                        ]
                    let! ids =
                        eventStore.SetInitialAggregateStateAndMultiAddAggregateEventsMd initialInstance.Id 'A4.Version 'A4.StorageName initialInstance.Serialize md multiEvents
                    
                    AggregateCache<'A1,'F>.Instance.Memoize2 (newState1 |> Ok) ((ids.[0] |> List.last, aggregateId1))
                    AggregateCache<'A2,'F>.Instance.Memoize2 (newState2 |> Ok) ((ids.[1] |> List.last, aggregateId2))
                    AggregateCache<'A3,'F>.Instance.Memoize2 (newState3 |> Ok) ((ids.[2] |> List.last, aggregateId3))
                    
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore aggregateId1 newState1 (ids.[0] |> List.last)
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A2, 'E2, 'F> eventStore aggregateId2 newState2 (ids.[1] |> List.last)
                    let _ = mkAggregateSnapshotIfIntervalPassed2<'A3, 'E3, 'F> eventStore aggregateId3 newState3 (ids.[2] |> List.last)
                    
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
        (eventBroker: IEventBroker<'F>)
        (initialInstance: 'A4)
        (command1: AggregateCommand<'A1, 'E1>)
        (command2: AggregateCommand<'A2, 'E2>)
        (command3: AggregateCommand<'A3, 'E3>)
        =
            logger.Value.LogDebug (sprintf "runInitAndThreeAggregateCommands %A %A %A %A %A %A %A %A %A" 'A1.StorageName 'A2.StorageName 'A3.StorageName command1 command2 command3 aggregateId1 aggregateId2 aggregateId3)
            runInitAndThreeAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'A3, 'E3, 'F, 'A4> aggregateId1 aggregateId2 aggregateId3 eventStore eventBroker initialInstance Metadata.Empty command1 command2 command3

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
        (eventBroker: IEventBroker<'F>)
        (md: Metadata)
        (command: AggregateCommand<'A, 'E>)
        =
            logger.Value.LogDebug (sprintf "runAggregateCommand %A,  %A, id: %A" 'A.StorageName command  aggregateId)
            let command = fun () ->
                result {
                    let! (eventId, state) = getAggregateFreshState<'A, 'E, 'F> aggregateId storage
                    let! (newState, events) =
                        state
                        |> command.Execute
                    let serEvents =
                        events 
                        |>> fun x -> x.Serialize
                    let! ids =
                        serEvents |> storage.AddAggregateEventsMd eventId 'A.Version 'A.StorageName state.Id md
                   
                    AggregateCache<'A, 'F>.Instance.Memoize2 (newState |> Ok) ((ids |> List.last, state.Id))
                    
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
        (eventBroker: IEventBroker<'F>) 
        (command: AggregateCommand<'A, 'E>)
        =
            logger.Value.LogDebug (sprintf "runAggregateCommand %A,  %A, id: %A" 'A.StorageName command  aggregateId)
            runAggregateCommandMd<'A, 'E, 'F> aggregateId storage eventBroker Metadata.Empty command
    
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
        (eventBroker: IEventBroker<'F>)
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
                        |> List.traverseResultM (fun (state, cmds) -> foldCommands state cmds)
                        
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
                        AggregateCache<'A1, 'F>.Instance.Memoize2 (newStates.[i] |> Ok) (dbEventIds.[i] |> List.last, uniqueAggregateIds.[i])
                        mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore uniqueAggregateIds.[i] newStates.[i] (dbEventIds.[i] |> List.last) |> ignore
                        
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
        (eventBroker: IEventBroker<'F>)
        (commands: List<AggregateCommand<'A1, 'E1>>)
        =
            logger.Value.LogDebug "forceRunNAggregateCommands"
            forceRunNAggregateCommandsMd<'A1, 'E1, 'F> aggregateIds eventStore eventBroker Metadata.Empty commands
                
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
        (eventBroker: IEventBroker<'F>)
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
                        |>> fun (state, command) -> (command.Execute state)
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
                        eventStore.MultiAddAggregateEventsMd md currentStateEventIdEventsAndAggregateIds
                    
                    for i in 0..(aggregateIds.Length - 1) do
                        AggregateCache<'A1, 'F>.Instance.Memoize2 (newStates.[i] |> Ok) (eventIds.[i] |> List.last, aggregateIds.[i])
                        mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore aggregateIds.[i] newStates.[i] (eventIds.[i] |> List.last) |> ignore
                        
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
        (eventBroker: IEventBroker<'F>)
        (commands: List<AggregateCommand<'A1, 'E1>>)
        =
            logger.Value.LogDebug "runNAggregateCommands"
            runNAggregateCommandsMd<'A1, 'E1, 'F> aggregateIds eventStore eventBroker Metadata.Empty commands
    
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
        (eventBroker: IEventBroker<'F>)
        (md: Metadata)
        (command1: AggregateCommand<'A1, 'E1>)
        (command2: AggregateCommand<'A2, 'E2>)
        =
            let commands = fun () ->
                result {
                    let! (eventId1, state1) = getAggregateFreshState<'A1, 'E1, 'F> aggregateId1 eventStore
                    let! (eventId2, state2) = getAggregateFreshState<'A2, 'E2, 'F> aggregateId2 eventStore
                    
                    let! (newState1, events1) =
                        state1
                        |> command1.Execute
                    
                    let! (newState2, events2) =
                        state2
                        |> command2.Execute    
                    
                    let serializedEvents1 =
                        events1 
                        |>> fun x -> x.Serialize    
                 
                    let serializedEvents2 =
                        events2
                        |>> fun x -> x.Serialize
                    
                    let! newLastStateIdsList =
                        eventStore.MultiAddAggregateEventsMd
                            md 
                            [
                                (eventId1, serializedEvents1, 'A1.Version, 'A1.StorageName, aggregateId1)
                                (eventId2, serializedEvents2, 'A2.Version, 'A2.StorageName, aggregateId2)
                            ]
                    AggregateCache<'A1, 'F>.Instance.Memoize2 (newState1 |> Ok) ((newLastStateIdsList.[0] |> List.last, aggregateId1))
                    AggregateCache<'A2, 'F>.Instance.Memoize2 (newState2 |> Ok) ((newLastStateIdsList.[1] |> List.last, aggregateId2))
                   
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
        (eventBroker: IEventBroker<'F>)
        (command1: AggregateCommand<'A1, 'E1>)
        (command2: AggregateCommand<'A2, 'E2>)
        =
            runTwoAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'F> aggregateId1 aggregateId2 eventStore eventBroker String.Empty command1 command2
   
    // all those Saga-like functions are convoluted and not really needed, but they are a good way to test the undoer mechanism
    [<Obsolete>]
    let inline runSagaNAggregateCommandsUndoersMd<'A, 'E, 'F
        when 'A :> Aggregate<'F>
        and 'E :> Event<'A>
        and 'E : (member Serialize: 'F)
        and 'E : (static member Deserialize: 'F -> Result<'E, string>)
        and 'A : (static member Deserialize: 'F -> Result<'A, string>)
        and 'A : (static member SnapshotsInterval: int)
        and 'A : (static member StorageName: string)
        and 'A : (static member Version: string)
        >
        (aggregateIds: List<Guid>)
        (eventStore: IEventStore<'F>)
        (eventBroker: IEventBroker<'F>)
        (md: Metadata)
        (undoers: List<Option<Result<(unit -> Result<List<'E>,string>),string>>>)
        =
            let undoerRun =
                fun () ->
                    result {
                        let! compensatingStream =
                            undoers
                            |>> fun x -> x
                            |> List.traverseOptionM (fun x -> x)
                            |> Option.toResultWith (sprintf "compensatingStream %s" 'A.StorageName)
                        let! extractedCompensator =
                            compensatingStream
                            |> List.traverseResultM (fun x -> x) 
                        let! extractedCompensatorApplied =
                            extractedCompensator
                            |> List.traverseResultM (fun x -> x())
                        let! currentStatesOfAggregates =
                            aggregateIds
                            |> List.traverseResultM (fun id -> getAggregateFreshState<'A, 'E, 'F> id eventStore)
                        let stateAndEvents =
                            List.zip (currentStatesOfAggregates |>> snd) extractedCompensatorApplied
                            
                        // this precomputation of future states will be ok for the future versions of the cache (without eventid)
                        // and for making sure that those events succeeds
                        let! futureStates =
                            stateAndEvents
                            |> List.traverseResultM (fun (state, compensator) -> evolveUNforgivingErrors state compensator)
                                
                        let extractedEvents =
                            let exCompLen = extractedCompensatorApplied.Length
                            List.zip3
                                (aggregateIds |> List.take exCompLen)
                                ((aggregateIds |> List.take exCompLen)
                                 |>> (eventStore.TryGetLastAggregateEventId 'A.Version 'A.StorageName))
                                extractedCompensatorApplied
                            |> List.map (fun (id, a, b) -> id, a |> Option.defaultValue 0, b |>> fun x -> x.Serialize)
                        let addEventsStreamA =
                            extractedEvents
                            |> List.traverseResultM
                                (fun (id, eventId, events) ->
                                    eventStore.AddAggregateEventsMd eventId 'A.Version 'A.StorageName id md events
                                )
                        match addEventsStreamA with
                        | Ok _ ->
                            for i in 0..(aggregateIds.Length - 1) do
                                AggregateCache<'A, 'F>.Instance.Clean aggregateIds.[i]
                            return ()
                        | Error e -> return! Error e
                    }
                
            let lookupName = sprintf "%s" 'A.StorageName
            let tryCompensations =
                
            #if USING_MAILBOXPROCESSOR    
                MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) undoerRun
            #else
                undoerRun ()
            #endif    
                
            let _ =
                match tryCompensations with
                | Error x -> logger.Value.LogError x
                | Ok _ -> logger.Value.LogInformation "compensation Saga succeeded"
            Error (sprintf "action failed needed to compensate. The compensation action had the following result %A" tryCompensations)
            
    // all those Saga-like functions are convoluted and not really needed, but they are a good way to test the undoer mechanism
    [<Obsolete>]
    let inline runSagaNAggregateCommandsUndoers<'A, 'E, 'F
        when 'A :> Aggregate<'F>
        and 'E :> Event<'A>
        and 'E : (member Serialize: 'F)
        and 'E : (static member Deserialize: 'F -> Result<'E, string>)
        and 'A : (static member Deserialize: 'F -> Result<'A, string>)
        and 'A : (static member SnapshotsInterval: int)
        and 'A : (static member StorageName: string)
        and 'A : (static member Version: string)
        >
        (aggregateIds: List<Guid>)
        (eventStore: IEventStore<'F>)
        (eventBroker: IEventBroker<'F>)
        (undoers: List<Option<Result<(unit -> Result<List<'E>,string>),string>>>)
        =
            runSagaNAggregateCommandsUndoersMd<'A, 'E, 'F> aggregateIds eventStore eventBroker Metadata.Empty undoers

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
        (eventBroker: IEventBroker<'F>)
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
                        |> List.traverseResultM (fun (state, commands) -> foldCommands state commands)
                        
                    let! newStatesAndEvents2 =
                        initialStatesAndMultiCommands2
                        |> List.traverseResultM (fun (state, commands) -> foldCommands state commands)
                        
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
                                AggregateCache<'A1, 'F>.Instance.Memoize2 (newStates1.[i] |> Ok) (newDbBasedEventIds1.[i] |> List.last, uniqueAggregateIds1.[i])
                                mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore uniqueAggregateIds1.[i] newStates1.[i] (newDbBasedEventIds1.[i] |> List.last) |> ignore
                                
                            for i in 0 .. (uniqueAggregateIds2.Length - 1) do
                                AggregateCache<'A2, 'F>.Instance.Memoize2 (newStates2.[i] |> Ok) (newDbBasedEventIds2.[i] |> List.last, uniqueAggregateIds2.[i])
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
                                AggregateCache<'A1, 'F>.Instance.Clean id
                        )
                        aggregateIds2 |> List.iter (fun id ->
                            if (duplicatedIds |> List.contains id) then
                                AggregateCache<'A2, 'F>.Instance.Clean id
                        )
                                
                    // if (List.length (uniqueAggregateIds1 @ uniqueAggregateIds2)) = List.length (List.distinct (uniqueAggregateIds1 @ uniqueAggregateIds2)) then
                    //     doCacheResults ()
                        
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
        (eventBroker: IEventBroker<'F>)
        (command1: List<AggregateCommand<'A1, 'E1>>)
        (command2: List<AggregateCommand<'A2, 'E2>>)
        =
            logger.Value.LogDebug "forceRunTwoNAggregateCommands"
            forceRunTwoNAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'F> aggregateIds1 aggregateIds2 eventStore eventBroker String.Empty command1 command2
            
    // // all those Saga-like functions are convoluted and not really needed, but they are a good way to test the undoer mechanism
    // [<Obsolete>]
    // let inline runSagaNAggregatesCommandsAndForceTwoMAggregateCommandsMd<'A, 'E, 'A1, 'E1, 'A2, 'E2, 'F
    //     when 'A :> Aggregate<'F>
    //     and 'E :> Event<'A>
    //     and 'E : (member Serialize: 'F)
    //     and 'E : (static member Deserialize: 'F -> Result<'E, string>)
    //     and 'A : (static member Deserialize: 'F -> Result<'A, string>)
    //     and 'A : (static member SnapshotsInterval: int)
    //     and 'A : (static member StorageName: string)
    //     and 'A : (static member Version: string)
    //     and 'A1 :> Aggregate<'F>
    //     and 'E1 :> Event<'A1>
    //     and 'E1 : (member Serialize: 'F)
    //     and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
    //     and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
    //     and 'A1 : (static member SnapshotsInterval: int)
    //     and 'A1 : (static member StorageName: string)
    //     and 'A1 : (static member Version: string)
    //     and 'A2 :> Aggregate<'F>
    //     and 'E2 :> Event<'A2>
    //     and 'E2 : (member Serialize: 'F)
    //     and 'E2 : (static member Deserialize: 'F -> Result<'E2, string>)
    //     and 'A2 : (static member Deserialize: 'F -> Result<'A2, string>)
    //     and 'A2 : (static member SnapshotsInterval: int)
    //     and 'A2 : (static member StorageName: string)
    //     and 'A2 : (static member Version: string)
    //     >
    //     (aggregateIds: List<Guid>)
    //     (aggregateIds1: List<Guid>)
    //     (aggregateIds2: List<Guid>)
    //     (eventStore: IEventStore<'F>)
    //     (eventBroker: IEventBroker<'F>)
    //     (md: Metadata)
    //     (commands: List<AggregateCommand<'A, 'E>>)
    //     (command1: List<AggregateCommand<'A1, 'E1>>)
    //     (command2: List<AggregateCommand<'A2, 'E2>>)
    //     : Result<unit, string>
    //     =
    //         logger.Value.LogDebug "runSagaNAggregatesCommandsAndForceTwoMAggregateCommands"
    //         let commandsHaveUndoers = commands |> List.forall (fun x -> x.Undoer.IsSome)
    //         if (commandsHaveUndoers) then 
    //             let idsWithCommands = List.zip aggregateIds commands
    //             let iteratedExecutionOfCommands =
    //                 idsWithCommands
    //                 |> List.fold
    //                     (fun acc (id, c) ->
    //                         let guard = acc |> fst
    //                         let futureUndoers = acc |> snd
    //                         if (not guard) then (false, futureUndoers) else
    //                             let state = getAggregateFreshState<'A, 'E, 'F> id eventStore
    //                             let futureUndo =
    //                                 let aggregateStateViewer: AggregateViewer<'A> = fun id -> getAggregateFreshState<'A, 'E, 'F> id eventStore
    //                                 match c.Undoer, state with
    //                                 | Some undoer, Ok (_, st) -> Some (undoer st aggregateStateViewer)
    //                                 | _ -> None
    //                             let undoers = [futureUndo]
    //                             let myRes = runAggregateCommandMd id eventStore eventBroker md c
    //                             match myRes with
    //                             | Ok _ -> (guard, futureUndoers @ undoers)
    //                             | Error _ -> (false, futureUndoers)
    //                     )
    //                     (true, [])
    //             match iteratedExecutionOfCommands with
    //             | (false, undoers) ->
    //                 runSagaNAggregateCommandsUndoersMd<'A, 'E, 'F> aggregateIds eventStore eventBroker md undoers
    //             | (true, undoers) ->
    //                 let runMWithNoSaga =
    //                     forceRunTwoNAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'F> aggregateIds1 aggregateIds2 eventStore eventBroker md command1 command2
    //                 match runMWithNoSaga with
    //                 | Ok _ -> Ok ()
    //                 | Error e -> 
    //                     runSagaNAggregateCommandsUndoersMd<'A, 'E, 'F> aggregateIds eventStore eventBroker md undoers
    //         else Error "no undoers"
            
    // all those Saga-like functions are convoluted and not really needed, but they are a good way to test the undoer mechanism
    
    // [<Obsolete>]
    // let inline runSagaNAggregatesCommandsAndForceTwoMAggregateCommands<'A, 'E, 'A1, 'E1, 'A2, 'E2, 'F
    //     when 'A :> Aggregate<'F>
    //     and 'E :> Event<'A>
    //     and 'E : (member Serialize: 'F)
    //     and 'E : (static member Deserialize: 'F -> Result<'E, string>)
    //     and 'A : (static member Deserialize: 'F -> Result<'A, string>)
    //     and 'A : (static member SnapshotsInterval: int)
    //     and 'A : (static member StorageName: string)
    //     and 'A : (static member Version: string)
    //     and 'A1 :> Aggregate<'F>
    //     and 'E1 :> Event<'A1>
    //     and 'E1 : (member Serialize: 'F)
    //     and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
    //     and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
    //     and 'A1 : (static member SnapshotsInterval: int)
    //     and 'A1 : (static member StorageName: string)
    //     and 'A1 : (static member Version: string)
    //     and 'A2 :> Aggregate<'F>
    //     and 'E2 :> Event<'A2>
    //     and 'E2 : (member Serialize: 'F)
    //     and 'E2 : (static member Deserialize: 'F -> Result<'E2, string>)
    //     and 'A2 : (static member Deserialize: 'F -> Result<'A2, string>)
    //     and 'A2 : (static member SnapshotsInterval: int)
    //     and 'A2 : (static member StorageName: string)
    //     and 'A2 : (static member Version: string)
    //     >
    //     (aggregateIds: List<Guid>)
    //     (aggregateIds1: List<Guid>)
    //     (aggregateIds2: List<Guid>)
    //     (eventStore: IEventStore<'F>)
    //     (eventBroker: IEventBroker<'F>)
    //     (commands: List<AggregateCommand<'A, 'E>>)
    //     (command1: List<AggregateCommand<'A1, 'E1>>)
    //     (command2: List<AggregateCommand<'A2, 'E2>>)
    //     : Result<unit, string>
    //     =
    //         logger.Value.LogDebug "runSagaNAggregatesCommandsAndForceTwoMAggregateCommands"
    //         runSagaNAggregatesCommandsAndForceTwoMAggregateCommandsMd<'A, 'E, 'A1, 'E1, 'A2, 'E2, 'F> aggregateIds aggregateIds1 aggregateIds2 eventStore eventBroker String.Empty commands command1 command2
   
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
        (eventBroker: IEventBroker<'F>)
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
                        |>> fun (state, command) -> command.Execute state
                        |> List.traverseResultM id
                    let! events2 =
                        statesAndCommands2
                        |>> fun (state, command) -> command.Execute state
                        |> List.traverseResultM id
                    let serializedEvents1 =
                        events1 
                        |>> fun (_, x) -> x |>> fun (z: 'E1) -> z.Serialize
                    let serializedEvents2 =
                        events2 
                        |>> fun (_, x) -> x |>> fun (z: 'E2) -> z.Serialize
                        
                    let! statesAndEvents1 =
                        statesAndCommands1
                        |>> fun (state, command) -> (command.Execute state)
                        |> List.traverseResultM id
                    let! statesAndEvents2 =
                        statesAndCommands2
                        |>> fun (state, command) -> (command.Execute state)
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
                        
                    let eventIds1 = eventIds |> List.take aggregateIds1.Length
                    let eventIds2 = eventIds |> List.skip aggregateIds1.Length
                            
                    for i in 0..(aggregateIds1.Length - 1) do
                        AggregateCache<'A1, 'F>.Instance.Memoize2 (newStates1.[i] |> Ok) ((eventIds1.[i] |> List.last, aggregateIds1.[i]))
                        mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore aggregateIds1.[i] newStates1.[i] (eventIds1.[i] |> List.last) |> ignore
                    
                    for i in 0..(aggregateIds2.Length - 1) do
                       AggregateCache<'A2, 'F>.Instance.Memoize2 (newStates2.[i] |> Ok) ((eventIds2.[i] |> List.last, aggregateIds2.[i]))
                       mkAggregateSnapshotIfIntervalPassed2<'A2, 'E2, 'F> eventStore aggregateIds2.[i] newStates2.[i] (eventIds2.[i] |> List.last) |> ignore
                     
                    // supposed useful for publishing events:
                    // let aggregateIdsWithEventIds1 =
                    //     List.zip aggregateIds1 eventIds1
                    // let aggregateIdsWithEventIds2 =
                    //     List.zip aggregateIds2 eventIds2
                        
                    return ()
                }
            // using the aggregateIds to determine the name of the mailboxprocessor can be overkill: revise this ASAP
            // let aggregateIds = aggregateIds1 @ aggregateIds2 |> List.map (fun x -> x.ToString()) |> List.sort |> String.concat "_"
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
        (eventBroker: IEventBroker<'F>)
        (command1: List<AggregateCommand<'A1, 'E1>>)
        (command2: List<AggregateCommand<'A2, 'E2>>)
        =
            logger.Value.LogDebug "runTwoNAggregateCommands"
            runTwoNAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'F> aggregateIds1 aggregateIds2 eventStore eventBroker Metadata.Empty command1 command2
    
    // all those Saga-like functions are convoluted and not really needed, but they are a good way to test the undoer mechanism
    [<Obsolete>]
    let inline runSagaNAggregateCommandsMd<'A, 'E, 'F
        when 'A :> Aggregate<'F>
        and 'E :> Event<'A>
        and 'E : (member Serialize: 'F)
        and 'E : (static member Deserialize: 'F -> Result<'E, string>)
        and 'A : (static member Deserialize: 'F -> Result<'A, string>)
        and 'A : (static member SnapshotsInterval: int)
        and 'A : (static member StorageName: string)
        and 'A : (static member Version: string)
        >
        (aggregateIds: List<Guid>)
        (eventStore: IEventStore<'F>)
        (eventBroker: IEventBroker<'F>)
        (md: Metadata)
        (commands: List<AggregateCommand<'A, 'E>>)
        =
            let commandHasUndoers = commands |> List.forall (fun x -> x.Undoer.IsSome)
            if commandHasUndoers then
                let idsWithCommands = List.zip aggregateIds commands
                let iteratedExecutionOfCommands =
                    idsWithCommands
                    |> List.fold
                        (fun (guard, futureUndoers) (id, c) ->
                            if not guard then (false, futureUndoers) else
                                let state = getAggregateFreshState<'A, 'E, 'F> id eventStore
                                let futureUndo =
                                    let aggregateStateViewer: AggregateViewer<'A> = fun id -> getAggregateFreshState<'A, 'E, 'F> id eventStore
                                    match c.Undoer, state with
                                    | Some undoer, Ok (_, st) -> Some (undoer st aggregateStateViewer)
                                    | _ -> None
                                let undoers = [futureUndo]
                                let myRes = runAggregateCommand id eventStore eventBroker c
                                match myRes with
                                | Ok _ -> (guard, futureUndoers @ undoers)
                                | Error _ -> (false, futureUndoers)
                        )
                        (true, [])
                match iteratedExecutionOfCommands with
                | (true, _) -> Ok ()
                | (false, undoers) ->
                    let undoerRun =
                        fun () ->
                            result {
                                let! compensatingStream =
                                    undoers
                                    |>> fun x -> x
                                    |> List.traverseOptionM (fun x -> x)
                                    |> Option.toResultWith (sprintf "compensatingStream %s" 'A.StorageName)
                                let! extractedCompensator =
                                    compensatingStream
                                    |> List.traverseResultM (fun x -> x) 
                                let! extractedCompensatorApplied =
                                    extractedCompensator
                                    |> List.traverseResultM (fun x -> x())
                                let extractedEvents =
                                    let exCompLen = extractedCompensatorApplied.Length
                                    List.zip3
                                        (aggregateIds |> List.take exCompLen)
                                        ((aggregateIds |> List.take exCompLen)
                                         |>> (eventStore.TryGetLastAggregateEventId 'A.Version 'A.StorageName))
                                        extractedCompensatorApplied
                                    |> List.map (fun (id, a, b) -> id, a |> Option.defaultValue 0, b |>> fun x -> x.Serialize)
                                let addEventsStreamA =
                                    extractedEvents
                                    |> List.traverseResultM
                                        (fun (id, eventId, events) ->
                                            let evIdQuickFix = (eventStore.TryGetLastAggregateEventId 'A.Version 'A.StorageName id |> Option.defaultValue 0)
                                            eventStore.AddAggregateEventsMd evIdQuickFix 'A.Version 'A.StorageName id md events
                                        )
                                match addEventsStreamA with
                                | Ok _ -> return ()
                                | Error e -> return! Error e
                            }
                            
                    let tryCompensations =
                        undoerRun ()
                    
                    let _ =
                        match tryCompensations with
                        | Error x -> logger.Value.LogError x
                        | Ok _ -> logger.Value.LogInformation "compensation Saga succeeded"
                    Error (sprintf "action failed needed to compensate. The compensation action had the following result %A" tryCompensations)    
                else
                    Error (sprintf "failed the precondition of applicability of the runSagaNAggregateCommands. All of these must be true: commandHasUndoers %A" commandHasUndoers)
                    
                    
    // all those Saga-like functions are convoluted and not really needed, but they are a good way to test the undoer mechanism
    [<Obsolete>]
    let inline runSagaNAggregateCommands<'A, 'E, 'F
        when 'A :> Aggregate<'F>
        and 'E :> Event<'A>
        and 'E : (member Serialize: 'F)
        and 'E : (static member Deserialize: 'F -> Result<'E, string>)
        and 'A : (static member Deserialize: 'F -> Result<'A, string>)
        and 'A : (static member SnapshotsInterval: int)
        and 'A : (static member StorageName: string)
        and 'A : (static member Version: string)
        >
        (aggregateIds: List<Guid>)
        (eventStore: IEventStore<'F>)
        (eventBroker: IEventBroker<'F>)
        (commands: List<AggregateCommand<'A, 'E>>)
        = runSagaNAggregateCommandsMd<'A, 'E, 'F> aggregateIds eventStore eventBroker Metadata.Empty commands
               
    // all those Saga-like functions are convoluted and not really needed, but they are a good way to test the undoer mechanism
    [<Obsolete>]
    let inline runSagaTwoNAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'F
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
        (eventBroker: IEventBroker<'F>)
        (md: Metadata)
        (commands1: List<AggregateCommand<'A1, 'E1>>)
        (commands2: List<AggregateCommand<'A2, 'E2>>)
        =
            logger.Value.LogDebug "runSagaTwoNAggregateCommands"
            let command1HasUndoers = commands1 |> List.forall (fun x -> x.Undoer.IsSome)
            let command2HasUndoers = commands2 |> List.forall (fun x -> x.Undoer.IsSome)
            let lengthMustBeTheSame = commands1.Length = commands2.Length
            if (command1HasUndoers && command2HasUndoers && lengthMustBeTheSame) then
                let pairOfCommands = List.zip commands1 commands2
                let pairsOfIds = List.zip aggregateIds1 aggregateIds2
                let idsWithCommands = List.zip pairsOfIds pairOfCommands
                
                let iteratedExecutionOfPairOfCommands =
                    idsWithCommands
                    |> List.fold
                        (fun acc ((id1, id2 ), (c1, c2)) ->
                            let guard = acc |> fst
                            let futureUndoers = acc |> snd
                            if not guard then (false, futureUndoers) else
                                let stateA1 = getAggregateFreshState<'A1, 'E1, 'F> id1 eventStore
                                let stateA2 = getAggregateFreshState<'A2, 'E2, 'F> id2 eventStore
                                let futureUndo1 =
                                    let aggregateStateViewerA1: AggregateViewer<'A1> = fun id -> getAggregateFreshState<'A1, 'E1, 'F> id eventStore
                                    match c1.Undoer, stateA1 with
                                    | Some undoer, Ok (_, st) -> Some (undoer st aggregateStateViewerA1)
                                    | _ -> None
                                let futureUndo2 =
                                    let aggregateStateViewerA2: AggregateViewer<'A2> = fun id -> getAggregateFreshState<'A2, 'E2, 'F> id eventStore
                                    match c2.Undoer, stateA2 with
                                    | Some undoer, Ok (_, st) -> Some (undoer st aggregateStateViewerA2)
                                    | _ -> None    
                                let undoers = [futureUndo1, futureUndo2]
                                let myRes = runTwoAggregateCommandsMd id1 id2 eventStore eventBroker md c1 c2
                                match myRes with
                                | Ok _ -> (guard, futureUndoers @ undoers)
                                | Error _ -> (false, futureUndoers)
                        )
                        (true, [])
                match iteratedExecutionOfPairOfCommands with
                | (true, _) -> Ok ()
                | (false, undoers) ->
                     let undoerRun =
                        fun () ->
                            result {
                                let! compensatingStreamA1 =
                                    undoers
                                    |>> fun (x, _) -> x
                                    |> List.traverseOptionM (fun x -> x)
                                    |> Option.toResultWith (sprintf "compensatingStreamA1 %s - %s  " 'A1.StorageName 'A2.StorageName)
                                let! compensatingStreamA2 =
                                    undoers
                                    |>> fun (_, x) -> x
                                    |> List.traverseOptionM (fun x -> x)
                                    |> Option.toResultWith (sprintf "compensatingStreamA2 %s - %s  " 'A1.StorageName 'A2.StorageName)
                                    
                                let! extractedCompensatorE1 =
                                    compensatingStreamA1
                                    |> List.traverseResultM (fun x -> x) 
                                
                                let! extractedCompensatorE1Applied =
                                    extractedCompensatorE1
                                    |> List.traverseResultM (fun x -> x())
                                    
                                let! extractedCompensatorE2 =
                                    compensatingStreamA2
                                    |> List.traverseResultM id    
                                
                                let! extractedCompenatorE2Applied =
                                    extractedCompensatorE2
                                    |> List.traverseResultM (fun x -> x())
                                 
                                let extractedEventsForE1 =
                                    let exCompLen = extractedCompensatorE1Applied.Length
                                    List.zip3
                                        (aggregateIds1 |> List.take exCompLen)
                                        ((aggregateIds1 |> List.take exCompLen)
                                         |>> (eventStore.TryGetLastAggregateEventId 'A1.Version 'A1.StorageName))
                                        extractedCompensatorE1Applied  
                                    |> List.map (fun (id, a, b) -> id, a |> Option.defaultValue 0, b |>> fun x -> x.Serialize)
                               
                                let extractedEventsForE2 =
                                    let exCompLen = extractedCompenatorE2Applied.Length
                                    List.zip3
                                        (aggregateIds2 |> List.take exCompLen)
                                        ((aggregateIds2 |> List.take exCompLen)
                                         |>> (eventStore.TryGetLastAggregateEventId 'A2.Version 'A2.StorageName))
                                        extractedCompenatorE2Applied
                                    |> List.map (fun (id, a, b) -> id, a |> Option.defaultValue 0, b |>> fun x -> x.Serialize)
                                    
                                let addEventsStreamA1 =
                                    extractedEventsForE1
                                    |> List.traverseResultM
                                        (fun (id, _, events) ->
                                            let lastEventIdQuickFix = eventStore.TryGetLastAggregateEventId 'A1.Version 'A1.StorageName id |> Option.defaultValue 0
                                            eventStore.AddAggregateEventsMd lastEventIdQuickFix 'A1.Version 'A1.StorageName id md events)
                                
                                let addEventsStreamA2 =
                                    extractedEventsForE2
                                    |> List.traverseResultM
                                        (fun (id, _, events) ->
                                            let lastEventIdQuickFix = eventStore.TryGetLastAggregateEventId 'A2.Version 'A2.StorageName id |> Option.defaultValue 0
                                            eventStore.AddAggregateEventsMd lastEventIdQuickFix 'A2.Version 'A2.StorageName id md events)
                              
                                match addEventsStreamA1, addEventsStreamA2 with
                                | Ok _, Ok _ -> return ()
                                | Error e, Ok _ -> return! Error e
                                | Ok _, Error e -> return! Error e
                                | Error e1, Error e2 -> return! Error (sprintf "%s - %s" e1 e2)
                            }
                     let lookupName = sprintf "%s_%s" 'A1.StorageName 'A2.StorageName
                     let tryCompensations =
                     
                     #if USING_MAILBOXPROCESSOR    
                        MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) undoerRun
                     #else   
                         undoerRun ()
                     #endif    
                     let _ =
                         match tryCompensations with
                         | Error x -> logger.Value.LogError x
                         | Ok _ -> logger.Value.LogInformation "compensation Saga succeeded"
                     Error (sprintf "action failed needed to compensate. The compensation action had the following result %A" tryCompensations)
                else      
                    Error (sprintf "failed the precondition of applicability of the runSagaTwoNAggregateCommands. All of these must be true: command1HasUndoers: %A, command2HasUndoers: %A, lengthsMustBeTheSame: %A  " command1HasUndoers command2HasUndoers lengthMustBeTheSame)
    
    // all those Saga-like functions are convoluted and not really needed, but they are a good way to test the undoer mechanism
    [<Obsolete>]
    let inline runSagaTwoNAggregateCommands<'A1, 'E1, 'A2, 'E2, 'F
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
        (eventBroker: IEventBroker<'F>)
        (commands1: List<AggregateCommand<'A1, 'E1>>)
        (commands2: List<AggregateCommand<'A2, 'E2>>)
        =
            logger.Value.LogDebug "runSagaTwoNAggregateCommands"
            runSagaTwoNAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'F> aggregateIds1 aggregateIds2 eventStore eventBroker Metadata.Empty commands1 commands2
    
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
        (eventBroker: IEventBroker<'F>)
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
                        |> List.traverseResultM (fun (state, cmds) -> foldCommands state cmds)
                       
                    let! newStatesAndEvents2 =
                        initialStatesAndMultiCommands2
                        |> List.traverseResultM (fun (state, cmds) -> foldCommands state cmds)
                    
                    let! newStatesAndEvents3 =
                        initialStatesAndMultiCommands3
                        |> List.traverseResultM (fun (state, cmds) -> foldCommands state cmds)
                    
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
                                AggregateCache<'A1, 'F>.Instance.Memoize2 (newStates1.[i] |> Ok) ((newDbBasedEventIds1.[i] |> List.last, aggregateIds1.[i]))
                                mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore aggregateIds1.[i] newStates1.[i] (newDbBasedEventIds1.[i] |> List.last) |> ignore
                            
                            for i in 0 .. (uniqueAggregateIds2.Length - 1) do
                                AggregateCache<'A2, 'F>.Instance.Memoize2 (newStates2.[i] |> Ok) ((newDbBasedEventIds2.[i] |> List.last, aggregateIds2.[i]))
                                mkAggregateSnapshotIfIntervalPassed2<'A2, 'E2, 'F> eventStore aggregateIds2.[i] newStates2.[i] (newDbBasedEventIds2.[i] |> List.last) |> ignore
                            
                            for i in 0 .. (uniqueAggregateIds3.Length - 1) do
                                AggregateCache<'A3, 'F>.Instance.Memoize2 (newStates3.[i] |> Ok) ((newDbBasedEventIds3.[i] |> List.last, aggregateIds3.[i]))
                                mkAggregateSnapshotIfIntervalPassed2<'A3, 'E3, 'F> eventStore aggregateIds3.[i] newStates3.[i] (newDbBasedEventIds3.[i] |> List.last) |> ignore
                    let _ =
                        if ((List.length (uniqueAggregateIds1 @ uniqueAggregateIds2 @ uniqueAggregateIds3)) = List.length (List.distinct (uniqueAggregateIds1 @ uniqueAggregateIds2 @ uniqueAggregateIds3))) then
                            doCaches () // first do caches, then invalidate where needed
                            
                    // if some aggregateIds is present in more than one parameters list then do not cache
                    // let _ =
                    //     if ((List.length (uniqueAggregateIds1 @ uniqueAggregateIds2 @ uniqueAggregateIds3)) = List.length (List.distinct (uniqueAggregateIds1 @ uniqueAggregateIds2 @ uniqueAggregateIds3)))
                            
                            // doCaches () // first do caches, then invalidate where needed
                    
                    // let allIds = aggregateIds1 @ aggregateIds2 @ aggregateIds3
                    // let duplicateIds =
                    //     allIds
                    //     |> List.groupBy id
                    //     |> List.filter (fun (_, l) -> l.Length > 1)
                    //     |> List.map (fun (id, _) -> id)
                    //
                    // let _ =
                    //     aggregateIds1 |> List.iter (fun id ->
                    //         if (duplicateIds |> List.contains id) then
                    //             AggregateCache<'A1, 'F>.Instance.Clean id
                    //     )
                    //     aggregateIds2 |> List.iter (fun id ->
                    //         if (duplicateIds |> List.contains id) then
                    //             AggregateCache<'A2, 'F>.Instance.Clean id
                    //     )
                    //     aggregateIds3 |> List.iter (fun id ->
                    //         if (duplicateIds |> List.contains id) then
                    //             AggregateCache<'A3, 'F>.Instance.Clean id
                    //     )
                    
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
        (eventBroker: IEventBroker<'F>)
        (command1: List<AggregateCommand<'A1, 'E1>>)
        (command2: List<AggregateCommand<'A2, 'E2>>)
        (command3: List<AggregateCommand<'A3, 'E3>>)
        =
            logger.Value.LogDebug "runThreeNAggregateCommands"
            forceRunThreeNAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'A3, 'E3, 'F> aggregateIds1 aggregateIds2 aggregateIds3 eventStore eventBroker Metadata.Empty command1 command2 command3

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
        (eventBroker: IEventBroker<'F>)
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
                            |>> fun (state, command) -> command.Execute state
                            |> List.traverseResultM id
                        let! events2 =
                            statesAndCommands2
                            |>> fun (state, command) -> command.Execute state
                            |> List.traverseResultM id
                        let! events3 =
                            statesAndCommands3
                            |>> fun (state, command) -> command.Execute state
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
                            |>> fun (state, command) -> (command.Execute state)
                            |> List.traverseResultM id
                        let! statesAndEvents2 =
                            statesAndCommands2
                            |>> fun (state, command) -> (command.Execute state)
                            |> List.traverseResultM id
                        let! statesAndEvents3 =
                            statesAndCommands3
                            |>> fun (state, command) -> (command.Execute state)
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
                            AggregateCache<'A1, 'F>.Instance.Memoize2 (newStates1.[i] |> Ok) ((eventIds1.[i] |> List.last, aggregateIds1.[i]))
                            mkAggregateSnapshotIfIntervalPassed2<'A1, 'E1, 'F> eventStore aggregateIds1.[i] newStates1.[i] (eventIds1.[i] |> List.last) |> ignore
                        for i in 0..(aggregateIds2.Length - 1) do
                            AggregateCache<'A2, 'F>.Instance.Memoize2 (newStates2.[i] |> Ok) ((eventIds2.[i] |> List.last, aggregateIds2.[i]))
                            mkAggregateSnapshotIfIntervalPassed2<'A2, 'E2, 'F> eventStore aggregateIds2.[i] newStates2.[i] (eventIds2.[i] |> List.last) |> ignore
                        for i in 0..(aggregateIds3.Length - 1) do
                            AggregateCache<'A3, 'F>.Instance.Memoize2 (newStates3.[i] |> Ok) ((eventIds3.[i] |> List.last, aggregateIds3.[i]))
                            mkAggregateSnapshotIfIntervalPassed2<'A3, 'E3, 'F> eventStore aggregateIds3.[i] newStates3.[i] (eventIds3.[i] |> List.last) |> ignore

                        return ()
                    }
                // using the aggregateIds to determine the name of the mailboxprocessor can be overkill: revise this ASAP
                // let aggregateIds = aggregateIds1 @ aggregateIds2 @ aggregateIds3 |> List.map (fun x -> x.ToString()) |> List.sort |> String.concat "_"
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
        (eventBroker: IEventBroker<'F>)
        (command1: List<AggregateCommand<'A1, 'E1>>)
        (command2: List<AggregateCommand<'A2, 'E2>>)
        (command3: List<AggregateCommand<'A3, 'E3>>)
        =
            logger.Value.LogDebug "runThreeNAggregateCommands"
            runThreeNAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'A3, 'E3, 'F> aggregateIds1 aggregateIds2 aggregateIds3 eventStore eventBroker Metadata.Empty command1 command2 command3
            
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
        (eventBroker: IEventBroker<'F>)
        (md: Metadata)
        (command1: AggregateCommand<'A1, 'E1>)
        (command2: AggregateCommand<'A2, 'E2>)
        (command3: AggregateCommand<'A3, 'E3>)
        =
            logger.Value.LogDebug "runThreeAggregateCommands"
            let commands = fun () ->
                result {
                    let! (id1, state1) = getAggregateFreshState<'A1, 'E1, 'F> aggregateId1 eventStore
                    let! (id2, state2) = getAggregateFreshState<'A2, 'E2, 'F> aggregateId2 eventStore
                    let! (id3, state3) = getAggregateFreshState<'A3, 'E3, 'F> aggregateId3 eventStore

                    let! (newState1, events1) =
                        state1
                        |> command1.Execute
                    let! (newState2, events2) =
                        state2
                        |> command2.Execute
                    let! (newState3, events3) =
                        state3
                        |> command3.Execute

                    let events1' =
                        events1
                        |>> fun x -> x.Serialize
                    let events2' =
                        events2
                        |>> fun x -> x.Serialize
                    let events3' =
                        events3
                        |>> fun x -> x.Serialize

                    let! idLists =
                        eventStore.MultiAddAggregateEventsMd
                            md    
                            [
                                (id1, events1', 'A1.Version, 'A1.StorageName, aggregateId1)
                                (id2, events2', 'A2.Version, 'A2.StorageName, aggregateId2)
                                (id3, events3', 'A3.Version, 'A3.StorageName, aggregateId3)
                            ]
                            
                    AggregateCache<'A1, 'F>.Instance.Memoize2 (newState1 |> Ok) (idLists.[0] |> List.last, aggregateId1)
                    AggregateCache<'A2, 'F>.Instance.Memoize2 (newState2 |> Ok) (idLists.[1] |> List.last, aggregateId2)
                    AggregateCache<'A3, 'F>.Instance.Memoize2 (newState3 |> Ok) (idLists.[2] |> List.last, aggregateId3)

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
        (eventBroker: IEventBroker<'F>)
        (command1: AggregateCommand<'A1, 'E1>)
        (command2: AggregateCommand<'A2, 'E2>)
        (command3: AggregateCommand<'A3, 'E3>)
        =
            logger.Value.LogDebug "runThreeAggregateCommands"
            runThreeAggregateCommandsMd aggregateId1 aggregateId2 aggregateId3 eventStore eventBroker Metadata.Empty command1 command2 command3
            
    // // experimental. Need to refactor and/or delete
    // [<Obsolete>]
    // let inline runSagaTwoNAggregateCommandsAndForceTwoMAggregateCommandsMd<'A11, 'E11, 'A12, 'E12, 'A21, 'E21, 'A22, 'E22, 'F
    //     when 'A11 :> Aggregate<'F>
    //     and 'E11 :> Event<'A11>
    //     and 'E11 : (member Serialize: 'F)
    //     and 'E11 : (static member Deserialize: 'F -> Result<'E11, string>)
    //     and 'A11 : (static member Deserialize: 'F -> Result<'A11, string>)
    //     and 'A11 : (static member SnapshotsInterval: int)
    //     and 'A11 : (static member StorageName: string)
    //     and 'A11 : (static member Version: string)
    //     and 'A12 :> Aggregate<'F>
    //     and 'E12 :> Event<'A12>
    //     and 'E12 : (member Serialize: 'F)
    //     and 'E12 : (static member Deserialize: 'F -> Result<'E12, string>)
    //     
    //     and 'A12 : (static member Deserialize: 'F -> Result<'A12, string>)
    //     and 'A12 : (static member SnapshotsInterval: int)
    //     and 'A12 : (static member StorageName: string)
    //     and 'A12 : (static member Version: string)
    //     and 'A21 :> Aggregate<'F>
    //     and 'E21 :> Event<'A21>
    //     and 'E21 : (member Serialize: 'F)
    //     and 'E21 : (static member Deserialize: 'F -> Result<'E21, string>)
    //     and 'A21 : (static member Deserialize: 'F -> Result<'A21, string>)
    //     and 'A21 : (static member SnapshotsInterval: int)
    //     and 'A21 : (static member StorageName: string)
    //     and 'A21 : (static member Version: string)
    //     and 'A22 :> Aggregate<'F>
    //     and 'E22 :> Event<'A22>
    //     and 'E22 : (member Serialize: 'F)
    //     and 'E22 : (static member Deserialize: 'F -> Result<'E22, string>)
    //     and 'A22 : (static member Deserialize: 'F -> Result<'A22, string>)
    //     and 'A22 : (static member SnapshotsInterval: int)
    //     and 'A22 : (static member StorageName: string)
    //     and 'A22 : (static member Version: string)
    //     >
    //     (aggregateIdsA1: List<Guid>)
    //     (aggregateIdsA2: List<Guid>)
    //     (aggregateIdsB1: List<Guid>)
    //     (aggregateIdsB2: List<Guid>)
    //     (eventStore: IEventStore<'F>)
    //     (eventBroker: IEventBroker<'F>)
    //     (md: Metadata)
    //     (commandA11: List<AggregateCommand<'A11, 'E11>>)
    //     (commandA12: List<AggregateCommand<'A12, 'E12>>)
    //     (commandA21: List<AggregateCommand<'A21, 'E21>>)
    //     (commandA22: List<AggregateCommand<'A22, 'E22>>)
    //     : Result<unit, string>
    //     =
    //         logger.Value.LogDebug "runSagaTwoNAggregateCommandsAndForceTwoMAggregateCommands"
    //         let runUndoers (undoers: (Result<(unit -> Result<List<'E11>,string>),string> option * Result<(unit -> Result<List<'E12>,string>),string> option) list) =
    //             let undoerRun =
    //                 fun () ->
    //                     // todo: refactor as this is duplicated
    //                     result {
    //                         let! compensatingStreamA1 =
    //                             undoers
    //                             |>> fun (x, _) -> x
    //                             |> List.traverseOptionM (fun x -> x)
    //                             |> Option.toResultWith (sprintf "compensatingStreamA1 %s - %s  " 'A11.StorageName 'A12.Version)
    //                         let! compensatingStreamA2 =
    //                             undoers
    //                             |>> fun (_, x) -> x
    //                             |> List.traverseOptionM (fun x -> x)
    //                             |> Option.toResultWith (sprintf "compensatingStreamA2 %s - %s  " 'A12.StorageName 'A12.StorageName)
    //                             
    //                         let! extractedCompensatorE1 =
    //                             compensatingStreamA1
    //                             |> List.traverseResultM (fun x -> x) 
    //                         
    //                         let! extractedCompensatorE1Applied =
    //                             extractedCompensatorE1
    //                             |> List.traverseResultM (fun x -> x())
    //                             
    //                         let! extractedCompensatorE2 =
    //                             compensatingStreamA2
    //                             |> List.traverseResultM id    
    //                         
    //                         let! extractedCompenatorE2Applied =
    //                             extractedCompensatorE2
    //                             |> List.traverseResultM (fun x -> x())
    //                         
    //                         let extractedEventsForE1 =
    //                             let exCompLen = extractedCompensatorE1Applied.Length
    //                             List.zip3
    //                                 (aggregateIdsA1 |> List.take exCompLen)
    //                                 ((aggregateIdsA1 |> List.take exCompLen)
    //                                  |>> (eventStore.TryGetLastAggregateEventId 'A11.Version 'A12.StorageName))
    //                                 extractedCompensatorE1Applied  
    //                             |> List.map (fun (id, a, b) -> id, a |> Option.defaultValue 0, b |>> fun x -> x.Serialize)
    //                         
    //                         let extractedEventsForE2 =
    //                             let exCompLen = extractedCompenatorE2Applied.Length
    //                             List.zip3
    //                                 (aggregateIdsA2 |> List.take exCompLen)
    //                                 ((aggregateIdsA2 |> List.take exCompLen)
    //                                  |>> (eventStore.TryGetLastAggregateEventId 'A12.Version 'A12.StorageName))
    //                                 extractedCompenatorE2Applied
    //                             |> List.map (fun (id, a, b) -> id, a |> Option.defaultValue 0, b |>> fun x -> x.Serialize)
    //                             
    //                         let addEventsStreamA1 =
    //                             extractedEventsForE1
    //                             |> List.traverseResultM
    //                                 (fun (id, _, events) ->
    //                                     let quickFixLastEventId = eventStore.TryGetLastAggregateEventId 'A11.Version 'A12.StorageName id |> Option.defaultValue 0
    //                                     eventStore.AddAggregateEventsMd quickFixLastEventId 'A11.Version 'A12.StorageName id md events)
    //                         
    //                         let addEventsStreamA2 =
    //                             extractedEventsForE2
    //                             |> List.traverseResultM
    //                                 (fun (id, _, events) ->
    //                                     let quickFixLastEventId = eventStore.TryGetLastAggregateEventId 'A12.Version 'A12.StorageName id |> Option.defaultValue 0
    //                                     eventStore.AddAggregateEventsMd quickFixLastEventId 'A12.Version 'A12.StorageName id md events)
    //                       
    //                         match addEventsStreamA1, addEventsStreamA2 with
    //                         | Ok _, Ok _ -> return ()
    //                         | Error e, Ok _ -> return! Error e
    //                         | Ok _, Error e -> return! Error e
    //                         | Error e1, Error e2 -> return! Error (sprintf "%s - %s" e1 e2)
    //                     }
    //             let lookupName = sprintf "%s_%s" 'A11.StorageName 'A12.StorageName
    //             let tryCompensations =
    //             #if USING_MAILBOXPROCESSOR     
    //                  MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) undoerRun
    //             #else     
    //                 undoerRun ()
    //             #endif    
    //             
    //             let _ =
    //                  match tryCompensations with
    //                  | Error x -> logger.Value.LogError x
    //                  | Ok _ -> logger.Value.LogInformation "compensations has been succesfull"
    //             Error (sprintf "action failed needed to compensate. The compensation action had the following result %A" tryCompensations)
    //         
    //         let commandA11HasUndoers = commandA11 |> List.forall (fun x -> x.Undoer.IsSome)
    //         let commandA12HasUndoers = commandA12 |> List.forall (fun x -> x.Undoer.IsSome)
    //         if (commandA11HasUndoers && commandA12HasUndoers) then
    //             let pairOfCommands = List.zip commandA11 commandA12
    //             let pairsOfIds = List.zip aggregateIdsA1 aggregateIdsA2
    //             let idsWithCommands = List.zip pairsOfIds pairOfCommands
    //             let iteratedExecutionOfPairOfCommands =
    //                 idsWithCommands
    //                 |> List.fold
    //                     (fun acc ((id1, id2), (c1, c2)) ->
    //                         let guard = acc |> fst
    //                         let futureUndoers = acc |> snd
    //                         if not guard then (false, futureUndoers) else
    //                             let stateA1 = getAggregateFreshState<'A11, 'E11, 'F> id1 eventStore
    //                             let stateA2 = getAggregateFreshState<'A12, 'E12, 'F> id2 eventStore
    //                             let futureUndo1 =
    //                                 let aggregateStateViewerA1: AggregateViewer<'A11> = fun id -> getAggregateFreshState<'A11, 'E11, 'F> id eventStore
    //                                 match c1.Undoer, stateA1 with
    //                                 | Some undoer, Ok (_, st) -> Some (undoer st aggregateStateViewerA1)
    //                                 | _ -> None
    //                             let futureUndo2 =
    //                                 let aggregateStateViewerA2: AggregateViewer<'A12> = fun id -> getAggregateFreshState<'A12, 'E12, 'F> id eventStore
    //                                 match c2.Undoer, stateA2 with
    //                                 | Some undoer, Ok (_, st) -> Some (undoer st aggregateStateViewerA2)
    //                                 | _ -> None    
    //                             let undoers = [futureUndo1, futureUndo2]
    //                             let myRes = runTwoAggregateCommands id1 id2 eventStore eventBroker c1 c2
    //                             match myRes with
    //                             | Ok _ -> (guard, futureUndoers @ undoers)
    //                             | Error _ -> (false, futureUndoers)
    //                     )
    //                     (true, [])
    //             match iteratedExecutionOfPairOfCommands with
    //             | (false, undoers) ->
    //                  runUndoers undoers
    //             | true, undoers ->
    //                 let runMWithNoSaga =
    //                     forceRunTwoNAggregateCommands<'A21, 'E21, 'A22, 'E22, 'F> aggregateIdsB1 aggregateIdsB2 eventStore eventBroker commandA21 commandA22
    //                 match runMWithNoSaga with
    //                 | Ok _ -> Ok ()
    //                 | Error e ->
    //                     runUndoers undoers
    //         else
    //             Error (sprintf "error precondition not satisfied, one of those commands miss undoers %A %A\n" commandA11 commandA12)
   
    // will refactor or delete
    // [<Obsolete>]
    // let inline runSagaTwoNAggregateCommandsAndForceTwoMAggregateCommands<'A11, 'E11, 'A12, 'E12, 'A21, 'E21, 'A22, 'E22, 'F
    //     when 'A11 :> Aggregate<'F>
    //     and 'E11 :> Event<'A11>
    //     and 'E11 : (member Serialize: 'F)
    //     and 'E11 : (static member Deserialize: 'F -> Result<'E11, string>)
    //     and 'A11 : (static member Deserialize: 'F -> Result<'A11, string>)
    //     and 'A11 : (static member SnapshotsInterval: int)
    //     and 'A11 : (static member StorageName: string)
    //     and 'A11 : (static member Version: string)
    //     and 'A12 :> Aggregate<'F>
    //     and 'E12 :> Event<'A12>
    //     and 'E12 : (member Serialize: 'F)
    //     and 'E12 : (static member Deserialize: 'F -> Result<'E12, string>)
    //     
    //     and 'A12 : (static member Deserialize: 'F -> Result<'A12, string>)
    //     and 'A12 : (static member SnapshotsInterval: int)
    //     and 'A12 : (static member StorageName: string)
    //     and 'A12 : (static member Version: string)
    //     and 'A21 :> Aggregate<'F>
    //     and 'E21 :> Event<'A21>
    //     and 'E21 : (member Serialize: 'F)
    //     and 'E21 : (static member Deserialize: 'F -> Result<'E21, string>)
    //     and 'A21 : (static member Deserialize: 'F -> Result<'A21, string>)
    //     and 'A21 : (static member SnapshotsInterval: int)
    //     and 'A21 : (static member StorageName: string)
    //     and 'A21 : (static member Version: string)
    //     and 'A22 :> Aggregate<'F>
    //     and 'E22 :> Event<'A22>
    //     and 'E22 : (member Serialize: 'F)
    //     and 'E22 : (static member Deserialize: 'F -> Result<'E22, string>)
    //     and 'A22 : (static member Deserialize: 'F -> Result<'A22, string>)
    //     and 'A22 : (static member SnapshotsInterval: int)
    //     and 'A22 : (static member StorageName: string)
    //     and 'A22 : (static member Version: string)
    //     >
    //     (aggregateIdsA1: List<Guid>)
    //     (aggregateIdsA2: List<Guid>)
    //     (aggregateIdsB1: List<Guid>)
    //     (aggregateIdsB2: List<Guid>)
    //     (eventStore: IEventStore<'F>)
    //     (eventBroker: IEventBroker<'F>)
    //     (commandA11: List<AggregateCommand<'A11, 'E11>>)
    //     (commandA12: List<AggregateCommand<'A12, 'E12>>)
    //     (commandA21: List<AggregateCommand<'A21, 'E21>>)
    //     (commandA22: List<AggregateCommand<'A22, 'E22>>)
    //     : Result<unit, string>
    //     =
    //         logger.Value.LogDebug "runSagaTwoNAggregateCommandsAndForceTwoMAggregateCommands"
    //         runSagaTwoNAggregateCommandsAndForceTwoMAggregateCommandsMd aggregateIdsA1 aggregateIdsA2 aggregateIdsB1 aggregateIdsB2 eventStore eventBroker Metadata.Empty commandA11 commandA12 commandA21 commandA22

    // to be refactored or dismissed as the "forceRun" version will do the same job
    [<Obsolete>]
    let inline runSagaThreeNAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'A3, 'E3, 'F
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
        (eventBroker: IEventBroker<'F>)
        (md: Metadata)
        (command1: List<AggregateCommand<'A1, 'E1>>)
        (command2: List<AggregateCommand<'A2, 'E2>>)
        (command3: List<AggregateCommand<'A3, 'E3>>)
        =
            logger.Value.LogDebug "runSagaThreeNAggregateCommands"
            let command1HasUndoers = command1 |> List.forall (fun x -> x.Undoer.IsSome)
            let command2HasUndoers = command2 |> List.forall (fun x -> x.Undoer.IsSome)
            let command3HasUndoers = command3 |> List.forall (fun x -> x.Undoer.IsSome)
            
            let lengthsMustBeTheSame =
                    aggregateIds1.Length = aggregateIds2.Length &&
                    aggregateIds2.Length = aggregateIds3.Length &&
                    aggregateIds3.Length = command1.Length &&
                    command2.Length = command3.Length &&
                    aggregateIds1.Length >0
           
            if (command1HasUndoers && command2HasUndoers && command3HasUndoers && lengthsMustBeTheSame) then
                
                let tripletsOfCommands = List.zip3 command1 command2 command3
                let tripletsOfIds = List.zip3 aggregateIds1 aggregateIds2 aggregateIds3
                let idsWithCommands = List.zip tripletsOfIds tripletsOfCommands
              
                let iteratedExecutionOfTripletsOfCommands =
                    idsWithCommands
                    |> List.fold
                        (fun acc ((id1, id2, id3), (c1, c2, c3)) ->
                            let guard = acc |> fst
                            let futureUndoers = acc |> snd
                            if not guard then (false, futureUndoers) else
                                let stateA1 = getAggregateFreshState<'A1, 'E1, 'F> id1 eventStore
                                let stateA2 = getAggregateFreshState<'A2, 'E2, 'F> id2 eventStore
                                let stateA3 = getAggregateFreshState<'A3, 'E3, 'F> id3 eventStore
                                let futureUndo1 =
                                    let aggregateStateViewerA1: AggregateViewer<'A1> = fun id -> getAggregateFreshState<'A1, 'E1, 'F> id eventStore
                                    match c1.Undoer, stateA1 with
                                    | Some undoer, Ok (_, st) -> Some (undoer st aggregateStateViewerA1)
                                    | _ -> None
                                let futureUndo2 =
                                    let aggregateStateViewerA2: AggregateViewer<'A2> = fun id -> getAggregateFreshState<'A2, 'E2, 'F> id eventStore
                                    match c2.Undoer, stateA2 with
                                    | Some undoer, Ok (_, st) -> Some (undoer st aggregateStateViewerA2)
                                    | _ -> None
                                let futureUndo3 =
                                    let aggregateStateViewerA3: AggregateViewer<'A3> = fun id -> getAggregateFreshState<'A3, 'E3, 'F> id eventStore
                                    match c3.Undoer, stateA3 with
                                    | Some undoer, Ok (_, st) -> Some (undoer st aggregateStateViewerA3)
                                    | _ -> None 
                                let undoers = [futureUndo1, futureUndo2, futureUndo3]
                                let myRes = runThreeAggregateCommandsMd id1 id2 id3 eventStore eventBroker md c1 c2 c3
                                match myRes with
                                | Ok _ -> (guard, futureUndoers @ undoers)
                                | Error _ -> (false, futureUndoers)
                        )
                        (true, [])
                match iteratedExecutionOfTripletsOfCommands with
                | (true, _) -> Ok () // here it means we have been able to run all the triplets of command with no error
                | (false, undoers) -> // here it means that somehow we stopped somewhere, so we need to compensate with the accumulated undoers
                    let undoerRun =
                        fun () ->
                            result {
                                // removed a potentially dangerous Option.get.
                                // if there is room to simplify... will do it later

                                let! compensatingStreamA1' =
                                    undoers
                                    |>> fun (x, _, _) -> x
                                    |> List.traverseOptionM (fun x -> x)
                                    |> Option.toResultWith (sprintf "compensatingStreamA1 - %s - %s" 'A1.Version 'A1.StorageName)
                                     
                                let! compensatingStreamA2' =
                                    undoers
                                    |>> fun (_, x, _) -> x
                                    |> List.traverseOptionM (fun x -> x)
                                    |> Option.toResultWith (sprintf "compensatingStreamA2 %s - %s" 'A2.Version 'A2.StorageName)
                                    
                                let! compensatingStreamA3' =
                                    undoers
                                    |>> fun (_, _, x) -> x
                                    |> List.traverseOptionM (fun x -> x)
                                    |> Option.toResultWith (sprintf "compensatingStreamA3 %s - %s" 'A3.Version 'A3.StorageName)
                                
                                let! extractedCompensatorE1 =
                                    compensatingStreamA1'
                                    |> List.traverseResultM (fun x -> x)
                                    
                                let! extractedCompensatorE1Applied =
                                    extractedCompensatorE1
                                    |> List.traverseResultM (fun x -> x ())
                                    
                                let! extractedCompensatorE2 =
                                    compensatingStreamA2'
                                    |> List.traverseResultM (fun x -> x)
                                    
                                let! extractedCompensatorE2Applied =
                                    extractedCompensatorE2
                                    |> List.traverseResultM (fun x -> x ())
                                    
                                let! extractedCompensatorE3 =
                                    compensatingStreamA3'
                                    |> List.traverseResultM (fun x -> x)
                                    
                                let! extractedCompensatorE3Applied =
                                    extractedCompensatorE3
                                    |> List.traverseResultM (fun x -> x ())    
                                
                                let extractedEventsForE1 =
                                    let exCompLen = extractedCompensatorE1.Length
                                    List.zip3
                                        (aggregateIds1 |> List.take exCompLen)
                                        ((aggregateIds1 |> List.take exCompLen)
                                        |>> (eventStore.TryGetLastAggregateEventId 'A1.Version 'A1.StorageName))
                                        extractedCompensatorE1Applied
                                    |> List.map (fun (id, a, b) -> id, a |> Option.defaultValue 0, b |>> fun x -> x.Serialize)
                                    
                                let extractedEventsForE2 =
                                    let exCompLen = extractedCompensatorE2.Length
                                    List.zip3
                                        (aggregateIds2 |> List.take exCompLen)
                                        ((aggregateIds2 |> List.take exCompLen)
                                         |>> (eventStore.TryGetLastAggregateEventId 'A2.Version 'A2.StorageName))
                                        extractedCompensatorE2Applied
                                    |> List.map (fun (id, a, b) -> id, a |> Option.defaultValue 0, b |>> fun x -> x.Serialize)
                                     
                                let extractedEventsForE3 =
                                    let exCompLen = extractedCompensatorE3.Length
                                    List.zip3
                                        (aggregateIds3 |> List.take exCompLen)
                                        ((aggregateIds3 |> List.take exCompLen)
                                         |>> (eventStore.TryGetLastAggregateEventId 'A3.Version 'A3.StorageName))
                                        extractedCompensatorE3Applied
                                    |> List.map (fun (id, a, b) -> id, a |> Option.defaultValue 0, b |>> fun x -> x.Serialize)
                                 
                                let addEventsStreamA1 =
                                    extractedEventsForE1
                                    |> List.traverseResultM (fun (id, _, ev) ->
                                            let quickFixLastEventId = eventStore.TryGetLastAggregateEventId 'A1.Version 'A1.StorageName id |> Option.defaultValue 0
                                            eventStore.AddAggregateEventsMd quickFixLastEventId 'A1.Version 'A1.StorageName id md ev)
                                
                                let addEventsStreamA2 =
                                    extractedEventsForE2
                                    |> List.traverseResultM (fun (id, _, ev) ->
                                            let quickFixLastEventId = eventStore.TryGetLastAggregateEventId 'A2.Version 'A2.StorageName id |> Option.defaultValue 0
                                            eventStore.AddAggregateEventsMd quickFixLastEventId 'A2.Version 'A2.StorageName id md ev)
                               
                                let addEventsStreamA3 =
                                    extractedEventsForE3
                                    |> List.traverseResultM (fun (id, _, ev) ->
                                            let quickFixLastEventId = eventStore.TryGetLastAggregateEventId 'A3.Version 'A3.StorageName id |> Option.defaultValue 0
                                            eventStore.AddAggregateEventsMd quickFixLastEventId 'A3.Version 'A3.StorageName id md ev)
                             
                                // todo: recap. for uniformity and precautions may want to preprocess the events before adding them to the eventstore
                                // put result in the cache and should also
                                // notify the eventbroker (if everything is ok). not urgent because
                                // eventbroker is usually disabled and it is not likely that the compensation
                                // will fail. (in writing the undo you take care of the fact that you should
                                // succeed in reversing the command)
                                 
                                match addEventsStreamA1, addEventsStreamA2, addEventsStreamA3 with
                                | Ok _, Ok _, Ok _ -> return ()
                                | Error x, Ok   _, Ok _ -> return! Error x
                                | Ok _, Error x, Ok _ -> return! Error x
                                | Ok _, Ok _, Error x -> return! Error x
                                | Error x, Error y, Ok _ -> return! Error (x + " - " + y)
                                | Error x, Ok _, Error z -> return! Error (x + " - " + z)
                                | Ok _, Error y, Error z -> return! Error (y + " -" + z)
                                | Error x, Error y, Error z -> return! Error (x + " - " + y + " -" + z)
                            }
                    
                    let lookupName = sprintf "%s_%s_%s" 'A1.StorageName 'A2.StorageName 'A3.StorageName
                    let tryCompensations =
                    #if USING_MAILBOXPROCESSOR    
                        MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) undoerRun
                    #else
                        undoerRun ()
                    #endif    
                    let _ =
                        match tryCompensations with
                        | Error x -> logger.Value.LogError x
                        | Ok _ -> logger.Value.LogInformation "compensation has been succesful" 
                    Error (sprintf "action failed needed to compensate. The compensation action had the following result %A" tryCompensations)    
                else                 
                    Error (sprintf "falied the precondition of applicability of the runSagaThreeNAggregateCommands. All of these must be true: command1HasUndoers: %A, command2HasUndoers: %A, command3HasUndoers: %A, lengthsMustBeTheSame: %A  " command1HasUndoers command2HasUndoers command3HasUndoers lengthsMustBeTheSame)

    // to be dismissed as we can use the "forceRun" version
    // [<Obsolete>]
    // let inline runSagaThreeNAggregateCommands<'A1, 'E1, 'A2, 'E2, 'A3, 'E3, 'F
    //     when 'A1 :> Aggregate<'F>
    //     and 'E1 :> Event<'A1>
    //     and 'E1 : (member Serialize: 'F)
    //     and 'E1 : (static member Deserialize: 'F -> Result<'E1, string>)
    //     and 'A1 : (static member Deserialize: 'F -> Result<'A1, string>)
    //     and 'A1 : (static member SnapshotsInterval: int)
    //     and 'A1 : (static member StorageName: string)
    //     and 'A1 : (static member Version: string)
    //     and 'A2 :> Aggregate<'F>
    //     and 'E2 :> Event<'A2>
    //     and 'E2 : (member Serialize: 'F)
    //     and 'E2 : (static member Deserialize: 'F -> Result<'E2, string>)
    //     and 'A2 : (static member Deserialize: 'F -> Result<'A2, string>)
    //     and 'A2 : (static member SnapshotsInterval: int)
    //     and 'A2 : (static member StorageName: string)
    //     and 'A2 : (static member Version: string)
    //     and 'A3 :> Aggregate<'F>
    //     and 'E3 :> Event<'A3>
    //     and 'E3 : (member Serialize: 'F)
    //     and 'E3 : (static member Deserialize: 'F -> Result<'E3, string>)
    //     and 'A3 : (static member Deserialize: 'F -> Result<'A3, string>)
    //     and 'A3 : (static member SnapshotsInterval: int)
    //     and 'A3 : (static member StorageName: string)
    //     and 'A3 : (static member Version: string)
    //     >
    //     (aggregateIds1: List<Guid>)
    //     (aggregateIds2: List<Guid>)
    //     (aggregateIds3: List<Guid>)
    //     (eventStore: IEventStore<'F>)
    //     (eventBroker: IEventBroker<'F>)
    //     (command1: List<AggregateCommand<'A1, 'E1>>)
    //     (command2: List<AggregateCommand<'A2, 'E2>>)
    //     (command3: List<AggregateCommand<'A3, 'E3>>)
    //     =
    //         logger.Value.LogDebug "runSagaThreeNAggregateCommands"
    //         runSagaThreeNAggregateCommandsMd<'A1, 'E1, 'A2, 'E2, 'A3, 'E3, 'F> aggregateIds1 aggregateIds2 aggregateIds3 eventStore eventBroker Metadata.Empty command1 command2 command3

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

                    let! (eventId1, state1) = getFreshState<'A1, 'E1, 'F> eventStore
                    let! (eventId2, state2) = getFreshState<'A2, 'E2, 'F> eventStore

                    let! (newState1, events1) =
                        state1
                        |> command1.Execute
                    let! (newState2, events2) =
                        state2
                        |> command2.Execute

                    let events1' =
                        events1
                        |>> fun x -> x.Serialize
                    let events2' =
                        events2
                        |>> fun x -> x.Serialize

                    let! idLists =
                        eventStore.MultiAddEventsMd
                            md
                            [
                                (eventId1, events1', 'A1.Version, 'A1.StorageName)
                                (eventId2, events2', 'A2.Version, 'A2.StorageName)
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

                    let! (eventId1, state1) = getFreshState<'A1, 'E1, 'F> storage
                    let! (eventId2, state2) = getFreshState<'A2, 'E2, 'F> storage
                    let! (eventId3, state3) = getFreshState<'A3, 'E3, 'F> storage

                    let! (newState1, events1) =
                        state1
                        |> command1.Execute
                    let! (newState2, events2) =
                        state2
                        |> command2.Execute
                    let! (newState3, events3) =
                        state3
                        |> command3.Execute

                    let events1' =
                        events1
                        |>> fun x -> x.Serialize
                    let events2' =
                        events2
                        |>> fun x -> x.Serialize
                    let events3' =
                        events3
                        |>> fun x -> x.Serialize

                    let! idLists =
                        storage.MultiAddEventsMd
                            md  
                            [
                                (eventId1, events1', 'A1.Version, 'A1.StorageName)
                                (eventId2, events2', 'A2.Version, 'A2.StorageName)
                                (eventId3, events3', 'A3.Version, 'A3.StorageName)
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
                let _ = AggregateCache<'A, 'F>.Instance.Memoize2 (emptyGDPRState |> Ok) (lastAggregateEventId, aggregateId)
                printf "returning from GDPRResetSnapshotsAndEventsOfAnAggregate %s - %s" 'A.StorageName 'A.Version
                return ()
            }
        let lookupName = sprintf "%s" 'A.StorageName
    #if USING_MAILBOXPROCESSOR
        let processor = MailBoxProcessors.Processors.Instance.GetProcessor lookupName
        MailBoxProcessors.postToTheProcessor processor reset
    #else    
        reset ()
    #endif    
