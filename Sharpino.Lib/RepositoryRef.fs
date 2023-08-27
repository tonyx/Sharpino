
namespace Sharpino

open FSharp.Data.Sql
open FSharp.Core
open FSharpPlus
open FSharpPlus.Data

open Sharpino
open Sharpino.Utils
open Sharpino.Cache
open Sharpino.Core
open FsToolkit.ErrorHandling
open Sharpino.Storage

module RepositoryRef =
    let inline private getLastSnapshot<'A 
        when 'A: (static member Zero: 'A) 
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)>
        (storage: IStorageRefactor) =
            async {
                return
                    ResultCE.result {
                        let! result =
                            match storage.TryGetLastSnapshot 'A.Version 'A.StorageName  with
                            | Some (snapId, eventId, json) ->
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
        and 'E :> Event<'A>>(storage: IStorageRefactor) = 
            
        async {
            return
                ResultCE.result {
                    printf "snapIdStateAndEvents 100\n"
                    let! (id, state) = getLastSnapshot<'A> storage
                    printf "snapIdStateAndEvents 110\n"
                    let events = storage.GetEventsAfterId<'E> 'A.Version id 'A.StorageName
                    printf "snapIdStateAndEvents 120\n"
                    let result =
                        (id, state, events)
                    printf "snapIdStateAndEvents 130\n"
                    return result
                }
        }
        |> Async.RunSynchronously
    let inline getState<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'E :> Event<'A>>(storage: IStorageRefactor): Result< int * 'A, string> = 

            let eventuallyFromCache =
                fun () ->
                    printf "get state 100\n"
                    ResultCE.result {
                        printf "get state 120\n"
                        let! (lastSnapshotId, state, events) = snapIdStateAndEvents<'A, 'E> storage
                        printf "get state 130\n"
                        let lastEventId =
                            match events.Length with
                            | x when x > 0 -> events |> List.last |> fst
                            | _ -> lastSnapshotId 
                        printf "get state 140\n"
                        let result =
                            (events |>> snd) |> evolve<'A, 'E> state
                        printf "get state 150\n"
                        
                        // todo: get rid of result.get
                        return (lastEventId, result |> Result.get)
                    }
            let lastEventId =
                async  {
                    return storage.TryGetLastEventId 'A.Version 'A.StorageName |> Option.defaultValue 0
                }
                |> Async.RunSynchronously
            eventuallyFromCache()
        // StateCache<'A>.Instance.Memoize (fun () -> eventuallyFromCache()) (lastEventId, 'A.StorageName)

    let inline runCommand<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'E :> Event<'A>>(storage: IStorageRefactor) (command: Command<'A, 'E>) =
            async {
                return
                    ResultCE.result {
                        let! (_, state) = getState<'A, 'E> storage
                        let! events =
                            state
                            |> command.Execute
                        let! eventsAdded' =
                            // storage.AddEvents 'A.Version (events |>> (fun x -> Utils.serialize x)) 'A.StorageName
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
            (storage: IStorageRefactor)
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
        and 'E :> Event<'A>> (storage: IStorageRefactor) =
            async {
                return
                    ResultCE.result
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
        and 'E :> Event<'A>>(storage: IStorageRefactor) =
            async {
                return
                    ResultCE.result
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