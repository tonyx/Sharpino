
namespace Sharpino

open System
open FSharpPlus.Data

open FSharp.Core
open FSharpPlus

open Sharpino.Core
open Sharpino.Storage
open Sharpino.Cache
open Sharpino.Utils
open Sharpino.Definitions
open Sharpino.StateView
open Sharpino.KafkaBroker
open System.Runtime
open System.Runtime.CompilerServices

open FsToolkit.ErrorHandling
open log4net
open log4net.Config

module CommandHandler =
    let serializer = new Utils.JsonSerializer(Utils.serSettings) :> Utils.ISerializer
    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)


    // deprecated use getStorageFreshStateViewer instead
    [<Obsolete "use getStorageFreshStateViewer instead">]
    let inline getStorageStateViewer<'A, 'E
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
            let result = fun () -> getState<'A, 'E> eventStore
            result
            
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

    let config = 
        try
            Conf.config ()
        with
        | :? _ as ex -> 
            // if appSettings.json is missing
            log.Error (sprintf "appSettings.json file not found using defult!!! %A\n" ex)
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
            let stateViewer = getStorageStateViewer<'A, 'E> storage
            async {
                return
                    ResultCE.result
                        {
                            let! (id, state) = stateViewer () //getState<'A, 'E> storage 
                            let serState = state.Serialize serializer
                            let! result = storage.SetSnapshot 'A.Version (id, serState) 'A.StorageName
                            return result 
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
        (stateViewer: unit -> Result<EventId * 'A * Option<int64>, string>) 
        (command: Command<'A, 'E>) =
        
            log.Debug (sprintf "runCommand %A" command)
            let command = fun () ->
                async {
                    return
                        result {
                            let! (_, state, _) = stateViewer() //<'A, 'E> storage
                            let! events =
                                state
                                |> command.Execute
                            // printf "XXX. events %A\n" events
                            let events' =
                                events 
                                |>> (fun x -> x.Serialize serializer)
                            let! ids =
                                events' |> storage.AddEvents 'A.Version 'A.StorageName 
                            let sent =
                                let idAndEvents = List.zip ids events'
                                let sent = tryPublish eventBroker 'A.Version 'A.StorageName idAndEvents
                                sent |> Result.toOption
                            let _ = mkSnapshotIfIntervalPassed<'A, 'E> storage
                            return ([ids], [sent])
                        }
                }
                |> Async.RunSynchronously 

            match config.PessimisticLock with
            | true ->
                lock 'A.Lock <| fun () ->
                    command()
            | false ->
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
            (command2: Command<'A2, 'E2>) =
            log.Debug (sprintf "runTwoCommands %A %A" command1 command2)

            let stateViewerA1 = getStorageStateViewer<'A1, 'E1> storage
            let stateViewerA2 = getStorageStateViewer<'A2, 'E2> storage

            let command = fun () ->
                async {
                    return
                        result {

                            let! (_, state1) = stateViewerA1 ()
                            let! (_, state2) = stateViewerA2 ()

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

            match config.PessimisticLock with
            | true ->
                lock ('A1.Lock, 'A2.Lock) <| fun () ->
                    command()
            | false ->
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
            (command3: Command<'A3, 'E3>) =
            log.Debug (sprintf "runTwoCommands %A %A" command1 command2)

            // todo: will be able to get event broker based viewers
            let stateViewerA1 = getStorageStateViewer<'A1, 'E1> storage
            let stateViewerA2 = getStorageStateViewer<'A2, 'E2> storage
            let stateViewerA3 = getStorageStateViewer<'A3, 'E3> storage

            let command = fun () ->
                async {
                    return
                        result {

                            let! (_, state1) = stateViewerA1 ()
                            let! (_, state2) = stateViewerA2 ()
                            let! (_, state3) = stateViewerA3 ()

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

            match config.PessimisticLock with
            | true ->
                lock ('A1.Lock, 'A2.Lock, 'A3.Lock) <| fun () ->
                    command()
            | false ->
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