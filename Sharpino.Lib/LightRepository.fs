
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

    let inline updateState<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'E :> Event<'A>> (storage: EventStoreBridge)  =

        printf "updateState XXX\n"

        // let cons = 
        //     async {
        //         let! con = storage.ConsumeEvents ('A.Version, 'A.StorageName) |> Async.AwaitTask
        //         return con
        //     }
        //     |> Async.StartImmediateAsTask

        let consumed =
            async {
                let! consumed = storage.ConsumeEvents('A.Version, 'A.StorageName) |> Async.AwaitTask
                printf "consumedQ: %A X\n" consumed
                return consumed
            }
            |> Async.StartImmediateAsTask
            |> Async.AwaitTask
            |> Async.RunSynchronously

        let events =
            async {
                // let! consumed = storage.ConsumeEvents('A.Version, 'A.StorageName) |> Async.AwaitTask
                printf "after consumption\n"
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
                        let state = CurrentState<'A>.Instance.Lookup('A.StorageName, 'A.Zero) :?> 'A
                        let! newState = events |> evolve state
                        return newState
                    }
            }
            |> Async.RunSynchronously
        let newStateVal: 'A = (newState |> Result.get)
        CurrentState<'A>.Instance.Update('A.StorageName, newStateVal)
        ()
    let inline runCommand<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'E :> Event<'A>> (storage: EventStoreBridge) (command: Command<'A, 'E>)  =

        let addingEvents =
            async {
                return
                    ResultCE.result {
                        let state = CurrentState<'A>.Instance.Lookup('A.StorageName, 'A.Zero) :?> 'A
                        let! events =
                            state
                            |> command.Execute
                        let serEvents = events |> List.map (fun x -> Utils.serialize x) |> System.Collections.Generic.List
                        let! eventsAdded' =
                            try 
                                storage.AddEvents ('A.Version, serEvents, 'A.StorageName)  |> Ok
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


            

            

