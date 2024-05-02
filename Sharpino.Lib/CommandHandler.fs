
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
    open Sharpino.Lib.Core.Commons
    let serializer = new Utils.JsonSerializer(Utils.serSettings) :> Utils.ISerializer
    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    type StateViewer<'A> = unit -> Result<EventId * 'A * Option<KafkaOffset> * Option<KafkaPartitionId>, string>
    type AggregateViewer<'A> = Guid -> Result<EventId * 'A * Option<KafkaOffset> * Option<KafkaPartitionId>,string>

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
        and 'A: (static member Lock: obj)
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
            printf "appSettings.json file not found using defult!!! %A\n" ex
            Conf.defaultConf

    [<MethodImpl(MethodImplOptions.Synchronized)>]
    let inline private mkSnapshot<'A, 'E, 'F
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (member Serialize: 'F)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'A: (static member Lock: obj)
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
                            let! (id, state, _, _) = stateViewer ()
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
                            let! (eventId, state, _, _) = stateViewer aggregateId 
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
        and 'A: (static member Lock: obj)
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
                            let (lastEventId, _, _) = 
                                storage.TryGetLastEventIdByAggregateIdWithKafkaOffSet 'A.Version 'A.StorageName aggregateId
                                |> Option.defaultValue (0, None, None)
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
        and 'A: (static member Lock: obj)
        and 'A: (member Serialize: 'F)
        and 'A: (member StateId: Guid)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'A: (static member SnapshotsInterval : int)
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'E: (member Serialize: 'F) 
        >
        (storage: IEventStore<'F>) 
        (eventBroker: IEventBroker<'F>) 
        (stateViewer: StateViewer<'A>)
        (command: Command<'A, 'E>) =
        
            log.Debug (sprintf "runCommand %A" command)
            let delayedCommand = fun () ->
                async {
                    return
                        result {
                            let! (_, state, _, _) = stateViewer() 
                            let! events =
                                state
                                |> command.Execute
                            let events' =
                                events 
                                |>> fun x -> x.Serialize
                            let! ids =
                                events' |> storage.AddEvents 'A.Version 'A.StorageName state.StateId

                            // todo: reintroduce
                            if (eventBroker.notify.IsSome) then
                                let f =
                                    fun () ->
                                        List.zip ids events'
                                        |> tryPublish eventBroker 'A.Version 'A.StorageName
                                        |> ignore
                                f |> postToProcessor |> ignore

                            let _ = mkSnapshotIfIntervalPassed<'A, 'E, 'F> storage
                            return [ids]
                        }
                }
                |> Async.RunSynchronously 

            match config.LockType with
            | Pessimistic ->
                lock 'A.Lock <| fun () ->
                    delayedCommand()
            | _ ->
                delayedCommand()
    
    let inline runInitAndCommand<'A, 'E, 'A1, 'F
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (static member Lock: obj)
        and 'A: (member Serialize: 'F)
        and 'A: (static member Deserialize: 'F -> Result<'A, string>)
        and 'A: (static member SnapshotsInterval : int)
        and 'A: (member StateId: Guid)
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        and 'E: (member Serialize: 'F)
        and 'A1:> Aggregate<'F>
        and 'A1 : (static member StorageName: string) 
        and 'A1 : (static member Version: string)
        >
        (storage: IEventStore<'F>)
        (eventBroker: IEventBroker<'F>)
        (stateViewer: StateViewer<'A>)
        (initialInstance: 'A1)
        (command: Command<'A, 'E>)
        =
            log.Debug (sprintf "runInitAndCommand %A %A" 'A1.StorageName command)
            let delayedCommand = fun () ->
                async {
                    return
                        result {
                            let! (_, state, _, _) = stateViewer ()
                            let! events = 
                                state
                                |> command.Execute
                            let events' =
                                events 
                                |>> fun x -> x.Serialize
                            let! ids =
                                events' |> storage.SetInitialAggregateStateAndAddEvents initialInstance.Id initialInstance.StateId 'A1.Version 'A1.StorageName initialInstance.Serialize 'A.Version 'A.StorageName state.StateId

                            if (eventBroker.notify.IsSome) then
                                let f =
                                    fun () ->
                                        List.zip ids events'
                                        |> tryPublish eventBroker 'A.Version 'A.StorageName
                                        |> ignore
                                f |> postToProcessor |> ignore

                            let _ = mkSnapshotIfIntervalPassed<'A, 'E, 'F> storage
                            return [ids]
                        }
                }
                |> Async.RunSynchronously

            match config.LockType with
            | Pessimistic ->
                lock ('A.Lock, initialInstance.Lock) <| fun () ->
                    delayedCommand()
            | _ ->
                delayedCommand()

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
        (stateViewer: AggregateViewer<'A>)
        (command: Command<'A, 'E>)
        =
            let stateView = stateViewer aggregateId
            log.Debug (sprintf "runAggregateCommand %A" command)
            let delayedCommand = fun () ->
                async {
                    return
                        result {
                            let! (_, state, _, _) = stateView
                            let! events =
                                state
                                |> command.Execute
                            let events' =
                                events 
                                |>> fun x -> x.Serialize
                            let! ids =
                                events' |> storage.AddAggregateEvents 'A.Version 'A.StorageName state.Id state.StateId  // last one should be state_version_id

                            // todo: adapt event broker 
                            if (eventBroker.notifyAggregate.IsSome) then
                                let f =
                                    fun () ->    
                                        List.zip ids events'
                                        |> tryPublishAggregateEvent eventBroker aggregateId 'A.Version 'A.StorageName
                                        |> ignore
                                f |> postToProcessor |> ignore
                            
                            let _ = mkAggregateSnapshotIfIntervalPassed<'A, 'E, 'F> storage aggregateId    
                            return [ids]
                        }
                }
                |> Async.RunSynchronously 
            match (stateView, config.LockType) with
            |  Error e, _ -> Error e 
            |  _, Pessimistic ->
                // todo: review this that is suspicious. However: not important as the only recommended lock is the optimistic one
                let myLock =
                    stateView
                    |> Result.toOption
                    |> Option.map (fun (_, s, _, _) -> s.Lock)
                lock myLock <| fun () ->
                    delayedCommand()
            | _, _ ->
                delayedCommand()

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
        (storage: IEventStore<'F>)
        (eventBroker: IEventBroker<'F>)
        (stateViewer: AggregateViewer<'A1>)
        (commands: List<Command<'A1, 'E1>>)
        =
            log.Debug "runNAggregateCommands"
            let delayedCommand = fun () ->
                async {
                    return
                        result {
                            let! states =
                                aggregateIds
                                |> List.traverseResultM stateViewer
                                
                            let states' = 
                                states 
                                |>> fun (_, state, _, _) -> state

                            let statesAndCommands =
                                List.zip states' commands

                            let! events =
                                statesAndCommands
                                |>> fun (state, command) -> command.Execute state
                                |> List.traverseResultM id

                            let serializedEvents =
                                events 
                                |>> fun x -> x |>> fun (z: 'E1) -> z.Serialize 
                            
                            let aggregateIdsWithStateIds =
                                List.zip aggregateIds states'
                                |>> fun (id, state ) -> (id, state.StateId)
                                
                            let packParametersForDb =
                                List.zip serializedEvents aggregateIdsWithStateIds
                                |>> fun (events, (id, stateId)) -> (events, 'A1.Version, 'A1.StorageName, id, stateId)

                            let! eventIds =
                                storage.MultiAddAggregateEvents packParametersForDb

                            let aggregateIdsWithEventIds =
                                List.zip aggregateIds eventIds

                            // todo: reintroduce
                            let kafkaParameters =
                                List.map2 (fun idList serializedEvents -> (idList, serializedEvents)) aggregateIdsWithEventIds serializedEvents
                                |>> fun (((aggId: Guid), idList), serializedEvents) -> (aggId, List.zip idList serializedEvents)

                            // todo: reintroduce
                            if (eventBroker.notifyAggregate.IsSome) then
                                kafkaParameters
                                |>> fun (id, x) -> postToProcessor (fun () -> tryPublishAggregateEvent eventBroker id 'A1.Version 'A1.StorageName x |> ignore)
                                |> ignore

                            let _ =
                                aggregateIds
                                |>> mkAggregateSnapshotIfIntervalPassed<'A1, 'E1, 'F> storage
                            
                            return eventIds
                        }
                }
                |> Async.RunSynchronously 
            match config.LockType with
            | Optimistic ->
                delayedCommand()
            | _ ->
                log.Warn "locktype is pessimistic, but we are not using it in runNAggregateCommands"
                delayedCommand()

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
        (storage: IEventStore<'F>)
        (eventBroker: IEventBroker<'F>)
        (stateViewer1: AggregateViewer<'A1>)
        (stateViewer2: AggregateViewer<'A2>)
        (command1: List<Command<'A1, 'E1>>)
        (command2: List<Command<'A2, 'E2>>)
        =
            log.Debug "runTwoNAggregateCommands"
            let delayedCommand = fun () ->
                async {
                    return 
                        result {
                            let! states1 =
                                aggregateIds1
                                |> List.traverseResultM stateViewer1

                            let! states2 =
                                aggregateIds2
                                |> List.traverseResultM stateViewer2

                            let states1' =
                                states1 
                                |>> fun (_, state, _, _) -> state

                            let states2' =
                                states2 
                                |>> fun (_, state, _, _) -> state

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

                            let aggregateIdsWithStateIds1 =
                                List.zip aggregateIds1 states1'
                                |>> fun (id, state ) -> (id, state.StateId)
                        
                            let aggregateIdsWithStateIds2 =
                                List.zip aggregateIds2 states2'
                                |>> fun (id, state ) -> (id, state.StateId)

                            let packParametersForDb1 =
                                List.zip serializedEvents1 aggregateIdsWithStateIds1
                                |>> fun (events, (id, stateId)) -> (events, 'A1.Version, 'A1.StorageName, id, stateId)

                            let packParametersForDb2 =
                                List.zip serializedEvents2 aggregateIdsWithStateIds2
                                |>> fun (events, (id, stateId)) -> (events, 'A2.Version, 'A2.StorageName, id, stateId)

                            let allPacked = packParametersForDb1 @ packParametersForDb2

                            let! eventIds =
                                allPacked
                                |> storage.MultiAddAggregateEvents

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

                            // todo: reintroduce
                            if (eventBroker.notifyAggregate.IsSome) then
                                kafkaParmeters1
                                |>> fun (id, x) -> postToProcessor (fun () -> tryPublishAggregateEvent eventBroker id 'A1.Version 'A1.StorageName x |> ignore)
                                |> ignore

                            if (eventBroker.notifyAggregate.IsSome) then
                                kafkaParameters2
                                |>> fun (id, x) -> postToProcessor (fun () -> tryPublishAggregateEvent eventBroker id 'A2.Version 'A2.StorageName x |> ignore)
                                |> ignore

                            let _ =
                                aggregateIds1
                                |>> mkAggregateSnapshotIfIntervalPassed<'A1, 'E1, 'F> storage
                            let _ =
                                aggregateIds2
                                |>> mkAggregateSnapshotIfIntervalPassed<'A2, 'E2, 'F> storage
                        
                            return eventIds
                        }
                }
                |> Async.RunSynchronously

            match config.LockType with
            | Optimistic ->
                delayedCommand()
            | _ ->
                log.Warn "locktype is pessimistic, but we are not using it in runTwoNAggregateCommands"
                delayedCommand()


    let inline runTwoCommands<'A1, 'A2, 'E1, 'E2, 'F
        when 'A1: (static member Zero: 'A1)
        and 'A1: (static member StorageName: string)
        and 'A1: (member Serialize: 'F)
        and 'A1: (member StateId: Guid)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A2: (static member Zero: 'A2)
        and 'A2: (static member StorageName: string)
        and 'A2: (member Serialize: 'F)
        and 'A2: (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A1: (static member Version: string)
        and 'A2: (static member Version: string)
        and 'A1: (static member Lock: obj)
        and 'A1: (static member SnapshotsInterval : int)
        and 'A2: (static member Lock: obj)
        and 'A2: (static member SnapshotsInterval : int)
        and 'A2: (member StateId: Guid)
        and 'E1 :> Event<'A1>
        and 'E2 :> Event<'A2> 
        and 'E1: (static member Deserialize: 'F -> Result<'E1, string>)
        and 'E1: (member Serialize: 'F)
        and 'E2: (static member Deserialize: 'F -> Result<'E2, string>)
        and 'E2: (member Serialize: 'F)
        >
            (storage: IEventStore<'F>)
            (eventBroker: IEventBroker<'F>) 

            (command1: Command<'A1, 'E1>) 
            (command2: Command<'A2, 'E2>)
            (stateViewerA1: StateViewer<'A1>)
            (stateViewerA2: StateViewer<'A2>) =

            log.Debug (sprintf "runTwoCommands %A %A" command1 command2)

            let delayedCommand = fun () ->
                async {
                    return
                        result {

                            let! (_, state1, _, _) = stateViewerA1 ()
                            let! (_, state2, _, _) = stateViewerA2 ()

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
                                storage.MultiAddEvents 
                                    [
                                        (events1', 'A1.Version, 'A1.StorageName, state1.StateId)
                                        (events2', 'A2.Version, 'A2.StorageName, state2.StateId)
                                    ]

                            if (eventBroker.notify.IsSome) then
                                let idAndEvents1 = List.zip idLists.[0] events1'
                                let idAndEvents2 = List.zip idLists.[1] events2'
                                postToProcessor (fun () -> tryPublish eventBroker 'A1.Version 'A1.StorageName idAndEvents1 |> ignore)
                                postToProcessor (fun () -> tryPublish eventBroker 'A2.Version 'A2.StorageName idAndEvents2 |> ignore)
                                ()

                            let _ = mkSnapshotIfIntervalPassed<'A1, 'E1, 'F> storage
                            let _ = mkSnapshotIfIntervalPassed<'A2, 'E2, 'F> storage
                            return idLists
                        } 
                    }
                |> Async.RunSynchronously

            match config.LockType with
            | Pessimistic ->
                lock ('A1.Lock, 'A2.Lock) <| fun () ->
                    delayedCommand()
            | _ ->
                delayedCommand()

    let inline runThreeCommands<'A1, 'A2, 'A3, 'E1, 'E2, 'E3, 'F
        when 'A1: (static member Zero: 'A1)
        and 'A1: (static member StorageName: string)
        and 'A1: (member Serialize: 'F)
        and 'A1: (member StateId: Guid)
        and 'A1: (static member Deserialize: 'F -> Result<'A1, string>)
        and 'A2: (static member Zero: 'A2)
        and 'A2: (static member StorageName: string)
        and 'A2: (member Serialize: 'F)
        and 'A2: (member StateId: Guid)
        and 'A2: (static member Deserialize: 'F -> Result<'A2, string>)
        and 'A3: (static member Zero: 'A3)
        and 'A3: (static member StorageName: string)
        and 'A3: (member Serialize: 'F)
        and 'A2: (member StateId: Guid)
        and 'A3: (static member Deserialize: 'F -> Result<'A3, string>)
        and 'A1: (static member Version: string)
        and 'A2: (static member Version: string)
        and 'A3: (static member Version: string)
        and 'A1: (static member Lock: obj)
        and 'A2: (static member Lock: obj)
        and 'A3: (static member Lock: obj)
        and 'A1: (static member SnapshotsInterval : int)
        and 'A2: (static member SnapshotsInterval : int)
        and 'A3: (static member SnapshotsInterval : int)
        and 'A3: (member StateId: Guid)
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
            (stateViewerA1: StateViewer<'A1>)
            (stateViewerA2: StateViewer<'A2>)
            (stateViewerA3: StateViewer<'A3>)
            =
            log.Debug (sprintf "runTwoCommands %A %A" command1 command2)

            let delayedCommand = fun () ->
                async {
                    return
                        result {

                            let! (_, state1, _, _) = stateViewerA1 ()
                            let! (_, state2, _, _) = stateViewerA2 ()
                            let! (_, state3, _, _) = stateViewerA3 ()

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
                                        (events1', 'A1.Version, 'A1.StorageName, state1.StateId)
                                        (events2', 'A2.Version, 'A2.StorageName, state2.StateId)
                                        (events3', 'A3.Version, 'A2.StorageName, state3.StateId)
                                    ]
                            
                            // if (eventBroker.notify.IsSome) then
                            //     let idAndEvents1 = List.zip idLists.[0] events1'
                            //     let idAndEvents2 = List.zip idLists.[1] events2'
                            //     let idAndEvents3 = List.zip idLists.[2] events3'

                            //     postToProcessor (fun () -> tryPublish eventBroker 'A1.Version 'A1.StorageName idAndEvents1 |> ignore)
                            //     postToProcessor (fun () -> tryPublish eventBroker 'A2.Version 'A2.StorageName idAndEvents2 |> ignore)
                            //     postToProcessor (fun () -> tryPublish eventBroker 'A3.Version 'A3.StorageName idAndEvents3 |> ignore)
                            //     ()

                            let _ = mkSnapshotIfIntervalPassed<'A1, 'E1, 'F> storage
                            let _ = mkSnapshotIfIntervalPassed<'A2, 'E2, 'F> storage
                            let _ = mkSnapshotIfIntervalPassed<'A3, 'E3, 'F> storage

                            return idLists
                        } 
                }
                |> Async.RunSynchronously

            match config.LockType with
            | Pessimistic ->
                lock ('A1.Lock, 'A2.Lock, 'A3.Lock) <| fun () ->
                    delayedCommand()
            | _ ->
                delayedCommand()
