
namespace Sharpino

open FSharp.Core
open FSharpPlus
open FSharpPlus.Data

open Sharpino
open Sharpino.Storage
open Sharpino.Core
open FsToolkit.ErrorHandling
open log4net
open log4net.Config

module LightRepository =
    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    // you can configure log here, or in the main program (see tests)

    // todo: remember to get rid of static Utils.serialize and use JsonSerializer instance instead
    let inline private getLastSnapshot<'A 
        when 'A: (static member Zero: 'A) 
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)>
        (storage: ILightStorage) =
            log.Debug "getLastSnapshot"
            async {
                return
                    result {
                        return!
                            match storage.TryGetLastSnapshot 'A.Version 'A.StorageName  with
                            | Some (eventId, json) ->
                                let fromJson = Utils.deserialize<'A> json
                                match fromJson with
                                | Ok x -> (eventId, x) |> Ok
                                | Error e -> Error e
                            | None -> (0 |> uint64, 'A.Zero) |> Ok
                    }
            }
            |> Async.RunSynchronously

    let inline snapIdStateAndEvents<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'E :> Event<'A>>(storage: ILightStorage) = 
        log.Debug "snapIdStateAndEvents"
        async {
            return
                result {
                    let! (id, state) = getLastSnapshot<'A> storage
                    let events = storage.ConsumeEventsFromPosition 'A.Version 'A.StorageName id
                    return (id, state, events)
                }
        }
        |> Async.RunSynchronously

    let inline getState<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'E :> Event<'A>>(storage: ILightStorage) = 
            log.Debug "getState"
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
        log.Debug "runUndoCommand"

        async {
            return
                ResultCE.result {
                    let! (_, state) = storage |> getState<'A, 'E> 
                    let! events =
                        state
                        |> undoer 
                    let serEvents = 
                        events 
                        |>> Utils.serialize
                    return! storage.AddEvents 'A.Version serEvents 'A.StorageName 
                } 
        }
        |> Async.RunSynchronously

    let inline runCommand<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'E :> Event<'A>> (storage: ILightStorage) (command: Command<'A, 'E>)  =
        log.Debug "runCommand"
        async {
            return
                ResultCE.result {
                    let! (_, state) = storage |> getState<'A, 'E>
                    let! events =
                        state
                        |> command.Execute
                    let serEvents = 
                        events 
                        |>> Utils.serialize
                    return! storage.AddEvents 'A.Version serEvents 'A.StorageName
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
            log.Debug (sprintf "runTwoCommands %A %A\n" command1 command2)
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
                return! result2 
            }


    // not tested yet
    let inline runThreeCommands<'A1, 'A2, 'A3, 'E1, 'E2, 'E3
        when 'A1: (static member Zero: 'A1)
        and 'A1: (static member StorageName: string)

        and 'A2: (static member Zero: 'A2)
        and 'A2: (static member StorageName: string)

        and 'A3: (static member Zero: 'A3)
        and 'A3: (static member StorageName: string)

        and 'A1: (static member Version: string)
        and 'A2: (static member Version: string)
        and 'A3: (static member Version: string)

        and 'E1 :> Event<'A1>
        and 'E2 :> Event<'A2>
        and 'E3 :> Event<'A3>> 
            (storage: ILightStorage)
            (command1: Command<'A1, 'E1>) 
            (command2: Command<'A2, 'E2>) 
            (command3: Command<'A3, 'E3>) =
            log.Debug (sprintf "runThreeCommands %A %A %A\n" command1 command2 command3)
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

                let! _ =
                    match result2, command1Undoer2 with
                    | Error _, Some undoer ->
                        let doUndo = runUndoCommand storage undoer
                        match doUndo with
                        | Ok _ -> () |> Ok
                        | Error err -> 
                            printf "warning can't do undo: %A\n" err
                            Error err
                    | _ -> () |> Ok

                let! (_, a2State) = getState<'A2, 'E2> storage 
                let command2Undoer = 
                    match command2.Undoer with
                    | Some f -> a2State |> f |> Some 
                    | _ -> None

                let command2Undoer2 = 
                    match command2Undoer with
                    | Some x -> 
                        match x with
                        | Ok x -> Some x
                        | _ -> None
                    | _ -> None

                let result3 = runCommand<'A3, 'E3> storage command3

                match result3, command1Undoer2, command2Undoer2 with
                | Error _, Some undoer1, Some undoer2 ->
                    let doUndo1 = runUndoCommand storage undoer1
                    let doUndo2 = runUndoCommand storage undoer2
                    match doUndo1, doUndo2 with
                    | Ok _, Ok _ -> ()
                    | Error err, _ -> 
                        printf "warning can't do undo: %A\n" err
                    | _, Error err -> 
                        printf "warning can't do undo: %A\n" err
                | _ -> ()

                return! result3 
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

