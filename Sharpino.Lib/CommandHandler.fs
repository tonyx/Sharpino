
namespace Sharpino

open System

open FSharp.Core
open FSharpPlus

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
    type StateViewer<'A> = unit -> Result<EventId * 'A, string>
    type AggregateViewer<'A> = Guid -> Result<EventId * 'A,string>

    type UnitResult = ((unit -> unit) * AsyncReplyChannel<unit>)

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
        processor.PostAndAsyncReply(fun rc -> f, rc)
        |> Async.RunSynchronously

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

    [<MethodImpl(MethodImplOptions.Synchronized)>]
    let inline private mkSnapshot<'A, 'E, 'F
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (member Serialize: 'F)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'E: (member Serialize: 'F)
        > 
        (storage: IEventStore<'F>) =
            let stateViewer = getStorageFreshStateViewer<'A, 'E, 'F> storage
            async {
                return
                    ResultCE.result
                        {
                            let! (id, state) = stateViewer ()
                            let serState = state.Serialize
                            let! result = storage.SetSnapshot 'A.Version (id, serState) 'A.StorageName
                            return result 
                        }
            }
            |> Async.RunSynchronously

    [<MethodImpl(MethodImplOptions.Synchronized)>]
    let inline private mkAggregateSnapshot<'A, 'E, 'F
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
            let stateViewer = getAggregateStorageFreshStateViewer<'A, 'E, 'F> storage
            async {
                return
                    ResultCE.result
                        {
                            let! (eventId, state) = stateViewer aggregateId 
                            let serState = state.Serialize 
                            let result = storage.SetAggregateSnapshot 'A.Version (aggregateId, eventId, serState) 'A.StorageName
                            return! result 
                        }
            }
            |> Async.RunSynchronously
     
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
            log.Debug "mkSnapshotIfIntervalPassed"
            async {
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
            }    
            |> Async.RunSynchronously
            
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
            log.Debug "mkAggregateSnapshotIfIntervalPassed"
            async {
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
            }
            |> Async.RunSynchronously

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
            log.Debug (sprintf "runCommand %A\n" command)
            let command = fun ()  ->
                async {
                    return
                        result {
                            let! (eventId, state) = getFreshState<'A, 'E, 'F> storage
                            let! events =
                                state
                                |> command.Execute
                            let events' =
                                events 
                                |>> fun x -> x.Serialize
                            let! ids =
                                events' |> storage.AddEvents eventId 'A.Version 'A.StorageName 

                            if (eventBroker.notify.IsSome) then
                                let f =
                                    fun () ->
                                        tryPublish eventBroker 'A.Version 'A.StorageName (List.zip ids events')
                                        |> ignore
                                f |> postToProcessor |> ignore

                            let _ = mkSnapshotIfIntervalPassed<'A, 'E, 'F> storage
                            return ()
                        }
                }
                |> Async.RunSynchronously
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
            log.Debug (sprintf "runInitAndCommand %A %A" 'A1.StorageName command)
            let command = fun () ->
                async {
                    return
                        result {
                            let! (eventId, state) = getFreshState<'A, 'E, 'F> storage
                            let! events = 
                                state
                                |> command.Execute
                            let events' =
                                events 
                                |>> fun x -> x.Serialize
                            let! ids =
                                events' |> storage.SetInitialAggregateStateAndAddEvents eventId initialInstance.Id 'A1.Version 'A1.StorageName initialInstance.Serialize 'A.Version 'A.StorageName 

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
                }
                |> Async.RunSynchronously
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
        (command: Command<'A1, 'E1>)
        = 
            log.Debug (sprintf "runInitAndAggregateCommand %A %A" 'A1.StorageName command)
            let command = fun () ->
                async {
                    return
                        result {
                            let! (eventId, state) = getAggregateFreshState<'A1, 'E1, 'F> aggregateId storage
                            let! events =
                                state
                                |> command.Execute
                            let events' =
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
    
                            let _ = mkAggregateSnapshotIfIntervalPassed<'A1, 'E1, 'F> storage
                            return ()
                        }
                }
                |> Async.RunSynchronously
            let processor = MailBoxProcessors.Processors.Instance.GetProcessor 'A1.StorageName
            MailBoxProcessors.postToTheProcessor processor command

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
        (command: Command<'A, 'E>)
        =
            log.Debug (sprintf "runAggregateCommand %A" command)
            let command = fun () ->
                async {
                    return
                        result {
                            let! (eventId, state) = getAggregateFreshState<'A, 'E, 'F> aggregateId storage
                            let! events =
                                state
                                |> command.Execute
                            let events' =
                                events 
                                |>> fun x -> x.Serialize
                            let! ids =
                                events' |> storage.AddAggregateEvents eventId 'A.Version 'A.StorageName state.Id 

                            if (eventBroker.notifyAggregate.IsSome) then
                                let f =
                                    fun () ->    
                                        List.zip ids events'
                                        |> tryPublishAggregateEvent eventBroker aggregateId 'A.Version 'A.StorageName
                                        |> ignore
                                f |> postToProcessor |> ignore
                                
                            let _ = mkAggregateSnapshotIfIntervalPassed<'A, 'E, 'F> storage aggregateId
                            return ()
                        }
                }
                |> Async.RunSynchronously
            let processor = MailBoxProcessors.Processors.Instance.GetProcessor (sprintf "%s_%s" 'A.StorageName (aggregateId.ToString()))
            MailBoxProcessors.postToTheProcessor processor command

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
        (commands: List<Command<'A1, 'E1>>)
        =
            log.Debug "runNAggregateCommands"
            let commands = fun () ->
                async {
                    return
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

                            let! events =
                                statesAndCommands
                                |>> fun (state, command) -> command.Execute state
                                |> List.traverseResultM id

                            let serializedEvents =
                                events 
                                |>> fun x -> x |>> fun (z: 'E1) -> z.Serialize 
                                    
                            let packParametersForDb =
                                List.zip3 lastEventIds serializedEvents aggregateIds
                                |>> fun (eventId, events, id) -> (eventId, events, 'A1.Version, 'A1.StorageName, id)

                            let! eventIds =
                                eventStore.MultiAddAggregateEvents packParametersForDb

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
                }
                |> Async.RunSynchronously
            let aggregateIds = aggregateIds |> List.map (fun x -> x.ToString()) |> List.sort |> String.concat "_"
            let lookupName = sprintf "%s_%s" 'A1.StorageName aggregateIds
            let processor = MailBoxProcessors.Processors.Instance.GetProcessor lookupName
            MailBoxProcessors.postToTheProcessor processor commands

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
        (command1: List<Command<'A1, 'E1>>)
        (command2: List<Command<'A2, 'E2>>)
        =
            let commands = fun () ->
                async {
                    return 
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
                                |>> fun x -> x |>> fun (z: 'E1) -> z.Serialize

                            let serializedEvents2 =
                                events2 
                                |>> fun x -> x |>> fun (z: 'E2) -> z.Serialize

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
                            // return eventIds
                        }
                }
                |> Async.RunSynchronously
            let aggregateIds = aggregateIds1 @ aggregateIds2 |> List.map (fun x -> x.ToString()) |> List.sort |> String.concat "_"
            let lookupName = sprintf "%s_%s_%s" 'A1.StorageName 'A2.StorageName aggregateIds
            MailBoxProcessors.postToTheProcessor (MailBoxProcessors.Processors.Instance.GetProcessor lookupName) commands

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

            log.Debug (sprintf "runTwoCommands %A %A" command1 command2)

            let commands = fun () ->
                async {
                    return
                        result {

                            let! (eventId1, state1) = getFreshState<'A1, 'E1, 'F> eventStore
                            let! (eventId2, state2) = getFreshState<'A2, 'E2, 'F> eventStore

                            let! events1 =
                                state1
                                |> command1.Execute
                            let! events2 =
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
                    }
                |> Async.RunSynchronously
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
            log.Debug (sprintf "runTwoCommands %A %A" command1 command2)
            
            let commands = fun () ->
                async {
                    return
                        result {

                            let! (eventId1, state1) = getFreshState<'A1, 'E1, 'F> storage
                            let! (eventId2, state2) = getFreshState<'A2, 'E2, 'F> storage
                            let! (eventId3, state3) = getFreshState<'A3, 'E3, 'F> storage

                            let! events1 =
                                state1
                                |> command1.Execute
                            let! events2 =
                                state2
                                |> command2.Execute
                            let! events3 =
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
                }
                |> Async.RunSynchronously
            let lookupNames = sprintf "%s_%s_%s" 'A1.StorageName 'A2.StorageName 'A3.StorageName
            let processor = MailBoxProcessors.Processors.Instance.GetProcessor lookupNames
            MailBoxProcessors.postToTheProcessor processor commands
