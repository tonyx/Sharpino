
namespace Sharpino

open FSharpPlus.Data

open FSharp.Core
open FSharpPlus

open Sharpino.Core
open Sharpino.Storage

open FsToolkit.ErrorHandling

module Repository =
    let inline private getLastSnapshot<'A 
        when 'A: (static member Zero: 'A) 
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)>
        (storage: IStorage) =
            async {
                return
                    result {
                        let! result =
                            match storage.TryGetLastSnapshot 'A.Version 'A.StorageName  with
                            | Some (_, eventId, json) ->
                                (eventId, json ) |> Ok
                            | None -> (0, 'A.Zero) |> Ok
                        return result
                    }
            }
            |> Async.RunSynchronously

    let inline snapIdStateAndEvents<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'E :> Event<'A>>(storage: IStorage) = 
            
        async {
            return
                result {
                    let! (id, state) = getLastSnapshot<'A> storage
                    let events = storage.GetEventsAfterId<'E> 'A.Version id 'A.StorageName
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
        and 'E :> Event<'A>>(storage: IStorage): Result< int * 'A, string> = 

            let eventuallyFromCache =
                fun () ->
                    result {
                        let! (lastSnapshotId, state, events) = snapIdStateAndEvents<'A, 'E> storage
                        let lastEventId =
                            match events.Length with
                            | x when x > 0 -> events |> List.last |> fst
                            | _ -> lastSnapshotId 
                        let result =
                            (events |>> snd) |> evolve<'A, 'E> state

                        let result' =
                            match result with
                            | Ok x -> (lastEventId, x) |> Ok
                            | Error e -> Error e
                        return! result' 
                    }
            eventuallyFromCache()

    let inline runCommand<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'E :> Event<'A>>(storage: IStorage) (command: Command<'A, 'E>) =
            async {
                return
                    result {
                        let! (_, state) = getState<'A, 'E> storage
                        let! events =
                            state
                            |> command.Execute
                        let! eventsAdded' =
                            storage.AddEvents 'A.Version events 'A.StorageName
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
                    result {
                        let! (_, state1) = getState<'A1, 'E1> storage
                        let! (_, state2) = getState<'A2, 'E2> storage
                        let! events1 =
                            state1
                            |> command1.Execute
                        let! events2 =
                            state2
                            |> command2.Execute

                        let events1' = events1 |>> fun x -> x :> obj
                        let events2' = events2 |>> fun x -> x :> obj
                        let! eventAdded =
                            storage.MultiAddEvents 
                                [
                                    (events1', 'A1.Version, 'A1.StorageName)
                                    (events2', 'A2.Version, 'A2.StorageName)
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
                    result
                        {
                            let! (id, state) = getState<'A, 'E> storage
                            let! result = storage.SetSnapshot 'A.Version (id, state) 'A.StorageName
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
                    result
                        {
                            let! lastEventId = 
                                storage.TryGetLastEventId 'A.Version 'A.StorageName 
                                |> Result.ofOption "lastEventId is None"
                            let snapEventId = storage.TryGetLastSnapshotEventId 'A.Version 'A.StorageName |> Option.defaultValue 0
                            let! result =
                                if ((lastEventId - snapEventId)) > 'A.SnapshotsInterval || snapEventId = 0 then
                                    mksnapshot<'A, 'E> storage
                                else
                                    () |> Ok
                            return result
                        }
            }    
            |> Async.RunSynchronously   

    type UnitResult = ((unit -> Result<unit, string>) * AsyncReplyChannel<Result<unit,string>>)

    // todo: remember that using a processor ensure strict single thread processing of commands. 
    // in my example I used it sistematically but to be honest it is rare that we need this strict single thread processing
    // probably I'll with more example relatedo to aggregate level locking or no lock at all (optimistic concurrency)
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