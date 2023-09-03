
namespace Sharpino

open FSharp.Core
open FSharpPlus
open FSharpPlus.Data

open Sharpino
open Sharpino.Storage
open Sharpino.Core
open FsToolkit.ErrorHandling

module LightRepository =

    // todo: remember to get rid of static Utils.serialize and use JsonSerializer instance instead
    let inline private getLastSnapshot<'A 
        when 'A: (static member Zero: 'A) 
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)>
        (storage: ILightStorage) =
            async {
                return
                    result {
                        let! result =
                            match storage.TryGetLastSnapshot 'A.Version 'A.StorageName  with
                            | Some (eventId, json) ->
                                let fromJson = Utils.deserialize<'A> json
                                match fromJson with
                                | Ok x -> (eventId, x) |> Ok
                                | Error e -> Error e
                            | None -> (0 |> uint64, 'A.Zero) |> Ok
                        return result
                    }
            }
            |> Async.RunSynchronously

    let inline snapIdStateAndEvents<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'E :> Event<'A>>(storage: ILightStorage) = 
            
        async {
            return
                result {
                    let! (id, state) = getLastSnapshot<'A> storage
                    let events = storage.ConsumeEventsFromPosition 'A.Version 'A.StorageName id
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
        and 'E :> Event<'A>>(storage: ILightStorage) = 
            result {
                let! (lastSnapshotId, state, events) = snapIdStateAndEvents<'A, 'E> storage
                let lastEventId =
                    match events.Length with
                    | x when x > 0 -> events |> List.last |> fst
                    | _ -> lastSnapshotId 

                let! deserEvents = 
                    events |>> snd
                    |> Utils.catchErrors (fun x -> Utils.deserialize<'E> x)

                let! newState = 
                    deserEvents |> evolve<'A, 'E> state

                return (lastEventId, newState) 

            }

    let inline runUndoCommand<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'E :> Event<'A>> (storage: ILightStorage) (undoer: Undoer<'A, 'E>)  =

        async {
            return
                ResultCE.result {
                    let! (_, state) = getState<'A, 'E> storage
                    let! events =
                        state
                        |> undoer 
                    let serEvents = events |> List.map (fun x -> Utils.serialize x)
                    let! _ = storage.AddEvents 'A.Version serEvents 'A.StorageName 
                    return ()
                } 
        }
        |> Async.RunSynchronously

    let inline runCommand<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'E :> Event<'A>> (storage: ILightStorage) (command: Command<'A, 'E>)  =
        async {
            return
                ResultCE.result {
                    let! (_, state) = getState<'A, 'E> storage
                    let! events =
                        state
                        |> command.Execute
                    let serEvents = 
                        events 
                        |> List.map (fun x -> Utils.serialize x) 
                    let! _ = storage.AddEvents 'A.Version serEvents 'A.StorageName
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
            (storage: ILightStorage)
            (command1: Command<'A1, 'E1>) 
            (command2: Command<'A2, 'E2>) =
            result {
                let! (_, a1State) = getState<'A1, 'E1> storage 

                let command1Undoer = 
                    match command1.Undoer with
                    | Some f -> a1State |> f |> Some 
                    | _ -> None

                let command1Undoer2 = 
                    match command1Undoer with
                    | Some x -> 
                        match x with
                        | Ok x -> Some x
                        | _ -> None
                    | _ -> None

                let! result1 = runCommand<'A1, 'E1> storage command1
                let result2 = runCommand<'A2, 'E2> storage command2

                match result2, command1Undoer2 with
                | Error _, Some undoer ->
                    let doUndo = runUndoCommand storage undoer
                    match doUndo with
                    | Ok _ -> ()
                    | Error err -> 
                        printf "warning can't do undo: %A\n" err
                | _ -> ()
                let! result2' = result2
                return result2'
            }

    // this is the same as runTwoCommands but with a failure in the second command to test the undo
    let inline runTwoCommandsWithFailure_USE_IT_ONLY_TO_TEST_THE_UNDO<'A1, 'A2, 'E1, 'E2 
        when 'A1: (static member Zero: 'A1)
        and 'A1: (static member StorageName: string)
        and 'A2: (static member Zero: 'A2)
        and 'A2: (static member StorageName: string)
        and 'A1: (static member Version: string)
        and 'A2: (static member Version: string)
        and 'E1 :> Event<'A1>
        and 'E2 :> Event<'A2>> 
            (storage: ILightStorage)
            (command1: Command<'A1, 'E1>) 
            (command2: Command<'A2, 'E2>) =
            ResultCE.result {
                let! (_, a1State) = getState<'A1, 'E1> storage
                let command1Undoer = 
                    match command1.Undoer with
                    | Some f -> a1State |> f |> Some 
                    | _ -> None

                let command1Undoer2 = 
                    match command1Undoer with
                    | Some x -> 
                        match x with
                        | Ok x -> Some x
                        | _ -> None
                    | _ -> None
                
                let! result1 = runCommand<'A1, 'E1> storage command1
                let result2: Result<unit, string> = Error "error"
                
                match result2, command1Undoer2 with
                | Error _, Some undoer ->
                    let doUndo = runUndoCommand storage undoer
                    match doUndo with
                    | Ok _ -> ()
                    | Error err -> 
                        printf "warning can't do undo: %A\n" err
                    ()
                | _ -> ()
                return! result2
            }

    let inline mkSnapshotIfIntervalPassed<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (static member SnapshotsInterval : int)
        and 'E :> Event<'A>> (storage: ILightStorage) =

            let addNewShapshot eventId state = 
                let newSnapshot = state |> Utils.serialize<'A>
                storage.AddSnapshot eventId 'A.Version newSnapshot 'A.StorageName

            async {
                let lastSnapshot = storage.TryGetLastSnapshot 'A.Version 'A.StorageName
                let snapId = 
                    if lastSnapshot.IsSome then
                        let (id, _) = lastSnapshot.Value
                        id
                    else    
                        0 |> uint64

                let eventIdAndState = getState<'A, 'E> storage
                match eventIdAndState with
                | Error err -> 
                    printf "warning: %A\n" err
                    return ()
                | Ok (eventId, state) ->
                    let difference = eventId - snapId
                    if (difference > ('A.SnapshotsInterval |> uint64)) then
                        let _ = addNewShapshot eventId state 
                        ()

                    return ()
            }
            |> Async.RunSynchronously


    type UnitResult = ((unit -> Result<unit, string>) * AsyncReplyChannel<Result<unit,string>>)

    // by using this light processor we process the events in a single thread. It is not always needed but at the moment we stick to it
    // note: I guess this is too strict, and it is almost always _not_ needed. Keep it for now but should not use it.
    let lightProcessor = MailboxProcessor<UnitResult>.Start (fun inbox  ->
        let rec loop() =
            async {
                let! (statement, replyChannel) = inbox.Receive()
                let result = statement()
                replyChannel.Reply result
                do! loop()
            }
        loop()
    )

