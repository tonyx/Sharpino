namespace Sharpino

open System.Runtime.CompilerServices
open FsToolkit.ErrorHandling
open FSharp.Data.Sql
open Npgsql.FSharp
open FSharpPlus
open Sharpino.Utils
open Sharpino

open System
open System.Linq
open System.Net.Http
open System.Reflection
open System.Text
open System.Threading
open System.Threading.Tasks
open EventStore.Client

module EventStore =
    type EventStoreBridgeFS(connection) =

        let lastEventIds = new Collections.Generic.Dictionary<string, StreamPosition>()
        let _client = new EventStoreClient(EventStoreClientSettings.Create(connection))

        member this.ResetEvents version name =

            let strExists = 
                _client.ReadStreamAsync(
                    Direction.Forwards,
                    "events" + version + name, 
                    StreamPosition.Start,
                    1) 
            
            async {
                let! readState = strExists.ReadState |> Async.AwaitTask
                if readState = ReadState.StreamNotFound then
                    return ()
                else
                    let! _ =  _client.DeleteAsync("events" + version + name, StreamState.Any) |> Async.AwaitTask
                    // let! _ = _client.DeleteAsync("events" + version + name, StreamState.Any) |> Async.AwaitTask
                    return ()
            }
            |> Async.RunSynchronously

        member this.ResetSnapshots version name =
            let strExists = 
                _client.ReadStreamAsync(
                    Direction.Forwards,
                    "snapshots" + version + name, 
                    StreamPosition.Start,
                    1) 
            
            async {
                let! readState = strExists.ReadState |> Async.AwaitTask
                if readState = ReadState.StreamNotFound then
                    return ()
                else
                    let! _ =  _client.DeleteAsync("snapshots" + version + name, StreamState.Any) |> Async.AwaitTask
                    // let! _ = _client.DeleteAsync("snapshots" + version + name, StreamState.Any) |> Async.AwaitTask
                    return ()
            }
            |> Async.RunSynchronously

        member this.AddEvents version (events: List<string>) name =
            let streamName = "events" + version + name
            let eventData = 
                events 
                |> List.map 
                    (fun e -> 
                        EventData(
                            Uuid.NewUuid(), 
                            streamName,
                            Encoding.UTF8.GetBytes(e)
                        )
                    )
            async {
                let! _ = _client.AppendToStreamAsync(streamName, StreamState.Any, eventData) |> Async.AwaitTask
                return ()
            }
            |> Async.RunSynchronously

        member this.AddSnapshot (eventId: UInt64) (version: string) (snapshot: string) (name: string) =
            let streamName = "snapshots" + version + name
            let eventData = 
                EventData(
                    Uuid.NewUuid(), 
                    streamName,
                    Encoding.UTF8.GetBytes(snapshot),
                    new Nullable<ReadOnlyMemory<byte>>(Encoding.UTF8.GetBytes("XXX"))
                )
            async {
                let! _ = _client.AppendToStreamAsync(streamName, StreamState.Any, [eventData]) |> Async.AwaitTask
                return ()
            }
            |> Async.RunSynchronously

        member this.ConsumeEvents version name =    
            try
                // printf "XXXX. consuming 100\n"
                let streamName = "events" + version + name
                let position = 
                    match lastEventIds.TryGetValue(streamName) with
                    | true, pos -> pos
                    | false, _ -> StreamPosition.Start

                // printf "XXXX. consuming 200\n"


                let events = _client.ReadStreamAsync(Direction.Forwards, streamName, position.Next())


                // printf "XXXX. consuming 300\n"


                let eventsReturned =
                    async {
                        let! ev = events.ToListAsync().AsTask() |> Async.AwaitTask
                        return ev
                    }
                    |> Async.RunSynchronously

                // printf "XXXX. consuming 400\n"

                let last = eventsReturned.LastOrDefault()

                // printf "XXXX. consuming 500\n"
                // lastEventIds.[streamName] <- last.Event.EventNumber
                // printf "\n\nXXX. lasteventids %A\n\n\n" lastEventIds

                // printf "XXXX. consuming 600\n"

                if (eventsReturned |> Seq.length > 0) then
                    // printf "last %A\n\n" last.Event.EventNumber
                    lastEventIds.TryAdd(streamName, last.Event.EventNumber) |> ignore
                    // lastEventIds.Add(streamName, last.Event.EventNumber)

                // printf "XXXX. consuming 700\n"
                eventsReturned
            with 
            | _ ->  let ret:List<ResolvedEvent> = []
                    ret |> Collections.Generic.List<ResolvedEvent>

        member this.GetLastSnapshot version name =            
            let streamName = "snapshots" + version + name
            let snapshots = _client.ReadStreamAsync(Direction.Backwards, streamName, StreamPosition.End)
            let snapshotVals =
                async {
                    let! ev = snapshots.ToListAsync().AsTask() |> Async.AwaitTask
                    return ev
                }
                |> Async.RunSynchronously

            if ((snapshotVals |> Seq.length) = 0) then
                None
            else
                let last = snapshotVals.FirstOrDefault()
                let eventId = UInt64.Parse(Encoding.UTF8.GetString(last.Event.Metadata.ToArray()))
                let snapshotData = Encoding.UTF8.GetString(last.Event.Data.ToArray())
                let lastEventId = last.Event.EventNumber
                (eventId, snapshotData) |> Some

            
