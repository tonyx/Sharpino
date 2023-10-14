
namespace Sharpino

open FSharpPlus.Data

open FSharp.Core
open FSharpPlus

open Sharpino.Core
open Sharpino.Storage

open FsToolkit.ErrorHandling
open log4net
open log4net.Config
open System.Runtime.CompilerServices
open Sharpino.Utils

module Repository =
    let serializer = new Utils.JsonSerializer(Utils.serSettings) :> Utils.ISerializer
    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    // you can configure log here, or in the main program (see tests)

    let inline private getLastSnapshot<'A 
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
                        let! result =
                            match storage.TryGetLastSnapshot 'A.Version 'A.StorageName  with
                            | Some (_, eventId, state) ->
                                let deserState = 'A.Deserialize (serializer, state) 
                                match deserState with
                                | Ok deserState -> (eventId, deserState) |> Ok
                                | Error e -> 
                                    log.Error (sprintf "getLastSnapshot: %s" e)
                                    (0, 'A.Zero) |> Ok
                            | None -> (0, 'A.Zero) |> Ok
                        return result
                    }
            }
            |> Async.RunSynchronously

    let inline snapIdStateAndEvents<'A, 'E
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
                    let! (id, state) = getLastSnapshot<'A> storage
                    let! events = storage.GetEventsAfterId 'A.Version id 'A.StorageName
                    let result =
                        (id, state, events)
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
            result {
                let! (lastSnapshotId, state, events) = snapIdStateAndEvents<'A, 'E> storage
                let lastEventId =
                    match events.Length with
                    | x when x > 0 -> events |> List.last |> fst
                    | _ -> lastSnapshotId 
                let! deserEvents =
                    events 
                    |>> snd 
                    |> catchErrors (fun x -> 'E.Deserialize (serializer, x))
                let! newState = 
                    deserEvents |> evolve<'A, 'E> state
                return (lastEventId, newState)
            }

    let inline runCommand<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (static member Lock: obj)
        and 'A: (member Serialize: ISerializer -> string)
        and 'A: (static member Deserialize: ISerializer -> Json -> Result<'A, string>)
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: ISerializer -> Json -> Result<'E, string>)
        and 'E: (member Serialize: ISerializer -> string)
        >
        (storage: IStorage) (command: Command<'A, 'E>) =
            log.Debug (sprintf "runCommand %A" command)
            async {
                return
                    result {
                        let! (_, state) = storage |> getState<'A, 'E>
                        let! events =
                            state
                            |> command.Execute
                        let serEvents = 
                            events |>> (fun x -> x.Serialize serializer)
                        return! 
                            storage.AddEvents 'A.Version serEvents 'A.StorageName
                    } 
            }
            |> Async.RunSynchronously
                        
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
        and 'A2: (static member Lock: obj)
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
                            events1 |>> 
                            (fun x -> x.Serialize serializer)
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

    let inline mkSnapshotIfInterval<'A, 'E
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