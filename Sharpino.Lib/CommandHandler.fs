
namespace Sharpino

open System
open FSharpPlus.Data

open FSharp.Core
open FSharp.Data
open FSharpPlus
open FSharpPlus.Operators

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

    let inline getStorageFreshStateViewer<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (member Serialize: ISerializer -> string)
        and 'A: (static member Deserialize: ISerializer -> Json -> Result<'A, string>)
        and 'A: (static member Lock: obj)
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: ISerializer -> Json -> Result<'E, string>)
        and 'E: (member Serialize: ISerializer -> string)
        >(eventStore: IEventStore) =
            let result = fun () -> getFreshState<'A, 'E> eventStore
            result

    let inline getAggregateStorageFreshStateViewer<'A, 'E
        when 'A :> Aggregate 
        and 'A :> Entity 
        and 'A : (static member Deserialize: ISerializer -> Json -> Result<'A, string>) 
        and 'A : (static member StorageName: string) 
        and 'A : (static member Version: string) 
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: ISerializer -> Json -> Result<'E, string>)
        >
        (eventStore: IEventStore) 
        =
            let result = fun (id: Guid) -> getAggregateFreshState<'A, 'E> id eventStore 
            result

    let config = 
        try
            Conf.config ()
        with
        | :? _ as ex -> 
            log.Error (sprintf "appSettings.json file not found using defult!!! %A\n" ex)
            printf "appSettings.json file not found using defult!!! %A\n" ex
            Conf.defaultConf

    [<MethodImpl(MethodImplOptions.Synchronized)>]
    let inline private mksnapshot<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (member Serialize: ISerializer -> string)
        and 'A: (static member Deserialize: ISerializer -> Json -> Result<'A, string>)
        and 'A: (static member Lock: obj)
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: ISerializer -> Json -> Result<'E, string>)
        and 'E: (member Serialize: ISerializer -> string)
        > 
        (storage: IEventStore) =
            let stateViewer = getStorageFreshStateViewer<'A, 'E> storage
            async {
                return
                    ResultCE.result
                        {
                            let! (id, state, _, _) = stateViewer ()
                            let serState = state.Serialize serializer
                            let! result = storage.SetSnapshot 'A.Version (id, serState) 'A.StorageName
                            return result 
                        }
            }
            |> Async.RunSynchronously

    [<MethodImpl(MethodImplOptions.Synchronized)>]
    let inline private mkAggregateSnapshot<'A, 'E
        when 'A :> Aggregate 
        and 'A :> Entity 
        and 'E :> Event<'A>
        and 'A : (static member Deserialize: ISerializer -> Json -> Result<'A, string>) 
        and 'A : (static member StorageName: string) 
        and 'A : (static member Version: string) 
        and 'E : (static member Deserialize: ISerializer -> Json -> Result<'E, string>)
        and 'E : (member Serialize: ISerializer -> string)
        > 
        (storage: IEventStore) 
        (aggregateId: AggregateId) =
            let stateViewer = getAggregateStorageFreshStateViewer<'A, 'E> storage
            async {
                return
                    ResultCE.result
                        {
                            let! (eventId, state, _, _) = stateViewer aggregateId 
                            let serState = state.Serialize serializer
                            let result = storage.SetAggregateSnapshot 'A.Version (aggregateId, eventId, serState) 'A.StorageName
                            return! result 
                        }
            }
            |> Async.RunSynchronously
     
    let inline mkSnapshotIfIntervalPassed<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (static member SnapshotsInterval : int)
        and 'A: (static member Lock: obj)
        and 'A: (member Serialize: ISerializer -> string)
        and 'A: (static member Deserialize: ISerializer -> Json -> Result<'A, string>)
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: ISerializer -> Json -> Result<'E, string>)
        and 'E: (member Serialize: ISerializer -> string)
        >
        (storage: IEventStore) =
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
                                    mksnapshot<'A, 'E> storage
                                else
                                    () |> Ok
                        }
            }    
            |> Async.RunSynchronously
            
    let inline mkAggregateSnapshotIfIntervalPassed<'A, 'E
        when 'A :> Aggregate 
        and 'A :> Entity 
        and 'E :> Event<'A>
        and 'A : (static member Deserialize: ISerializer -> Json -> Result<'A, string>) 
        and 'A : (static member StorageName: string)
        and 'A : (static member SnapshotsInterval : int)
        and 'A : (static member Version: string) 
        and 'E : (static member Deserialize: ISerializer -> Json -> Result<'E, string>)
        and 'E : (member Serialize: ISerializer -> string)
        >
        (storage: IEventStore)
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
                                    mkAggregateSnapshot<'A, 'E> storage aggregateId 
                                else
                                    () |> Ok
                            return! result
                        }
            }
            |> Async.RunSynchronously
            

    let inline runCommand<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (static member Lock: obj)
        and 'A: (member Serialize: ISerializer -> string)
        and 'A: (static member Deserialize: ISerializer -> Json -> Result<'A, string>)
        and 'A: (static member SnapshotsInterval : int)
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: ISerializer -> Json -> Result<'E, string>)
        and 'E: (member Serialize: ISerializer -> string)
        >
        (storage: IEventStore) 
        (eventBroker: IEventBroker) 
        (stateViewer: StateViewer<'A>)
        (command: Command<'A, 'E>) =
        
            log.Debug (sprintf "runCommand %A" command)
            let command = fun () ->
                async {
                    return
                        result {
                            let! (_, state, _, _) = stateViewer() 
                            let! events =
                                state
                                |> command.Execute
                            let events' =
                                events 
                                |>> (fun x -> x.Serialize serializer)
                            let! ids =
                                events' |> storage.AddEvents 'A.Version 'A.StorageName 
                            let sent =
                                List.zip ids events'
                                |> tryPublish eventBroker 'A.Version 'A.StorageName
                                |> Result.toOption
                            let _ = mkSnapshotIfIntervalPassed<'A, 'E> storage
                            return ([ids], [sent])
                        }
                }
                |> Async.RunSynchronously 

            match config.LockType with
            | Pessimistic ->
                lock 'A.Lock <| fun () ->
                    command()
            | _ ->
                command()
    
    let inline commandInitContextAggregate<'A, 'E, 'A1
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (static member Lock: obj)
        and 'A: (member Serialize: ISerializer -> string)
        and 'A: (static member Deserialize: ISerializer -> Json -> Result<'A, string>)
        and 'A: (static member SnapshotsInterval : int)
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: ISerializer -> Json -> Result<'E, string>)
        and 'E: (member Serialize: ISerializer -> string)
        and 'A1:> Aggregate
        and 'A1 : (static member StorageName: string) 
        and 'A1 : (static member Version: string)
        >
        (storage: IEventStore)
        (eventBroker: IEventBroker)
        (stateViewer: StateViewer<'A>)
        (initialInstance: 'A1)
        (command: Command<'A, 'E>)
        =
            ResultCE.result {
                let initSnapshot = initialInstance.Serialize serializer
                let! stored = storage.SetInitialAggregateState initialInstance.Id initialInstance.StateId 'A1.Version 'A1.StorageName initSnapshot
                let! result = runCommand<'A, 'E> storage eventBroker stateViewer command
                return result
            }
        
    
    let inline runAggregateCommand<'A, 'E
        when 'A :> Aggregate 
        and 'A :> Entity 
        and 'E :> Event<'A>
        and 'A : (static member Deserialize: ISerializer -> Json -> Result<'A, string>) 
        and 'A : (static member StorageName: string) 
        and 'A : (static member Version: string)
        and 'A : (static member SnapshotsInterval: int)
        and 'E : (static member Deserialize: ISerializer -> Json -> Result<'E, string>)
        and 'E : (member Serialize: ISerializer -> string)
        >
        (aggregateId: Guid)
        (storage: IEventStore)
        (eventBroker: IEventBroker)
        (stateViewer: StateViewer<'A>)
        (command: Command<'A, 'E>)
        =
            let stateView = stateViewer()
            log.Debug (sprintf "runAggregateCommand %A" command)
            let command = fun () ->
                async {
                    return
                        result {
                            let! (_, state, _, _) = stateView
                            let! events =
                                state
                                |> command.Execute
                            let events' =
                                events 
                                |>> (fun x -> x.Serialize serializer)
                            let! ids =
                                events' |> storage.AddAggregateEvents 'A.Version 'A.StorageName state.Id state.StateId  // last one should be state_version_id
                            let sent =
                                List.zip ids events'
                                |> tryPublishAggregateEvent eventBroker aggregateId 'A.Version 'A.StorageName 
                                |> Result.toOption
                            let _ = mkAggregateSnapshotIfIntervalPassed<'A, 'E> storage aggregateId    
                            return ([ids], [sent])
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
                    command()
            | _, _ ->
                command()

    let inline runNAggregateCommands<'A1, 'E1
        when 'A1 :> Aggregate
        and 'A1 :> Entity
        and 'E1 :> Event<'A1>
        and 'E1 : (member Serialize: ISerializer -> string)
        and 'E1 : (static member Deserialize: ISerializer -> Json -> Result<'E1, string>)
        and 'A1 : (static member Deserialize: ISerializer -> Json -> Result<'A1, string>)
        and 'A1 : (static member SnapshotsInterval: int)
        and 'A1 : (static member StorageName: string)
        and 'A1 : (static member Version: string)
        >
        (aggregateIds: List<Guid>)
        (storage: IEventStore)
        (eventBroker: IEventBroker)
        (stateViewers: List<StateViewer<'A1>>)
        (commands: List<Command<'A1, 'E1>>)
        =
            log.Debug "runNAggregateCommands"
            let command = fun () ->
                async {
                    return
                        result {
                            let! states =
                                stateViewers
                                |>> (fun x -> x())
                                |> List.traverseResultM id

                            let states' = 
                                states 
                                |>> (fun (_, state, _, _) -> state)

                            let statesAndCommands =
                                List.zip states' commands

                            let! events =
                                statesAndCommands
                                |>> (fun (state, command) -> command.Execute state)
                                |> List.traverseResultM id

                            let serializedEvents =
                                events 
                                |>> (fun x -> x |>> fun (z: 'E1) -> z.Serialize serializer )
                            
                            let aggregateIdsWithStateIds =
                                List.zip aggregateIds states'
                                |>> (fun (id, state ) -> (id, state.StateId))    
                                
                            let packParametersForDb =
                                List.zip serializedEvents aggregateIdsWithStateIds
                                |>> (fun (events, (id, stateId)) -> (events, 'A1.Version, 'A1.StorageName, id, stateId))

                            let! idLists =
                                storage.MultiAddAggregateEvents packParametersForDb

                            let kafkaParameters =
                                 List.map2 (fun idList serializedEvents -> (idList, serializedEvents)) idLists serializedEvents
                                 |>>  (fun (idList, serializedEvents) -> List.zip idList serializedEvents)

                            let sent =
                                kafkaParameters
                                |>> (fun x -> tryPublish eventBroker 'A1.Version 'A1.StorageName x |> Result.toOption)
                                
                            let _ =
                                aggregateIds
                                |> List.map (fun id -> mkAggregateSnapshotIfIntervalPassed<'A1, 'E1> storage id)
                            
                            return (idLists, sent)
                        }
                }
                |> Async.RunSynchronously 
            match config.LockType with
            | Optimistic ->
                command()
            | _ ->
                log.Warn "locktype is pessimistic, but we are not using it in runNAggregateCommands"
                command()
                        
    let inline runTwoCommands<'A1, 'A2, 'E1, 'E2 
        when 'A1: (static member Zero: 'A1)
        and 'A1: (static member StorageName: string)
        and 'A1: (member Serialize: ISerializer -> string)
        and 'A1: (static member Deserialize: ISerializer -> Json -> Result<'A1, string>)
        and 'A2: (static member Zero: 'A2)
        and 'A2: (static member StorageName: string)
        and 'A2: (member Serialize: ISerializer -> string)
        and 'A2: (static member Deserialize: ISerializer -> Json -> Result<'A2, string>)
        and 'A1: (static member Version: string)
        and 'A2: (static member Version: string)
        and 'A1: (static member Lock: obj)
        and 'A1: (static member SnapshotsInterval : int)
        and 'A2: (static member Lock: obj)
        and 'A2: (static member SnapshotsInterval : int)
        and 'E1 :> Event<'A1>
        and 'E2 :> Event<'A2> 
        and 'E1: (static member Deserialize: ISerializer -> Json -> Result<'E1, string>)
        and 'E1: (member Serialize: ISerializer -> string)
        and 'E2: (static member Deserialize: ISerializer -> Json -> Result<'E2, string>)
        and 'E2: (member Serialize: ISerializer -> string)
        >
            (storage: IEventStore)
            (eventBroker: IEventBroker) 

            (command1: Command<'A1, 'E1>) 
            (command2: Command<'A2, 'E2>)
            (stateViewerA1: StateViewer<'A1>)
            (stateViewerA2: StateViewer<'A2>) =

            log.Debug (sprintf "runTwoCommands %A %A" command1 command2)

            let command = fun () ->
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
                                |>> (fun x -> x.Serialize serializer)
                            let events2' =
                                events2 
                                |>> (fun x -> x.Serialize serializer)

                            let! idLists =
                                storage.MultiAddEvents 
                                    [
                                        (events1', 'A1.Version, 'A1.StorageName)
                                        (events2', 'A2.Version, 'A2.StorageName)
                                    ]
                            let sent =
                                let idAndEvents1 = List.zip idLists.[0] events1'
                                let idAndEvents2 = List.zip idLists.[1] events2'
                                let sent1 = tryPublish eventBroker 'A1.Version 'A1.StorageName idAndEvents1 |> Result.toOption
                                let sent2 = tryPublish eventBroker 'A2.Version 'A2.StorageName idAndEvents2 |> Result.toOption
                                [ sent1; sent2 ]
                            let _ = mkSnapshotIfIntervalPassed<'A1, 'E1> storage
                            let _ = mkSnapshotIfIntervalPassed<'A2, 'E2> storage
                            return (idLists, sent)
                        } 
                    }
                |> Async.RunSynchronously

            match config.LockType with
            | Pessimistic ->
                lock ('A1.Lock, 'A2.Lock) <| fun () ->
                    command()
            | _ ->
                command()

    let inline runThreeCommands<'A1, 'A2, 'A3, 'E1, 'E2, 'E3
        when 'A1: (static member Zero: 'A1)
        and 'A1: (static member StorageName: string)
        and 'A1: (member Serialize: ISerializer -> string)
        and 'A1: (static member Deserialize: ISerializer -> Json -> Result<'A1, string>)
        and 'A2: (static member Zero: 'A2)
        and 'A2: (static member StorageName: string)
        and 'A2: (member Serialize: ISerializer -> string)
        and 'A2: (static member Deserialize: ISerializer -> Json -> Result<'A2, string>)
        and 'A3: (static member Zero: 'A3)
        and 'A3: (static member StorageName: string)
        and 'A3: (member Serialize: ISerializer -> string)
        and 'A3: (static member Deserialize: ISerializer -> Json -> Result<'A3, string>)
        and 'A1: (static member Version: string)
        and 'A2: (static member Version: string)
        and 'A3: (static member Version: string)
        and 'A1: (static member Lock: obj)
        and 'A2: (static member Lock: obj)
        and 'A3: (static member Lock: obj)
        and 'A1: (static member SnapshotsInterval : int)
        and 'A2: (static member SnapshotsInterval : int)
        and 'A3: (static member SnapshotsInterval : int)
        and 'E1 :> Event<'A1>
        and 'E2 :> Event<'A2> 
        and 'E3 :> Event<'A3>
        and 'E1: (static member Deserialize: ISerializer -> Json -> Result<'E1, string>)
        and 'E1: (member Serialize: ISerializer -> string)
        and 'E2: (static member Deserialize: ISerializer -> Json -> Result<'E2, string>)
        and 'E2: (member Serialize: ISerializer -> string)
        and 'E3: (static member Deserialize: ISerializer -> Json -> Result<'E3, string>)
        and 'E3: (member Serialize: ISerializer -> string)
        > 
            (storage: IEventStore)
            (eventBroker: IEventBroker) 
            (command1: Command<'A1, 'E1>) 
            (command2: Command<'A2, 'E2>) 
            (command3: Command<'A3, 'E3>) 
            (stateViewerA1: StateViewer<'A1>)
            (stateViewerA2: StateViewer<'A2>)
            (stateViewerA3: StateViewer<'A3>)
            =
            log.Debug (sprintf "runTwoCommands %A %A" command1 command2)

            let command = fun () ->
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
                                |>> (fun x -> x.Serialize serializer)
                            let events2' =
                                events2 
                                |>> (fun x -> x.Serialize serializer)
                            let events3' =
                                events3 
                                |>> (fun x -> x.Serialize serializer)

                            let! idLists =
                                storage.MultiAddEvents 
                                    [
                                        (events1', 'A1.Version, 'A1.StorageName)
                                        (events2', 'A2.Version, 'A2.StorageName)
                                        (events3', 'A3.Version, 'A2.StorageName)
                                    ]
                            
                            let sent =
                                let idAndEvents1 = List.zip idLists.[0] events1'
                                let idAndEvents2 = List.zip idLists.[1] events2'
                                let idAndEvents3 = List.zip idLists.[2] events3'

                                let sent1 = tryPublish eventBroker 'A1.Version 'A1.StorageName idAndEvents1 |> Result.toOption
                                let sent2 = tryPublish eventBroker 'A2.Version 'A2.StorageName idAndEvents2 |> Result.toOption
                                let sent3 = tryPublish eventBroker 'A3.Version 'A3.StorageName idAndEvents3 |> Result.toOption
                                [ sent1, sent2, sent3 ]
                            let _ = mkSnapshotIfIntervalPassed<'A1, 'E1> storage
                            let _ = mkSnapshotIfIntervalPassed<'A2, 'E2> storage
                            let _ = mkSnapshotIfIntervalPassed<'A3, 'E3> storage

                            return (idLists, sent)
                        } 
                }
                |> Async.RunSynchronously

            match config.LockType with
            | Pessimistic ->
                lock ('A1.Lock, 'A2.Lock, 'A3.Lock) <| fun () ->
                    command()
            | _ ->
                command()

    type UnitResult = ((unit -> Result<unit, string>) * AsyncReplyChannel<Result<unit,string>>)

    // by using this light processor we process the events in a single thread. It is not always needed but at the moment we stick to it
    // note: I guess this is too strict, and it is almost always _not_ needed. Keep it for now but should not use it.
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