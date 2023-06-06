namespace Tonyx.EventSourcing

open System.Runtime.CompilerServices
open FSharp.Data.Sql
open FSharp.Core
open FSharpPlus
open FSharpPlus.Data
open Newtonsoft.Json

open Tonyx.EventSourcing
open Tonyx.EventSourcing.Utils
open Tonyx.EventSourcing.Core
open Tonyx.EventSourcing.Cache
open Tonyx.EventSourcing.Conf
open FsToolkit.ErrorHandling

module Repository =
    let inline getLastSnapshot<'A 
        when 'A: (static member Zero: 'A) 
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)>
        (storage: IStorage) = 

        ResultCE.result {
            let! result =
                match storage.TryGetLastSnapshot 'A.Version 'A.StorageName  with
                | Some (id, eventId, json) ->
                    let state = SnapCache<'A>.Instance.Memoize (fun () -> json |> deserialize<'A>) id
                    match state with
                    | Error e -> Error e
                    | _ -> (eventId, state |> Result.get) |> Ok
                | None -> (0, 'A.Zero) |> Ok
            return result
        }

    let inline getState<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (static member LockObj: obj)
        and 'E :> Event<'A>>(storage: IStorage) = 

        let idStateAndEvents()  =
            lock 'A.LockObj ( fun _ ->
                ResultCE.result {
                    let! (id, state) = getLastSnapshot<'A> storage
                    let events = storage.GetEventsAfterId 'A.Version id 'A.StorageName
                    let result =
                        (id, state, events)
                    return result
                }
            )

        ResultCE.result {
            let! (id, state, events) = idStateAndEvents()
            let lastId =
                match events.Length with
                | x when x > 0 -> events |> List.last |> fst
                | _ -> id
            let! events' =
                events |>> snd |> catchErrors deserialize<'E>
            let! result =
                events' |> evolve<'A, 'E> state
            return (lastId, result)
        }

    let inline runCommand<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (static member LockObj: obj)
        and 'E :> Event<'A>> (storage: IStorage) (mycommand: Command<'A, 'E>)  =

        lock 'A.LockObj <| fun () -> 
            ResultCE.result {
                let! (_, state) = getState<'A, 'E> storage
                let! events =
                    state
                    |> mycommand.Execute
                let! eventsAdded' =
                    storage.AddEvents 'A.Version (events |>> (fun x -> Utils.serialize x)) 'A.StorageName
                return ()
            } 

    let inline runTwoCommands<'A1, 'A2, 'E1, 'E2 
        when 'A1: (static member Zero: 'A1)
        and 'A1: (static member StorageName: string)
        and 'A2: (static member Zero: 'A2)
        and 'A1: (static member LockObj: obj)
        and 'A2: (static member StorageName: string)
        and 'A1: (static member Version: string)
        and 'A2: (static member Version: string)
        and 'A2: (static member LockObj: obj)
        and 'E1 :> Event<'A1>
        and 'E2 :> Event<'A2>> 
            (storage: IStorage)
            (command1: Command<'A1, 'E1>) 
            (command2: Command<'A2, 'E2>) =

            lock ('A1.LockObj, 'A2.LockObj) <| fun () -> 
                ResultCE.result {
                    let! (_, state1) = getState<'A1, 'E1> storage
                    let! (_, state2) = getState<'A2, 'E2> storage
                    let! events1 =
                        state1
                        |> command1.Execute
                    let! events2 =
                        state2
                        |> command2.Execute

                    let! eventsAdded =
                        let serEv1 = events1 |>> Utils.serialize 
                        let serEv2 = events2 |>> Utils.serialize
                        storage.MultiAddEvents 
                            [
                                (serEv1, 'A1.Version, 'A1.StorageName)
                                (serEv2, 'A2.Version, 'A2.StorageName)
                            ]
                    return ()
                }

    let inline mksnapshot<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (static member LockObj: obj)
        and 'E :> Event<'A>> (storage: IStorage) =
        ResultCE.result
            {
                let! (id, state) = getState<'A, 'E> storage
                let snapshot = Utils.serialize<'A> state
                let! result = storage.SetSnapshot 'A.Version (id, snapshot) 'A.StorageName
                return result
            }

    let inline mkSnapshotIfInterval<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (static member LockObj: obj)
        and 'A: (static member SnapshotsInterval : int)
        and 'E :> Event<'A>>(storage: IStorage) =

        ResultCE.result
            {
                let! lastEventId = storage.TryGetLastEventId 'A.Version 'A.StorageName |> Result.ofOption "lastEventId is None"
                let snapEventId = storage.TryGetLastSnapshotEventId 'A.Version 'A.StorageName |> Option.defaultValue 0
                let! result =
                    if ((lastEventId - snapEventId)) > 'A.SnapshotsInterval || snapEventId = 0 then
                        mksnapshot<'A, 'E>(storage)
                    else
                        () |> Ok
                return result
            }
