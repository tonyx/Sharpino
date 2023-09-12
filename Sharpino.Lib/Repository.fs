
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
open System.Threading

module Repository =
    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    // no config: uncomment the folloing line for quick conf
    // BasicConfigurator.Configure() |> ignore 

    let inline private getLastSnapshot<'A 
        when 'A: (static member Zero: 'A) 
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)>
        (storage: IStorage) =
            log.Debug "getLastSnapshot"
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
        log.Debug "snapIdStateAndEvents"
        async {
            return
                result {
                    let! (id, state) = getLastSnapshot<'A> storage
                    let! events = storage.GetEventsAfterId<'E> 'A.Version id 'A.StorageName
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
            log.Debug "getState"
            result {
                let! (lastSnapshotId, state, events) = snapIdStateAndEvents<'A, 'E> storage
                let lastEventId =
                    match events.Length with
                    | x when x > 0 -> events |> List.last |> fst
                    | _ -> lastSnapshotId 
                let! newState = 
                    (events |>> snd) |> evolve<'A, 'E> state
                return (lastEventId, newState)
            }

    let inline runCommand<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'E :> Event<'A>>(storage: IStorage) (command: Command<'A, 'E>) =
            log.Debug (sprintf "runCommand %A" command)
            async {
                return
                    result {
                        let! (_, state) = storage |> getState<'A, 'E>
                        let! events =
                            state
                            |> command.Execute
                        return! 
                            storage.AddEvents 'A.Version events 'A.StorageName
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

                        let events1' = events1 |>> fun x -> x :> obj
                        let events2' = events2 |>> fun x -> x :> obj

                        return! 
                            storage.MultiAddEvents 
                                [
                                    (events1', 'A1.Version, 'A1.StorageName)
                                    (events2', 'A2.Version, 'A2.StorageName)
                                ]
                    } 
            }
            |> Async.RunSynchronously

    [<MethodImpl(MethodImplOptions.Synchronized)>]
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