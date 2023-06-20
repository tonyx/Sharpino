namespace Sharpino

open System.Runtime.CompilerServices
open FSharp.Data.Sql
open FSharp.Core
open FSharpPlus
open FSharpPlus.Data

open Sharpino
open Sharpino.Utils
open Sharpino.Cache
open Sharpino.Core
open FsToolkit.ErrorHandling

module Repository =
    let inline private getLastSnapshot<'A 
        when 'A: (static member Zero: 'A) 
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)>
        (storage: IStorage) = 
            async {
                return
                    ResultCE.result {
                        let! result =
                            match storage.TryGetLastSnapshot 'A.Version 'A.StorageName  with
                            | Some (snapId, eventId, json) ->
                                let state = SnapCache<'A>.Instance.Memoize (fun () -> json |> deserialize<'A>) snapId
                                match state with
                                | Error e -> Error e
                                | _ -> (eventId, state |> Result.get) |> Ok
                            | None -> (0, 'A.Zero) |> Ok
                        return result
                    }
            } 
            |> Async.RunSynchronously


    let inline getState<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'E :> Event<'A>>(storage: IStorage) = 

        let snapIdStateAndEvents()  =
            async {
                return
                    ResultCE.result {
                        let! (id, state) = getLastSnapshot<'A> storage
                        let events = storage.GetEventsAfterId 'A.Version id 'A.StorageName
                        let result =
                            (id, state, events)
                        return result
                    }
            }
            |> Async.RunSynchronously

        let eventuallyFromCache = 
            fun () ->
                ResultCE.result {
                    let! (lastSnapshotId, state, events) = snapIdStateAndEvents()
                    let lastEventId =
                        match events.Length with
                        | x when x > 0 -> events |> List.last |> fst
                        | _ -> lastSnapshotId 
                    let! events' =
                        events |>> snd |> catchErrors deserialize<'E>
                    let! result =
                        events' |> evolve<'A, 'E> state
                    return (lastEventId, result)
                }
        let lastEventId = 
            async {
                return storage.TryGetLastEventId 'A.Version 'A.StorageName |> Option.defaultValue 0
            } 
            |> Async.RunSynchronously
        StateCache<'A>.Instance.Memoize (fun () -> eventuallyFromCache()) lastEventId

    let inline runCommand<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'E :> Event<'A>> (storage: IStorage) (mycommand: Command<'A, 'E>)  =

        async {
            return
                ResultCE.result {
                    let! (_, state) = getState<'A, 'E> storage
                    let! events =
                        state
                        |> mycommand.Execute
                    let! eventsAdded' =
                        storage.AddEvents 'A.Version (events |>> (fun x -> Utils.serialize x)) 'A.StorageName
                    return ()
                } 
        }
        |> Async.RunSynchronously

    let inline runTwoCommands<'A1, 'A2, 'E1, 'E2 
        when 'A1: (static member Zero: 'A1)
        and 'A1: (static member StorageName: string)
        and 'A2: (static member Zero: 'A2)
        and 'A2: (static member StorageName: string)
        and 'A1: (static member Version: string)
        and 'A2: (static member Version: string)
        and 'E1 :> Event<'A1>
        and 'E2 :> Event<'A2>> 
            (storage: IStorage)
            (command1: Command<'A1, 'E1>) 
            (command2: Command<'A2, 'E2>) =

            async {
                return
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
            }
            |> Async.RunSynchronously

    let inline private mksnapshot<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'E :> Event<'A>> (storage: IStorage) =
            async {
                return
                    ResultCE.result
                        {
                            let! (id, state) = getState<'A, 'E> storage
                            let snapshot = Utils.serialize<'A> state
                            let! result = storage.SetSnapshot 'A.Version (id, snapshot) 'A.StorageName
                            return result
                        }
            }
            |> Async.RunSynchronously

    let inline mkSnapshotIfInterval<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (static member SnapshotsInterval : int)
        and 'E :> Event<'A>>(storage: IStorage) =
            async {
                return
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
            }    
            |> Async.RunSynchronously   

    type UnitResult =  ((unit -> Result<unit, string>) * AsyncReplyChannel<Result<unit,string>>)

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
