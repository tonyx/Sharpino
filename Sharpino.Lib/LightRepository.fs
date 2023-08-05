
namespace Sharpino

open System.Runtime.CompilerServices
open FSharp.Data.Sql
open FSharp.Core
open FSharpPlus
open FSharpPlus.Data

open Sharpino
open Sharpino.Lib.EvStore
open Sharpino.Utils
open Sharpino.Cache
open Sharpino.Core
open FsToolkit.ErrorHandling

module LightRepository =

    // probably we are not going to use snapshot in the light version
    let inline private getLastSnapshot<'A 
        when 'A: (static member Zero: 'A) 
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)>
        (storage: EventStoreBridge) = 
            async {
                let! (st: Sharpino.Lib.EvStore.Option<struct (int64 * string)>) = 
                    storage.ConsumeSnapshots ('A.Version, 'A.StorageName) |> Async.AwaitTask
                match st.HasValue with
                    | true ->  
                        let struct(id, snapshot) = st.Value
                        let deser = snapshot |> Utils.deserialize<'A>
                        match deser with
                        | Error e -> return Error (e.ToString())
                        | Ok deser -> return (id |> int, deser) |> Ok
                    | false -> return (0, 'A.Zero ) |> Ok
            }
            |> Async.RunSynchronously

    let inline getState<'A
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)>() =
            CurrentState<'A>.Instance.Lookup('A.StorageName, 'A.Zero) :?> 'A

    let inline updateState<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'E :> Event<'A>> (storage: EventStoreBridge)  =

        let consumed =
            async {
                let! consumed = 
                    storage.ConsumeEvents('A.Version, 'A.StorageName) 
                    |> Async.AwaitTask
                return consumed
            }
            |> Async.StartImmediateAsTask
            |> Async.AwaitTask
            |> Async.RunSynchronously

        let events =
            async {
                let events = 
                    consumed 
                    |> Seq.toList 
                    |> List.map 
                        (fun x -> 
                            (System.Text.Encoding.UTF8.GetString(x.Event.Data.ToArray())) 
                            |> Utils.deserialize<'E> |> Result.get
                        )
                return
                    events
            }
            |> Async.RunSynchronously
        
        let newState =
            async {
                return
                    ResultCE.result {
                        let state = getState<'A>()
                        let! newState = events |> evolve state
                        return newState
                    }
            }
            |> Async.RunSynchronously
        let newStateVal: 'A = (newState |> Result.get)
        CurrentState<'A>.Instance.Update('A.StorageName, newStateVal)
        ()

    let inline runUndoCommand<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'E :> Event<'A>> (storage: EventStoreBridge) (undoer: Undoer<'A, 'E>)  =

        let addEvents (events: System.Collections.Generic.List<string>) =
            async {
                let! added = storage.AddEvents ('A.Version, events, 'A.StorageName) |> Async.AwaitTask
                return added
            }
            |> Async.RunSynchronously

        let addingEvents =
            async {
                return
                    ResultCE.result {
                        let state = getState<'A>()
                        let! events =
                            state
                            |> undoer 
                        let serEvents = events |> List.map (fun x -> Utils.serialize x) |> System.Collections.Generic.List
                        let! eventsAdded' =
                            try 
                                addEvents serEvents |> Ok
                            with
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
        and 'E :> Event<'A>> (storage: EventStoreBridge) (command: Command<'A, 'E>)  =

        let addEvents (events: System.Collections.Generic.List<string>) =
            async {
                let! added = storage.AddEvents ('A.Version, events, 'A.StorageName) |> Async.AwaitTask
                return added
            }
            |> Async.RunSynchronously

        let addingEvents =
            async {
                return
                    ResultCE.result {
                        let state = getState<'A>()
                        let! events =
                            state
                            |> command.Execute
                        let serEvents = 
                            events 
                            |> List.map (fun x -> Utils.serialize x) 
                            |> System.Collections.Generic.List
                        let! eventsAdded' =
                            try 
                                addEvents serEvents |> Ok
                            with
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
            (storage: EventStoreBridge)
            (command1: Command<'A1, 'E1>) 
            (command2: Command<'A2, 'E2>) =
            ResultCE.result {
                let a1State = getState<'A1>() 

                let command1Undoer = 
                    match command1.Undo with
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
                    runUndoCommand storage undoer |> ignore
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
            (storage: EventStoreBridge)
            (command1: Command<'A1, 'E1>) 
            (command2: Command<'A2, 'E2>) =
            ResultCE.result {
                let a1State = getState<'A1>()
                let command1Undoer = 
                    match command1.Undo with
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
                    runUndoCommand storage undoer
                    ()
                | _ -> ()

                let! result2' = result2
                return ()
            }

    type UnitResult = ((unit -> Result<unit, string>) * AsyncReplyChannel<Result<unit,string>>)

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

