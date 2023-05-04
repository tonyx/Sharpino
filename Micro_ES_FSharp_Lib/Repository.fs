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

module Repository =
    let storage: IStorage =
        match Conf.storageType with
            | StorageType.Memory -> MemoryStorage.MemoryStorage.Instance
            | StorageType.Postgres -> DbStorage.PgDb()

    let ceResult = CeResultBuilder()
    let inline getLastSnapshot<'A 
        when 'A: (static member Zero: 'A) 
        and 'A: (static member StorageName: string)>() = 
        ceResult {
            let! result =
                match storage.TryGetLastSnapshot 'A.StorageName  with
                | Some (id, eventId, json) ->
                    let state = SnapCache<'A>.Instance.Memoize(fun () -> json |> deserialize<'A>) id
                    match state with
                    | Error e -> Error e
                    | _ -> (eventId, state |> Result.get) |> Ok
                | None -> (0, 'A.Zero) |> Ok
            return result
        }

    let inline getState<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'E :> Event<'A>>() = 
        ceResult {
            let! (id, state) = getLastSnapshot<'A>() 
            let events = storage.GetEventsAfterId id 'A.StorageName
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
        and 'E :> Event<'A>> (command: Command<'A, 'E>)  =
        let lockobj = 
            if Conf.syncobjects.ContainsKey ('A.StorageName) then
                (Conf.syncobjects.['A.StorageName])
            else
                failwith (sprintf "no lock object found %s" 'A.StorageName)
        lock lockobj <| fun () -> 
            ceResult {
                let! (_, state) = getState<'A, 'E>()
                let! events =
                    state
                    |> command.Execute
                let! eventsAdded =
                    storage.AddEvents (events |>> JsonConvert.SerializeObject) 'A.StorageName
                return ()
            }

    let inline runTwoCommands<'A1, 'A2, 'E1, 'E2 
        when 'A1: (static member Zero: 'A1)
        and 'A1: (static member StorageName: string)
        and 'A2: (static member Zero: 'A2)
        and 'A2: (static member StorageName: string)
        and 'E1 :> Event<'A1>
        and 'E2 :> Event<'A2>> 
            (command1: Command<'A1, 'E1>) 
            (command2: Command<'A2, 'E2>) =
            let lockobj1 = 
                if Conf.syncobjects.ContainsKey ('A1.StorageName) then
                    (Conf.syncobjects.['A1.StorageName])
                else
                    failwith (sprintf "no lock object found %s. Please configure it in Conf.fs" 'A1.StorageName)
            let lockobj2 =
                if (Conf.syncobjects.ContainsKey ('A2.StorageName)) then
                    (Conf.syncobjects.['A2.StorageName])
                else
                    failwith (sprintf "no lock object found %s. Please configure it in Conf.fs" 'A2.StorageName)
            lock (lockobj1, lockobj2) <| fun () -> 
                ceResult {
                    let! (_, state1) = getState<'A1, 'E1>()
                    let! (_, state2) = getState<'A2, 'E2>()
                    let! events1 =
                        state1
                        |> command1.Execute
                    let! events2 =
                        state2
                        |> command2.Execute

                    let! eventsAdded =
                        let serEv1 = events1 |>> JsonConvert.SerializeObject
                        let serEv2 = events2 |>> JsonConvert.SerializeObject
                        storage.MultiAddEvents
                            [
                                (serEv1, 'A1.StorageName)
                                (serEv2, 'A2.StorageName)
                            ]
                    return ()
                }

    let inline mksnapshot<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'E :> Event<'A>>() =
        ceResult
            {
                let! (id, state) = getState<'A, 'E>()
                let snapshot = JsonConvert.SerializeObject(state, Utils.serSettings)
                let! result = storage.SetSnapshot (id, snapshot) 'A.StorageName
                return result
            }

    let inline mksnapshotIfInterval<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'E :> Event<'A>>() =
        if (Conf.intervalBetweenSnapshots.ContainsKey 'A.StorageName) then
            ceResult
                {
                    let! lastEventId = storage.TryGetLastEventId 'A.StorageName |> optionToResult
                    let snapEventId = storage.TryGetLastSnapshotEventId 'A.StorageName |> optionToDefault 0
                    let! result =
                        if ((lastEventId - snapEventId) > Conf.intervalBetweenSnapshots.['A.StorageName] || snapEventId = 0) then
                            mksnapshot<'A, 'E>()
                        else
                            () |> Ok
                    return result
                }
        else
            failwith "please set intervalBetweenSnapshots of this aggregate in Conf.fs"

