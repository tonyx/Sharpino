
namespace Sharpino

open FSharp.Data.Sql
open FSharp.Core
open FSharpPlus
open FSharpPlus.Data

open Sharpino
open Sharpino.Storage
open Sharpino.Cache
open Sharpino.Core
open FsToolkit.ErrorHandling

module LightRepository =

    // todo: remember to get rid of static Utils.serialize and use JsonSerializer instance instead
    let inline getState<'A
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)> (storage: ILightStorage) =

            let eventualSnapshot = fun () -> storage.GetLastSnapshot 'A.Version 'A.StorageName |> Option.map (fun (id, value) -> (id |> string|> System.UInt64.Parse, value |> Utils.deserialize<'A> |> Result.get :> obj))

            let (eventId, stateX) = CurrentState<'A>.Instance.Lookup('A.StorageName, ((0 |> uint64),'A.Zero)) eventualSnapshot
            let state' = stateX :?> 'A
            (eventId, state')

    let inline updateState<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'E :> Event<'A>> (storage: ILightStorage)  =
            let newState =
                result {
                    let (eventId, state) = getState<'A> storage
                    let consumed = storage.ConsumeEventsFromPosition 'A.Version 'A.StorageName eventId
                    let! idAndEvents =
                        consumed 
                        |> Utils.catchErrors
                            (fun (i, x) -> 
                                match x |> Utils.deserialize<'E> with
                                | Ok x -> (i, x) |> Ok
                                | Error e -> Error e
                            )

                    let! newState = (idAndEvents |>> snd) |> evolve state
                    let _ =
                        if (idAndEvents.Length > 0) then
                            let lastEventId = idAndEvents |>> fst |> List.last 
                            CurrentState<'A>.Instance.Update('A.StorageName, (lastEventId, newState))
                    return newState
                }
            match newState with
            | Ok _ -> ()
            | Error x -> 
                failwith (sprintf "error updating state: %A\n" x)

    let inline runUndoCommand<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'E :> Event<'A>> (storage: ILightStorage) (undoer: Undoer<'A, 'E>)  =

        let addEvents (events: List<string>) =
            storage.AddEvents 'A.Version events 'A.StorageName

        let addingEvents =
            async {
                return
                    ResultCE.result {
                        let (_, state) = getState<'A> storage
                        let! events =
                            state
                            |> undoer 
                        let serEvents = events |> List.map (fun x -> Utils.serialize x)
                        let! eventsAdded' =
                            try 
                                addEvents serEvents |> Ok
                            with
                            // todo: no possible error for now
                            _ as e -> Error (sprintf "%s %A" "Error adding events to storage" e)
                        return ()
                    } 
            }
            |> Async.RunSynchronously

        let updatingState =
            async {
                return
                    ResultCE.result {
                        let _ = updateState<'A, 'E> storage
                        return ()
                    }
            }
            |> Async.RunSynchronously

        addingEvents

    let inline runCommand<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'E :> Event<'A>> (storage: ILightStorage) (command: Command<'A, 'E>)  =

        let addEvents (events: List<string>) =
            let added = storage.AddEvents 'A.Version events 'A.StorageName
            added

        let addingEvents =
            async {
                return
                    ResultCE.result {
                        let (_, state) = getState<'A> storage
                        let! events =
                            state
                            |> command.Execute
                        let serEvents = 
                            events 
                            |> List.map (fun x -> Utils.serialize x) 
                        let! eventsAdded' =
                            try 
                                addEvents serEvents |> Ok
                            with
                            // todo: no possible error for now
                            _ as e -> Error (sprintf "%s %A" "Error adding events to storage" e)
                        return ()
                    } 
            }
            |> Async.RunSynchronously

        let updatingState =
            async {
                return
                    ResultCE.result {
                        let _ = updateState<'A, 'E> storage
                        return ()
                    }
            }
            |> Async.RunSynchronously

        addingEvents

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
                let (_, a1State) = getState<'A1> storage 

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
                let (_, a1State) = getState<'A1> storage
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

                let! result2' = result2
                return result2'
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
                let lastSnapshot = storage.GetLastSnapshot 'A.Version 'A.StorageName
                let snapId = 
                    if lastSnapshot.IsSome then
                        let (id, value) = lastSnapshot.Value
                        id
                    else    
                        0 |> uint64

                let (eventId, state) = getState<'A> storage
                let difference = eventId - snapId

                if (difference > ('A.SnapshotsInterval |> uint64)) then
                    let _ = addNewShapshot eventId state 
                    ()

                return ()
            }
            |> Async.RunSynchronously


    type UnitResult = ((unit -> Result<unit, string>) * AsyncReplyChannel<Result<unit,string>>)

    // by using this light processor we process the events in a single thread. It is not always needed but at the moment we stick to it
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

