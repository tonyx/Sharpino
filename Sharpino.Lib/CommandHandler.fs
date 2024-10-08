
namespace Sharpino

open System

open FSharp.Core
open FSharpPlus

open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions
open Sharpino.Cache
open Sharpino.Conf
open Sharpino.Core
open Sharpino.Storage
open Sharpino.Utils
open Sharpino.Definitions
open Sharpino.StateView
open Sharpino.KafkaBroker
open System.Runtime.CompilerServices

open FsToolkit.ErrorHandling
open log4net

open log4net.Config

module CommandHandler =
    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)

    let host = Host.CreateApplicationBuilder().Build()
    
    let factory: ILoggerFactory = LoggerFactory.Create(fun builder -> builder.AddConsole() |> ignore)
    
    type UnitResult = ((unit -> unit) * AsyncReplyChannel<unit>)
   
    let logger: ILogger ref = ref NullLogger.Instance
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
            log.Error (sprintf "appSettings.json file not found using defult!!! %A\n" ex)
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
                        ResultCE.result
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
                        ResultCE.result
                            {
                                let! (eventId, state) = stateViewer aggregateId 
                                let serState = state.Serialize 
                                let result = storage.SetAggregateSnapshot 'A.Version (aggregateId, eventId, serState) 'A.StorageName
                                return! result 
                            }
                }, Commons.generalAsyncTimeOut)
     
    let inline mkSnapshotIfIntervalPassed<'A, 'E, 'F
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
        (storage: IEventStore<'F>) =
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
                                        mkSnapshot<'A, 'E, 'F> storage
                                    else
                                        () |> Ok
                            }
                }, Commons.generalAsyncTimeOut)    
            
    let inline mkAggregateSnapshotIfIntervalPassed<'A, 'E, 'F
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
        (aggregateId: AggregateId) =
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
                                        mkAggregateSnapshot<'A, 'E, 'F > storage aggregateId 
                                    else
                                        () |> Ok
                                return! result
                            }
                }, Commons.generalAsyncTimeOut)

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
        (storage: IEventStore<'F>) 
        (eventBroker: IEventBroker<'F>) 
        (command: Command<'A, 'E>) =
            logger.Value.LogDebug (sprintf "runCommand %A\n" command)
            let command = fun ()  ->
                result {
                    let! (eventId, state) = getFreshState<'A, 'E, 'F> storage
                    let! (newState, events) =
                        state
                        |> command.Execute
                        
                    let events' =
                        events
                        |>> fun x -> x.Serialize
                    let! ids =
                        events' |> storage.AddEvents eventId 'A.Version 'A.StorageName
                    
                    StateCache<'A>.Instance.Memoize2 (newState |> Ok) (ids |> List.last)

                    if (eventBroker.notify.IsSome) then
                        let f =
                            fun () ->
                                tryPublish eventBroker 'A.Version 'A.StorageName (List.zip ids events')
                                |> ignore
                        f |> postToProcessor |> ignore

                    let _ = mkSnapshotIfIntervalPassed<'A, 'E, 'F> storage
                    return ()
                }
            let processor = MailBoxProcessors.Processors.Instance.GetProcessor 'A.StorageName
            MailBoxProcessors.postToTheProcessor processor command
    
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
                        events' |> storage.SetInitialAggregateStateAndAddEvents eventId initialInstance.Id 'A1.Version 'A1.StorageName initialInstance.Serialize 'A.Version 'A.StorageName
                    
                    StateCache<'A>.Instance.Memoize2 (newState |> Ok) (ids |> List.last)

                    if (eventBroker.notify.IsSome) then
                        let f =
                            fun () ->
                                List.zip ids events'
                                |> tryPublish eventBroker 'A.Version 'A.StorageName
                                |> ignore
                        f |> postToProcessor |> ignore

                    let _ = mkSnapshotIfIntervalPassed<'A, 'E, 'F> storage
                    return ()
                }
            let processor = MailBoxProcessors.Processors.Instance.GetProcessor 'A.StorageName
            MailBoxProcessors.postToTheProcessor processor command
    
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
                        events' |> storage.SetInitialAggregateStateAndAddAggregateEvents eventId initialInstance.Id 'A2.Version 'A2.StorageName aggregateId initialInstance.Serialize 'A1.Version 'A1.StorageName

                    if (eventBroker.notify.IsSome) then
                        let f =
                            fun () ->
                                List.zip ids events'
                                |> tryPublish eventBroker 'A1.Version 'A1.StorageName
                                |> ignore
                        f |> postToProcessor |> ignore
                        
                    AggregateCache<'A1, 'F>.Instance.Memoize2 (newState |> Ok) ((ids |> List.last, aggregateId))

                    let _ = mkAggregateSnapshotIfIntervalPassed<'A1, 'E1, 'F> storage
                    return ()
                }
            let processor = MailBoxProcessors.Processors.Instance.GetProcessor 'A1.StorageName
            MailBoxProcessors.postToTheProcessor processor command

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
                        eventStore.SetInitialAggregateStateAndMultiAddAggregateEvents initialInstance.Id 'A3.Version 'A3.StorageName initialInstance.Serialize multiEvents
                    
                    // for the future
                    if (eventBroker.notifyAggregate.IsSome) then
                        let f =
                            fun () ->
                                tryPublishAggregateEvent eventBroker aggregateId1 'A1.Version 'A1.StorageName (List.zip (ids.[0]) events1')
                                |> ignore
                                tryPublishAggregateEvent eventBroker aggregateId2 'A2.Version 'A2.StorageName (List.zip (ids.[1]) events2')
                                |> ignore
                        f |> postToProcessor |> ignore
                    
                    AggregateCache<'A1, 'F>.Instance.Memoize2 (newState1 |> Ok) ((ids.[0] |> List.last, aggregateId1))
                    AggregateCache<'A2, 'F>.Instance.Memoize2 (newState2 |> Ok) ((ids.[1] |> List.last, aggregateId2))
                     
                    let _ = mkAggregateSnapshotIfIntervalPassed<'A1, 'E1, 'F> eventStore aggregateId1
                    let _ = mkAggregateSnapshotIfIntervalPassed<'A2, 'E2, 'F> eventStore aggregateId2
                    return ()
                }
            let lookupName = sprintf "%s_%s" 'A1.StorageName 'A2.StorageName
            MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) command
            
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
                    let events1' =
                        events1 
                        |>> fun x -> x.Serialize
                    let events2' =
                        events2 
                        |>> fun x -> x.Serialize
                    let events3' =
                        events3 
                        |>> fun x -> x.Serialize
                    let multiEvents =
                        [
                            (eventId1, events1', 'A1.Version, 'A1.StorageName, aggregateId1)
                            (eventId2, events2', 'A2.Version, 'A2.StorageName, aggregateId2)
                            (eventId3, events3', 'A3.Version, 'A3.StorageName, aggregateId3)
                        ]
                    let! ids =
                        eventStore.SetInitialAggregateStateAndMultiAddAggregateEvents initialInstance.Id 'A4.Version 'A4.StorageName initialInstance.Serialize multiEvents
                    
                    if (eventBroker.notifyAggregate.IsSome) then
                        let f =
                            fun () ->
                                tryPublishAggregateEvent eventBroker aggregateId1 'A1.Version 'A1.StorageName (List.zip (ids.[0]) events1')
                                |> ignore
                                tryPublishAggregateEvent eventBroker aggregateId2 'A2.Version 'A2.StorageName (List.zip (ids.[1]) events2')
                                |> ignore
                                tryPublishAggregateEvent eventBroker aggregateId3 'A3.Version 'A3.StorageName (List.zip (ids.[2]) events3')
                                |> ignore
                        f |> postToProcessor |> ignore
                    
                    AggregateCache<'A1,'F>.Instance.Memoize2 (newState1 |> Ok) ((ids.[0] |> List.last, aggregateId1))
                    AggregateCache<'A2,'F>.Instance.Memoize2 (newState2 |> Ok) ((ids.[1] |> List.last, aggregateId2))
                    AggregateCache<'A3,'F>.Instance.Memoize2 (newState3 |> Ok) ((ids.[2] |> List.last, aggregateId3))
                    let _ = mkAggregateSnapshotIfIntervalPassed<'A1, 'E1, 'F> eventStore aggregateId1
                    let _ = mkAggregateSnapshotIfIntervalPassed<'A2, 'E2, 'F> eventStore aggregateId2
                    let _ = mkAggregateSnapshotIfIntervalPassed<'A3, 'E3, 'F> eventStore aggregateId3
                    return ()
                }
            let lookupName = sprintf "%s_%s_%s" 'A1.StorageName 'A2.StorageName 'A3.StorageName
            MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) command
        
    
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
            let command = fun () ->
                result {
                    let! (eventId, state) = getAggregateFreshState<'A, 'E, 'F> aggregateId storage
                    let! (newState, events) =
                        state
                        |> command.Execute
                    let events' =
                        events 
                        |>> fun x -> x.Serialize
                    let! ids =
                        events' |> storage.AddAggregateEvents eventId 'A.Version 'A.StorageName state.Id
                   
                    AggregateCache<'A, 'F>.Instance.Memoize2 (newState |> Ok) ((ids |> List.last, state.Id))
                        
                    let publish =
                        if (eventBroker.notifyAggregate.IsSome) then
                            let f =
                                fun () ->    
                                    List.zip ids events'
                                    |> tryPublishAggregateEvent eventBroker aggregateId 'A.Version 'A.StorageName
                                    |> ignore
                            // FOCUS todo: fix the notification getting stuck 
                            f |> postToProcessor |> ignore
                            // f()
                    
                    let _ = mkAggregateSnapshotIfIntervalPassed<'A, 'E, 'F> storage aggregateId
                    return ()
                }
            let processor = MailBoxProcessors.Processors.Instance.GetProcessor (sprintf "%s_%s" 'A.StorageName (aggregateId.ToString()))
            MailBoxProcessors.postToTheProcessor processor command

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
                    let packParametersForDb =
                        List.zip3 lastEventIds serializedEvents aggregateIds
                        |>> fun (eventId, events, id) -> (eventId, events, 'A1.Version, 'A1.StorageName, id)
                    let! eventIds =
                        eventStore.MultiAddAggregateEvents packParametersForDb
                        
                    for i in 0..(aggregateIds.Length - 1) do
                        AggregateCache<'A1, 'F>.Instance.Memoize2 (newStates.[i] |> Ok) (eventIds.[i] |> List.last, aggregateIds.[i])
                        
                    let aggregateIdsWithEventIds =
                        List.zip aggregateIds eventIds
                    let kafkaParameters =
                        List.map2 (fun idList serializedEvents -> (idList, serializedEvents)) aggregateIdsWithEventIds serializedEvents
                        |>> fun (((aggId: Guid), idList), serializedEvents) -> (aggId, List.zip idList serializedEvents)
                    if (eventBroker.notifyAggregate.IsSome) then
                        kafkaParameters
                        |>> fun (id, x) -> postToProcessor (fun () -> tryPublishAggregateEvent eventBroker id 'A1.Version 'A1.StorageName x |> ignore)
                        |> ignore
                    let _ =
                        aggregateIds
                        |>> mkAggregateSnapshotIfIntervalPassed<'A1, 'E1, 'F> eventStore
                    return ()    
                }
            // using the aggregateIds to determine the name of the mailboxprocessor can be overkill: revise this ASAP
            // let aggregateIds = aggregateIds |> List.map (fun x -> x.ToString()) |> List.sort |> String.concat "_"
            // let lookupName = sprintf "%s_%s" 'A1.StorageName aggregateIds
            let lookupName = 'A1.StorageName
            let processor = MailBoxProcessors.Processors.Instance.GetProcessor lookupName
            MailBoxProcessors.postToTheProcessor processor commands
    
    
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
            let aggregateIdsAreUnique = aggregateIds |> List.distinct |> List.length = aggregateIds.Length
            if (not aggregateIdsAreUnique) then
                Error "aggregateIds are not unique"
            else
                forceRunNAggregateCommands
                    (aggregateIds: List<Guid>)
                    (eventStore: IEventStore<'F>)
                    (eventBroker: IEventBroker<'F>)
                    (commands: List<AggregateCommand<'A1, 'E1>>)
    
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
                    
                    let events1' =
                        events1 
                        |>> fun x -> x.Serialize    
                 
                    let events2' =
                        events2
                        |>> fun x -> x.Serialize
                    
                    let! idLists =
                        eventStore.MultiAddAggregateEvents
                            [
                                (eventId1, events1', 'A1.Version, 'A1.StorageName, aggregateId1)
                                (eventId2, events2', 'A2.Version, 'A2.StorageName, aggregateId2)
                            ]
                    AggregateCache<'A1, 'F>.Instance.Memoize2 (newState1 |> Ok) ((idLists.[0] |> List.last, aggregateId1))
                    AggregateCache<'A2, 'F>.Instance.Memoize2 (newState2 |> Ok) ((idLists.[1] |> List.last, aggregateId2))
                    
                    // for the future
                    if (eventBroker.notifyAggregate.IsSome) then
                        let f =
                            fun () ->
                                tryPublishAggregateEvent eventBroker aggregateId1 'A1.Version 'A1.StorageName (List.zip (idLists.[0]) events1')
                                |> ignore
                                tryPublishAggregateEvent eventBroker aggregateId2 'A2.Version 'A2.StorageName (List.zip (idLists.[1]) events2')
                                |> ignore
                        f |> postToProcessor |> ignore
                    
                    let _ = mkAggregateSnapshotIfIntervalPassed<'A1, 'E1, 'F> eventStore aggregateId1
                    let _ = mkAggregateSnapshotIfIntervalPassed<'A2, 'E2, 'F> eventStore aggregateId2
                    return ()     
                }
            let lookupName = sprintf "%s_%s" 'A1.StorageName  'A2.StorageName
            MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) commands
    
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
                                ((aggregateIds |> List.skip exCompLen)
                                 |>> (eventStore.TryGetLastAggregateEventId 'A.Version 'A.StorageName))
                                extractedCompensatorApplied
                            |> List.map (fun (id, a, b) -> id, a |> Option.defaultValue 0, b |>> fun x -> x.Serialize)
                        let addEventsStreamA =
                            extractedEvents
                            |> List.traverseResultM
                                (fun (id, eventId, events) ->
                                    eventStore.AddAggregateEvents eventId 'A.Version 'A.StorageName id events
                                )
                        match addEventsStreamA with
                        | Ok _ -> return ()
                        | Error e -> return! Error e
                    }
                
            let lookupName = sprintf "%s" 'A.StorageName
            let tryCompensations =
                MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) undoerRun
            // tryCompensations    
            let _ =
                match tryCompensations with
                | Error x -> logger.Value.LogError x
                | Ok _ -> logger.Value.LogInformation "compensation Saga succeeded"
            Error (sprintf "action failed needed to compensate. The compensation action had the following result %A" tryCompensations)    
    
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
                        |> eventStore.MultiAddAggregateEvents
                        
                    let eventIds1 = eventIds |> List.take aggregateIds1.Length
                    let eventIds2 = eventIds |> List.skip aggregateIds1.Length
                            
                    for i in 0..(aggregateIds1.Length - 1) do
                        AggregateCache<'A1, 'F>.Instance.Memoize2 (newStates1.[i] |> Ok) ((eventIds1.[i] |> List.last, aggregateIds1.[i]))
                    
                    for i in 0..(aggregateIds2.Length - 1) do
                        AggregateCache<'A2, 'F>.Instance.Memoize2 (newStates2.[i] |> Ok) ((eventIds2.[i] |> List.last, aggregateIds2.[i]))
                     
                    let aggregateIdsWithEventIds1 =
                        List.zip aggregateIds1 eventIds1
                    let aggregateIdsWithEventIds2 =
                        List.zip aggregateIds2 eventIds2
                        
                    let kafkaParmeters1 =
                        List.map2 (fun idList serializedEvents -> (idList, serializedEvents)) aggregateIdsWithEventIds1 serializedEvents1
                        |>> fun (((aggId: Guid), idList), serializedEvents) -> (aggId, List.zip idList serializedEvents)
                    let kafkaParameters2 =
                        (List.map2 (fun idList serializedEvents -> (idList, serializedEvents)) aggregateIdsWithEventIds2 serializedEvents2
                        |>> fun (((aggId: Guid), idList), serializedEvents) -> (aggId, List.zip idList serializedEvents))
                       
                    // FOCUS todo: use the doNothingBroker (no notification)
                    if (eventBroker.notifyAggregate.IsSome) then
                        kafkaParmeters1
                        |>> fun (id, x) -> postToProcessor (fun () -> tryPublishAggregateEvent eventBroker id 'A1.Version 'A1.StorageName x |> ignore)
                        |> ignore
                        kafkaParameters2
                        |>> fun (id, x) -> postToProcessor (fun () -> tryPublishAggregateEvent eventBroker id 'A2.Version 'A2.StorageName x |> ignore)
                        |> ignore
                        
                    let _ =
                        aggregateIds1
                        |>> mkAggregateSnapshotIfIntervalPassed<'A1, 'E1, 'F> eventStore
                    let _ =
                        aggregateIds2
                        |>> mkAggregateSnapshotIfIntervalPassed<'A2, 'E2, 'F> eventStore
                    return ()
                }
            // using the aggregateIds to determine the name of the mailboxprocessor can be overkill: revise this ASAP
            // let aggregateIds = aggregateIds1 @ aggregateIds2 |> List.map (fun x -> x.ToString()) |> List.sort |> String.concat "_"
            
            let lookupName = sprintf "%s_%s" 'A1.StorageName 'A2.StorageName // aggregateIds
            MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) commands
    
    // this mixture of Saga/NonSaga is actually slipping away drop to drop
    let inline runSagaNAggregatesCommandsAndForceTwoMAggregateCommands<'A, 'E, 'A1, 'E1, 'A2, 'E2, 'F
        when 'A :> Aggregate<'F>
        and 'E :> Event<'A>
        and 'E : (member Serialize: 'F)
        and 'E : (static member Deserialize: 'F -> Result<'E, string>)
        and 'A : (static member Deserialize: 'F -> Result<'A, string>)
        and 'A : (static member SnapshotsInterval: int)
        and 'A : (static member StorageName: string)
        and 'A : (static member Version: string)
        and 'A1 :> Aggregate<'F>
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
        (aggregateIds: List<Guid>)
        (aggregateIds1: List<Guid>)
        (aggregateIds2: List<Guid>)
        (eventStore: IEventStore<'F>)
        (eventBroker: IEventBroker<'F>)
        (commands: List<AggregateCommand<'A, 'E>>)
        (command1: List<AggregateCommand<'A1, 'E1>>)
        (command2: List<AggregateCommand<'A2, 'E2>>)
        : Result<unit, string>
        =
            logger.Value.LogDebug "runSagaNAggregatesCommandsAndForceTwoMAggregateCommands"
            let commandsHaveUndoers = commands |> List.forall (fun x -> x.Undoer.IsSome)
            if (commandsHaveUndoers) then 
                let idsWithCommands = List.zip aggregateIds commands
                let iteratedExecutionOfCommands =
                    idsWithCommands
                    |> List.fold
                        (fun acc (id, c) ->
                            let guard = acc |> fst
                            let futureUndoers = acc |> snd
                            if (not guard) then (false, futureUndoers) else
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
                | (false, undoers) ->
                    runSagaNAggregateCommandsUndoers<'A, 'E, 'F> aggregateIds eventStore eventBroker undoers
                | (true, undoers) ->
                    let runMWithNoSaga =
                        forceRunTwoNAggregateCommands<'A1, 'E1, 'A2, 'E2, 'F> aggregateIds1 aggregateIds2 eventStore eventBroker command1 command2
                    match runMWithNoSaga with
                    | Ok _ -> Ok ()
                    | Error e -> 
                        runSagaNAggregateCommandsUndoers<'A, 'E, 'F> aggregateIds eventStore eventBroker undoers
            else Error "no undoers"
    
     
            
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
            let aggregateId1AreUnique = aggregateIds1 |> List.distinct |> List.length = aggregateIds1.Length
            let aggregateId2AreUnique = aggregateIds2 |> List.distinct |> List.length = aggregateIds2.Length
            if (not aggregateId1AreUnique) then
                Error "aggregateIds1 are not unique"
            else if (not aggregateId2AreUnique) then
                Error "aggregateIds2 are not unique"
            else
                forceRunTwoNAggregateCommands
                    (aggregateIds1: List<Guid>)
                    (aggregateIds2: List<Guid>)
                    (eventStore: IEventStore<'F>)
                    (eventBroker: IEventBroker<'F>)
                    (command1: List<AggregateCommand<'A1, 'E1>>)
                    (command2: List<AggregateCommand<'A2, 'E2>>)

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
        =
            let commandHasUndoers = commands |> List.forall (fun x -> x.Undoer.IsSome)
            if (commandHasUndoers) then
                let idsWithCommands = List.zip aggregateIds commands
                let iteratedExecutionOfCommands =
                    idsWithCommands
                    |> List.fold
                        (fun acc (id, c) ->
                            let guard = acc |> fst
                            let futureUndoers = acc |> snd
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
                                        ((aggregateIds |> List.skip exCompLen)
                                         |>> (eventStore.TryGetLastAggregateEventId 'A.Version 'A.StorageName))
                                        extractedCompensatorApplied
                                    |> List.map (fun (id, a, b) -> id, a |> Option.defaultValue 0, b |>> fun x -> x.Serialize)
                                let addEventsStreamA =
                                    extractedEvents
                                    |> List.traverseResultM
                                        (fun (id, eventId, events) ->
                                            eventStore.AddAggregateEvents eventId 'A.Version 'A.StorageName id events
                                        )
                                match addEventsStreamA with
                                | Ok _ -> return ()
                                | Error e -> return! Error e
                            }
                    let lookupName = sprintf "%s" 'A.StorageName
                    let tryCompensations =
                        MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) undoerRun
                    
                    let _ =
                        match tryCompensations with
                        | Error x -> logger.Value.LogError x
                        | Ok _ -> logger.Value.LogInformation "compensation Saga succeeded"
                    Error (sprintf "action failed needed to compensate. The compensation action had the following result %A" tryCompensations)    
                else
                    Error (sprintf "failed the precondition of applicability of the runSagaNAggregateCommands. All of these must be true: commandHasUndoers %A" commandHasUndoers) 
    
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
                                let myRes = runTwoAggregateCommands id1 id2 eventStore eventBroker c1 c2
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
                                        ((aggregateIds1 |> List.skip exCompLen)
                                         |>> (eventStore.TryGetLastAggregateEventId 'A1.Version 'A1.StorageName))
                                        extractedCompensatorE1Applied  
                                    |> List.map (fun (id, a, b) -> id, a |> Option.defaultValue 0, b |>> fun x -> x.Serialize)
                                
                                let extractedEventsForE2 =
                                    let exCompLen = extractedCompenatorE2Applied.Length
                                    List.zip3
                                        (aggregateIds2 |> List.take exCompLen)
                                        ((aggregateIds2 |> List.skip exCompLen)
                                         |>> (eventStore.TryGetLastAggregateEventId 'A2.Version 'A2.StorageName))
                                        extractedCompenatorE2Applied
                                    |> List.map (fun (id, a, b) -> id, a |> Option.defaultValue 0, b |>> fun x -> x.Serialize)
                                    
                                let addEventsStreamA1 =
                                    extractedEventsForE1
                                    |> List.traverseResultM
                                        (fun (id, lastId, events) -> eventStore.AddAggregateEvents lastId 'A1.Version 'A1.StorageName id events)
                                
                                let addEventsStreamA2 =
                                    extractedEventsForE2
                                    |> List.traverseResultM
                                        (fun (id, lastId, events) -> eventStore.AddAggregateEvents lastId 'A2.Version 'A2.StorageName id events)
                              
                                match addEventsStreamA1, addEventsStreamA2 with
                                | Ok _, Ok _ -> return ()
                                | Error e, Ok _ -> return! Error e
                                | Ok _, Error e -> return! Error e
                                | Error e1, Error e2 -> return! Error (sprintf "%s - %s" e1 e2)
                            }
                     let lookupName = sprintf "%s_%s" 'A1.StorageName 'A2.StorageName
                     let tryCompensations =
                         MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) undoerRun
                     let _ =
                         match tryCompensations with
                         | Error x -> log.Error x
                         | Ok _ -> log.Info "compensations has been succesfull"
                     Error (sprintf "action failed needed to compensate. The compensation action had the following result %A" tryCompensations)
                else      
                    Error (sprintf "failed the precondition of applicability of the runSagaTwoNAggregateCommands. All of these must be true: command1HasUndoers: %A, command2HasUndoers: %A, lengthsMustBeTheSame: %A  " command1HasUndoers command2HasUndoers lengthMustBeTheSame)
                
    
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
                            |> eventStore.MultiAddAggregateEvents
                        let eventIds1 = eventIds |> List.take aggregateIds1.Length
                        let eventIds2 = eventIds |> List.skip aggregateIds1.Length |> List.take aggregateIds2.Length
                        let eventIds3 = eventIds |> List.skip (aggregateIds1.Length + aggregateIds2.Length)

                        for i in 0..(aggregateIds1.Length - 1) do
                            AggregateCache<'A1, 'F>.Instance.Memoize2 (newStates1.[i] |> Ok) ((eventIds1.[i] |> List.last, aggregateIds1.[i]))
                        for i in 0..(aggregateIds2.Length - 1) do
                            AggregateCache<'A2, 'F>.Instance.Memoize2 (newStates2.[i] |> Ok) ((eventIds2.[i] |> List.last, aggregateIds2.[i]))
                        for i in 0..(aggregateIds3.Length - 1) do
                            AggregateCache<'A3, 'F>.Instance.Memoize2 (newStates3.[i] |> Ok) ((eventIds3.[i] |> List.last, aggregateIds3.[i]))

                        let aggregateIdsWithEventIds1 =
                            List.zip aggregateIds1 eventIds1
                        let aggregateIdsWithEventIds2 =
                            List.zip aggregateIds2 eventIds2
                        let aggregateIdsWithEventIds3 =
                            List.zip aggregateIds3 eventIds3

                        let kafkaParmeters1 =
                            List.map2 (fun idList serializedEvents -> (idList, serializedEvents)) aggregateIdsWithEventIds1 serializedEvents1
                            |>> fun (((aggId: Guid), idList), serializedEvents) -> (aggId, List.zip idList serializedEvents)
                        let kafkaParameters2 =
                            List.map2 (fun idList serializedEvents -> (idList, serializedEvents)) aggregateIdsWithEventIds2 serializedEvents2
                            |>> fun (((aggId: Guid), idList), serializedEvents) -> (aggId, List.zip idList serializedEvents)
                        let kafkaParameters3 =
                            List.map2 (fun idList serializedEvents -> (idList, serializedEvents)) aggregateIdsWithEventIds3 serializedEvents3
                            |>> fun (((aggId: Guid), idList), serializedEvents) -> (aggId, List.zip idList serializedEvents)
                        
                        if (eventBroker.notifyAggregate.IsSome) then
                            kafkaParmeters1
                            |>> fun (id, x) -> postToProcessor (fun () -> tryPublishAggregateEvent eventBroker id 'A1.Version 'A1.StorageName x |> ignore)
                            |> ignore
                            kafkaParameters2
                            |>> fun (id, x) -> postToProcessor (fun () -> tryPublishAggregateEvent eventBroker id 'A2.Version 'A2.StorageName x |> ignore)
                            |> ignore
                            kafkaParameters3
                            |>> fun (id, x) -> postToProcessor (fun () -> tryPublishAggregateEvent eventBroker id 'A3.Version 'A3.StorageName x |> ignore)
                            |> ignore

                        let _ =
                            aggregateIds1
                            |>> mkAggregateSnapshotIfIntervalPassed<'A1, 'E1, 'F> eventStore
                        let _ =
                            aggregateIds2
                            |>> mkAggregateSnapshotIfIntervalPassed<'A2, 'E2, 'F> eventStore
                        let _ =
                            aggregateIds3
                            |>> mkAggregateSnapshotIfIntervalPassed<'A3, 'E3, 'F> eventStore
                        return ()
                    }
                // using the aggregateIds to determine the name of the mailboxprocessor can be overkill: revise this ASAP
                // let aggregateIds = aggregateIds1 @ aggregateIds2 @ aggregateIds3 |> List.map (fun x -> x.ToString()) |> List.sort |> String.concat "_"
                
                let lookupName = sprintf "%s_%s_%s" 'A1.StorageName 'A2.StorageName 'A3.StorageName // aggregateIds
                MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) commands

    // this is in progress and is meant to be used for an alternative saga based version of the previud "runNThreeAggregateCommands"
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
                        eventStore.MultiAddAggregateEvents 
                            [
                                (id1, events1', 'A1.Version, 'A1.StorageName, aggregateId1)
                                (id2, events2', 'A2.Version, 'A2.StorageName, aggregateId2)
                                (id3, events3', 'A3.Version, 'A3.StorageName, aggregateId3)
                            ]
                    AggregateCache<'A1, 'F>.Instance.Memoize2 (newState1 |> Ok) (idLists.[0] |> List.last, aggregateId1)
                    AggregateCache<'A2, 'F>.Instance.Memoize2 (newState2 |> Ok) (idLists.[1] |> List.last, aggregateId2)
                    AggregateCache<'A3, 'F>.Instance.Memoize2 (newState3 |> Ok) (idLists.[2] |> List.last, aggregateId3)

                    if (eventBroker.notifyAggregate.IsSome) then
                        let idAndEvents1 = List.zip idLists.[0] events1'
                        let idAndEvents2 = List.zip idLists.[1] events2'
                        let idAndEvents3 = List.zip idLists.[2] events3'
                        postToProcessor (fun () -> tryPublishAggregateEvent eventBroker aggregateId1 'A1.Version 'A1.StorageName idAndEvents1 |> ignore)
                        postToProcessor (fun () -> tryPublishAggregateEvent eventBroker aggregateId2 'A2.Version 'A2.StorageName idAndEvents2 |> ignore)
                        postToProcessor (fun () -> tryPublishAggregateEvent eventBroker aggregateId3 'A3.Version 'A3.StorageName idAndEvents3 |> ignore)
                        ()

                    let _ = mkAggregateSnapshotIfIntervalPassed<'A1, 'E1, 'F> eventStore aggregateId1
                    let _ = mkAggregateSnapshotIfIntervalPassed<'A2, 'E2, 'F> eventStore aggregateId2
                    let _ = mkAggregateSnapshotIfIntervalPassed<'A3, 'E3, 'F> eventStore aggregateId3
                    return ()
                }
            // using the aggregateIds to determine the name of the mailboxprocessor can be overkill: revise this ASAP
            // let aggregateIds = aggregateId1.ToString() + "_" + aggregateId2.ToString() + "_" + aggregateId3.ToString()
            
            let lookupName = sprintf "%s_%s_%s" 'A1.StorageName 'A2.StorageName 'A3.StorageName // aggregateIds
            MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) commands
   
    // experimental. Need to refactor: with full proper SAGA support those kind of solutions will be obsolete
    let inline runSagaTwoNAggregateCommandsAndForceTwoMAggregateCommands<'A11, 'E11, 'A12, 'E12, 'A21, 'E21, 'A22, 'E22, 'F
        when 'A11 :> Aggregate<'F>
        and 'E11 :> Event<'A11>
        and 'E11 : (member Serialize: 'F)
        and 'E11 : (static member Deserialize: 'F -> Result<'E11, string>)
        and 'A11 : (static member Deserialize: 'F -> Result<'A11, string>)
        and 'A11 : (static member SnapshotsInterval: int)
        and 'A11 : (static member StorageName: string)
        and 'A11 : (static member Version: string)
        and 'A12 :> Aggregate<'F>
        and 'E12 :> Event<'A12>
        and 'E12 : (member Serialize: 'F)
        and 'E12 : (static member Deserialize: 'F -> Result<'E12, string>)
        
        and 'A12 : (static member Deserialize: 'F -> Result<'A12, string>)
        and 'A12 : (static member SnapshotsInterval: int)
        and 'A12 : (static member StorageName: string)
        and 'A12 : (static member Version: string)
        and 'A21 :> Aggregate<'F>
        and 'E21 :> Event<'A21>
        and 'E21 : (member Serialize: 'F)
        and 'E21 : (static member Deserialize: 'F -> Result<'E21, string>)
        and 'A21 : (static member Deserialize: 'F -> Result<'A21, string>)
        and 'A21 : (static member SnapshotsInterval: int)
        and 'A21 : (static member StorageName: string)
        and 'A21 : (static member Version: string)
        and 'A22 :> Aggregate<'F>
        and 'E22 :> Event<'A22>
        and 'E22 : (member Serialize: 'F)
        and 'E22 : (static member Deserialize: 'F -> Result<'E22, string>)
        and 'A22 : (static member Deserialize: 'F -> Result<'A22, string>)
        and 'A22 : (static member SnapshotsInterval: int)
        and 'A22 : (static member StorageName: string)
        and 'A22 : (static member Version: string)
        >
        (aggregateIdsA1: List<Guid>)
        (aggregateIdsA2: List<Guid>)
        (aggregateIdsB1: List<Guid>)
        (aggregateIdsB2: List<Guid>)
        (eventStore: IEventStore<'F>)
        (eventBroker: IEventBroker<'F>)
        (commandA11: List<AggregateCommand<'A11, 'E11>>)
        (commandA12: List<AggregateCommand<'A12, 'E12>>)
        (commandA21: List<AggregateCommand<'A21, 'E21>>)
        (commandA22: List<AggregateCommand<'A22, 'E22>>)
        : Result<unit, string>
        =
            logger.Value.LogDebug "runSagaTwoNAggregateCommandsAndForceTwoMAggregateCommands"
            let runUndoers (undoers: (Result<(unit -> Result<List<'E11>,string>),string> option * Result<(unit -> Result<List<'E12>,string>),string> option) list) =
                let undoerRun =
                    fun () ->
                        // todo: refactor as this is duplicated
                        result {
                            let! compensatingStreamA1 =
                                undoers
                                |>> fun (x, _) -> x
                                |> List.traverseOptionM (fun x -> x)
                                |> Option.toResultWith (sprintf "compensatingStreamA1 %s - %s  " 'A11.StorageName 'A12.Version)
                            let! compensatingStreamA2 =
                                undoers
                                |>> fun (_, x) -> x
                                |> List.traverseOptionM (fun x -> x)
                                |> Option.toResultWith (sprintf "compensatingStreamA2 %s - %s  " 'A12.StorageName 'A12.StorageName)
                                
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
                                    (aggregateIdsA1 |> List.take exCompLen)
                                    ((aggregateIdsA1 |> List.skip exCompLen)
                                     |>> (eventStore.TryGetLastAggregateEventId 'A11.Version 'A12.StorageName))
                                    extractedCompensatorE1Applied  
                                |> List.map (fun (id, a, b) -> id, a |> Option.defaultValue 0, b |>> fun x -> x.Serialize)
                            
                            let extractedEventsForE2 =
                                let exCompLen = extractedCompenatorE2Applied.Length
                                List.zip3
                                    (aggregateIdsA2 |> List.take exCompLen)
                                    ((aggregateIdsA2 |> List.skip exCompLen)
                                     |>> (eventStore.TryGetLastAggregateEventId 'A12.Version 'A12.StorageName))
                                    extractedCompenatorE2Applied
                                |> List.map (fun (id, a, b) -> id, a |> Option.defaultValue 0, b |>> fun x -> x.Serialize)
                                
                            let addEventsStreamA1 =
                                extractedEventsForE1
                                |> List.traverseResultM
                                    (fun (id, lastId, events) -> eventStore.AddAggregateEvents lastId 'A11.Version 'A12.StorageName id events)
                            
                            let addEventsStreamA2 =
                                extractedEventsForE2
                                |> List.traverseResultM
                                    (fun (id, lastId, events) -> eventStore.AddAggregateEvents lastId 'A12.Version 'A12.StorageName id events)
                          
                            match addEventsStreamA1, addEventsStreamA2 with
                            | Ok _, Ok _ -> return ()
                            | Error e, Ok _ -> return! Error e
                            | Ok _, Error e -> return! Error e
                            | Error e1, Error e2 -> return! Error (sprintf "%s - %s" e1 e2)
                        }
                let lookupName = sprintf "%s_%s" 'A11.StorageName 'A12.StorageName
                let tryCompensations =
                     MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) undoerRun
                let _ =
                     match tryCompensations with
                     | Error x -> log.Error x
                     | Ok _ -> log.Info "compensations has been succesfull"
                Error (sprintf "action failed needed to compensate. The compensation action had the following result %A" tryCompensations)
            
            let commandA11HasUndoers = commandA11 |> List.forall (fun x -> x.Undoer.IsSome)
            let commandA12HasUndoers = commandA12 |> List.forall (fun x -> x.Undoer.IsSome)
            if (commandA11HasUndoers && commandA12HasUndoers) then
                let pairOfCommands = List.zip commandA11 commandA12
                let pairsOfIds = List.zip aggregateIdsA1 aggregateIdsA2
                let idsWithCommands = List.zip pairsOfIds pairOfCommands
                let iteratedExecutionOfPairOfCommands =
                    idsWithCommands
                    |> List.fold
                        (fun acc ((id1, id2), (c1, c2)) ->
                            let guard = acc |> fst
                            let futureUndoers = acc |> snd
                            if not guard then (false, futureUndoers) else
                                let stateA1 = getAggregateFreshState<'A11, 'E11, 'F> id1 eventStore
                                let stateA2 = getAggregateFreshState<'A12, 'E12, 'F> id2 eventStore
                                let futureUndo1 =
                                    let aggregateStateViewerA1: AggregateViewer<'A11> = fun id -> getAggregateFreshState<'A11, 'E11, 'F> id eventStore
                                    match c1.Undoer, stateA1 with
                                    | Some undoer, Ok (_, st) -> Some (undoer st aggregateStateViewerA1)
                                    | _ -> None
                                let futureUndo2 =
                                    let aggregateStateViewerA2: AggregateViewer<'A12> = fun id -> getAggregateFreshState<'A12, 'E12, 'F> id eventStore
                                    match c2.Undoer, stateA2 with
                                    | Some undoer, Ok (_, st) -> Some (undoer st aggregateStateViewerA2)
                                    | _ -> None    
                                let undoers = [futureUndo1, futureUndo2]
                                let myRes = runTwoAggregateCommands id1 id2 eventStore eventBroker c1 c2
                                match myRes with
                                | Ok _ -> (guard, futureUndoers @ undoers)
                                | Error _ -> (false, futureUndoers)
                        )
                        (true, [])
                match iteratedExecutionOfPairOfCommands with
                | (false, undoers) ->
                     runUndoers undoers
                | true, undoers ->
                    let runMWithNoSaga =
                        forceRunTwoNAggregateCommands<'A21, 'E21, 'A22, 'E22, 'F> aggregateIdsB1 aggregateIdsB2 eventStore eventBroker commandA21 commandA22
                    match runMWithNoSaga with
                    | Ok _ -> Ok ()
                    | Error e ->
                        runUndoers undoers
            else
                Error (sprintf "error precondition not satisfied, one of those commands miss undoers %A %A\n" commandA11 commandA12)
        
   
    // this one is based on the idea of folding the single triplets of commands accumulating the
    // undoers and applying the undoers if one triplet of the commands fails.
    // test of this are in a private application (will add public tests soon)
    // this is a very experimental function and should be used with caution
    // note: minor issues are here but will fix them
    let inline runSagaThreeNAggregateCommands<'A1, 'E1, 'A2, 'E2, 'A3, 'E3, 'F
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
                                let myRes = runThreeAggregateCommands id1 id2 id3 eventStore eventBroker c1 c2 c3
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
                             
                                // FOCUS: much better pre-process those events checking any processing error before adding them to the event store
                                // wip here (get state instead of the last aggegateeventId, and use the related id it to feed the eventstore)
                                // let! statesA1 =
                                //     aggregateIds1
                                //     |> List.traverseResultM (fun id -> getAggregateFreshState<'A1, 'E1, 'F> id eventStore)
                                // let! statesA2 =
                                //     aggregateIds2
                                //     |> List.traverseResultM (fun id -> getAggregateFreshState<'A2, 'E2, 'F> id eventStore)
                                // let! statesA3 =
                                //     aggregateIds3
                                //     |> List.traverseResultM (fun id -> getAggregateFreshState<'A3, 'E3, 'F> id eventStore)     
                                
                                 
                                let addEventsStreamA1 =
                                    extractedEventsForE1
                                    |> List.traverseResultM (fun (id, evid, ev) ->
                                            eventStore.AddAggregateEvents evid 'A1.Version 'A1.StorageName id ev)
                                
                                let addEventsStreamA2 =
                                    extractedEventsForE2
                                    |> List.traverseResultM (fun (id, evid, ev) ->
                                            eventStore.AddAggregateEvents evid 'A2.Version 'A2.StorageName id ev)
                               
                                let addEventsStreamA3 =
                                    extractedEventsForE3
                                    |> List.traverseResultM (fun (id, evid, ev) ->
                                            eventStore.AddAggregateEvents evid 'A3.Version 'A3.StorageName id ev)
                             
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
                        MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) undoerRun
                    let _ =
                        match tryCompensations with
                        | Error x -> log.Error x
                        | Ok _ -> log.Info "compensation has been succesful" 
                    Error (sprintf "action failed needed to compensate. The compensation action had the following result %A" tryCompensations)    
                else                 
                    Error (sprintf "falied the precondition of applicability of the runSagaThreeNAggregateCommands. All of these must be true: command1HasUndoers: %A, command2HasUndoers: %A, command3HasUndoers: %A, lengthsMustBeTheSame: %A  " command1HasUndoers command2HasUndoers command3HasUndoers lengthsMustBeTheSame)


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
                        eventStore.MultiAddEvents 
                            [
                                (eventId1, events1', 'A1.Version, 'A1.StorageName)
                                (eventId2, events2', 'A2.Version, 'A2.StorageName)
                            ]
                    StateCache<'A1>.Instance.Memoize2 (newState1 |> Ok) (idLists.[0] |> List.last)
                    StateCache<'A2>.Instance.Memoize2 (newState2 |> Ok) (idLists.[1] |> List.last)
                     
                    if (eventBroker.notify.IsSome) then
                        let idAndEvents1 = List.zip idLists.[0] events1'
                        let idAndEvents2 = List.zip idLists.[1] events2'
                        postToProcessor (fun () -> tryPublish eventBroker 'A1.Version 'A1.StorageName idAndEvents1 |> ignore)
                        postToProcessor (fun () -> tryPublish eventBroker 'A2.Version 'A2.StorageName idAndEvents2 |> ignore)
                        ()

                    let _ = mkSnapshotIfIntervalPassed<'A1, 'E1, 'F> eventStore
                    let _ = mkSnapshotIfIntervalPassed<'A2, 'E2, 'F> eventStore
                    return ()
                }
                    
            let lookupNames = sprintf "%s_%s" 'A1.StorageName 'A2.StorageName
            let processor = MailBoxProcessors.Processors.Instance.GetProcessor lookupNames
            MailBoxProcessors.postToTheProcessor processor commands

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
                        storage.MultiAddEvents 
                            [
                                (eventId1, events1', 'A1.Version, 'A1.StorageName)
                                (eventId2, events2', 'A2.Version, 'A2.StorageName)
                                (eventId3, events3', 'A3.Version, 'A3.StorageName)
                            ]
                    StateCache<'A1>.Instance.Memoize2 (newState1 |> Ok) (idLists.[0] |> List.last)
                    StateCache<'A2>.Instance.Memoize2 (newState2 |> Ok) (idLists.[1] |> List.last)
                    StateCache<'A3>.Instance.Memoize2 (newState3 |> Ok) (idLists.[2] |> List.last)
                        
                    if (eventBroker.notify.IsSome) then
                        let idAndEvents1 = List.zip idLists.[0] events1'
                        let idAndEvents2 = List.zip idLists.[1] events2'
                        let idAndEvents3 = List.zip idLists.[2] events3'

                        postToProcessor (fun () -> tryPublish eventBroker 'A1.Version 'A1.StorageName idAndEvents1 |> ignore)
                        postToProcessor (fun () -> tryPublish eventBroker 'A2.Version 'A2.StorageName idAndEvents2 |> ignore)
                        postToProcessor (fun () -> tryPublish eventBroker 'A3.Version 'A3.StorageName idAndEvents3 |> ignore)
                        ()

                    let _ = mkSnapshotIfIntervalPassed<'A1, 'E1, 'F> storage
                    let _ = mkSnapshotIfIntervalPassed<'A2, 'E2, 'F> storage
                    let _ = mkSnapshotIfIntervalPassed<'A3, 'E3, 'F> storage

                    return ()
                } 
            let lookupNames = sprintf "%s_%s_%s" 'A1.StorageName 'A2.StorageName 'A3.StorageName
            let processor = MailBoxProcessors.Processors.Instance.GetProcessor lookupNames
            MailBoxProcessors.postToTheProcessor processor commands

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
        let processor = MailBoxProcessors.Processors.Instance.GetProcessor lookupName
        MailBoxProcessors.postToTheProcessor processor reset
