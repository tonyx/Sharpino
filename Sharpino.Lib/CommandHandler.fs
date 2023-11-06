
namespace Sharpino

open FSharpPlus.Data

open FSharp.Core
open FSharpPlus

open Sharpino.Core
open Sharpino.Storage
open Sharpino.Cache
open Sharpino.Utils
open Sharpino.Definitions

open FsToolkit.ErrorHandling
open log4net
open log4net.Config
open System.Runtime.CompilerServices

module CommandHandler =
    let serializer = new Utils.JsonSerializer(Utils.serSettings) :> Utils.ISerializer
    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    // enable for quick debugging
    // log4net.Config.BasicConfigurator.Configure() |> ignore

    let inline tryGetSnapshot<'A
        when 'A: (static member Zero: 'A) 
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (member Serialize: ISerializer -> string)
        and 'A: (static member Deserialize: ISerializer -> Json -> Result<'A, string>)
        >
        (id: int)
        (storage: IStorage) =
            let snapshot = storage.TryGetSnapshotById 'A.Version 'A.StorageName id
            match snapshot |>> snd with
            | Some snapshot' ->
                let deserSnapshot = 'A.Deserialize (serializer, snapshot')
                let eventid = snapshot |>> fst
                match deserSnapshot with
                | Ok deserSnapshot -> (eventid.Value, deserSnapshot) |> Ok
                | Error e -> 
                    log.Error (sprintf "deserialization error %A for snapshot %s" e snapshot')
                    printf "deserialization error %A for snapshot %s" e snapshot'
                    Error (sprintf "deserialization error %A for snapshot %s" e snapshot')
            | None ->
                (0, 'A.Zero) |> Ok

    let inline private getLastSnapshotOrStateCache<'A 
        when 'A: (static member Zero: 'A) 
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (static member Lock: obj)
        and 'A: (member Serialize: ISerializer -> string)
        and 'A: (static member Deserialize: ISerializer -> Json -> Result<'A, string>)
        >
        (storage: IStorage) =
            log.Debug "getLastSnapshot"
            async {
                return
                    result {
                        let lastCacheEventId = StateCache<'A>.Instance.LastEventId() |> Option.defaultValue 0
                        let (snapshotEventId, lastSnapshotId) = storage.TryGetLastSnapshotId 'A.Version 'A.StorageName |> Option.defaultValue (0, 0)
                        if (lastSnapshotId = 0 && lastCacheEventId = 0) then
                            return (0, 'A.Zero)
                        else
                            if lastCacheEventId >= snapshotEventId then
                                let! state = 
                                    Cache.StateCache<'A>.Instance.GestState lastCacheEventId
                                return (lastCacheEventId, state)
                            else
                                let! (eventId, snapshot) = 
                                    Cache.SnapCache<'A>.Instance.Memoize (fun () -> tryGetSnapshot<'A> lastSnapshotId storage) lastSnapshotId
                                return (eventId, snapshot)
                    }
            }
            |> Async.RunSynchronously

    let inline snapEventIdStateAndEvents<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (static member Lock: obj)
        and 'E :> Event<'A>
        and 'A: (member Serialize: ISerializer -> string)
        and 'A: (static member Deserialize: ISerializer -> Json -> Result<'A, string>)
        and 'E: (static member Deserialize: ISerializer -> Json -> Result<'E, string>)
        and 'E: (member Serialize: ISerializer -> string)
        >
        (storage: IStorage) = 
        log.Debug "snapIdStateAndEvents"
        async {
            return
                result {
                    let! (eventId, state) = getLastSnapshotOrStateCache<'A> storage
                    let! events = storage.GetEventsAfterId 'A.Version eventId 'A.StorageName
                    let result =
                        (eventId, state, events)
                    return result
                }
        }
        |> Async.RunSynchronously

    let inline getState<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (static member Lock: obj)
        and 'A: (static member Deserialize: ISerializer -> Json -> Result<'A, string>)
        and 'A: (member Serialize: ISerializer -> Json)
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: ISerializer -> Json -> Result<'E, string>)
        and 'E: (member Serialize: ISerializer -> string)
        >
        (storage: IStorage): Result< int * 'A, string> = 
            log.Debug "getState"
            let computeNewState =
                fun () ->
                    result {
                        let! (_, state, events) = snapEventIdStateAndEvents<'A, 'E> storage
                        let! deserEvents =
                            events 
                            |>> snd 
                            |> catchErrors (fun x -> 'E.Deserialize (serializer, x))
                        let! newState = 
                            deserEvents |> evolve<'A, 'E> state
                        return newState
                    }
            let lastEventId = storage.TryGetLastEventId 'A.Version 'A.StorageName |> Option.defaultValue 0
            let state = StateCache<'A>.Instance.Memoize computeNewState lastEventId
            match state with
            | Ok state -> (lastEventId, state) |> Ok
            | Error e -> Error e

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
        (storage: IStorage) =
            async {
                return
                    ResultCE.result
                        {
                            let! (id, state) = getState<'A, 'E> storage
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
        (storage: IStorage) =
            log.Debug "mkSnapshotIfInterval"
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
        (storage: IStorage) (command: Command<'A, 'E>) =
            log.Debug (sprintf "runCommand %A" command)
            let result =
                lock 'A.Lock <| fun () ->
                    async {
                        return
                            result {
                                let! (_, state) = getState<'A, 'E> storage
                                let! events =
                                    state
                                    |> command.Execute
                                let events' =
                                    events 
                                    |>> (fun x -> x.Serialize serializer)
                                return! 
                                    events' |> storage.AddEvents 'A.Version 'A.StorageName 
                            }
                    }
                    |> Async.RunSynchronously 
            let _ = 
                match result with
                | Ok _ -> mkSnapshotIfIntervalPassed<'A, 'E> storage
                | Error e -> Error e
            result
                        
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
            (storage: IStorage)

            (command1: Command<'A1, 'E1>) 
            (command2: Command<'A2, 'E2>) =
            log.Debug (sprintf "runTwoCommands %A %A" command1 command2)

            let result =
                lock ('A1.Lock, 'A2.Lock) <| fun () ->
                    async {
                        return
                            result {
                                let! (_, state1) = getState<'A1, 'E1> storage
                                let! (_, state2) = getState<'A2, 'E2> storage
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

                                return! 
                                    storage.MultiAddEvents 
                                        [
                                            (events1', 'A1.Version, 'A1.StorageName)
                                            (events2', 'A2.Version, 'A2.StorageName)
                                        ]
                            } 
                    }
                    |> Async.RunSynchronously
            let _ =
                match result with
                | Ok _ -> 
                    mkSnapshotIfIntervalPassed<'A1, 'E1> storage |> ignore
                    mkSnapshotIfIntervalPassed<'A2, 'E2> storage |> ignore
                    Ok ()
                | Error e -> Error e
            result

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
            (storage: IStorage)
            (command1: Command<'A1, 'E1>) 
            (command2: Command<'A2, 'E2>) 
            (command3: Command<'A3, 'E3>) =
            log.Debug (sprintf "runTwoCommands %A %A" command1 command2)
            let result =
                lock ('A1.Lock, 'A2.Lock, 'A3.Lock) <| fun () ->
                    async {
                        return
                            result {
                                let! (_, state1) = getState<'A1, 'E1> storage
                                let! (_, state2) = getState<'A2, 'E2> storage
                                let! (_, state3) = getState<'A3, 'E3> storage
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

                                return! 
                                    storage.MultiAddEvents 
                                        [
                                            (events1', 'A1.Version, 'A1.StorageName)
                                            (events2', 'A2.Version, 'A2.StorageName)
                                            (events3', 'A3.Version, 'A2.StorageName)
                                        ]
                            } 
                    }
                    |> Async.RunSynchronously
            let _ =
                match result with
                | Ok _ -> 
                    mkSnapshotIfIntervalPassed<'A1, 'E1> storage |> ignore
                    mkSnapshotIfIntervalPassed<'A2, 'E2> storage |> ignore
                    mkSnapshotIfIntervalPassed<'A3, 'E3> storage |> ignore
                    Ok ()
                | Error e -> Error e
            result

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