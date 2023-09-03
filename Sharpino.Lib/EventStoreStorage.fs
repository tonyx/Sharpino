namespace Sharpino

open FsToolkit.ErrorHandling
open FSharp.Data.Sql
open FSharpPlus

open System
open System.Linq
open System.Text
open EventStore.Client

open Sharpino
open Sharpino.Storage

// experimental support for EventStore
module EventStore =
    type EventStoreStorage(connection) =
        let _client = new EventStoreClient(EventStoreClientSettings.Create(connection))

        interface ILightStorage with
            member this.ResetEvents version name =
                let strExists = 
                    _client.ReadStreamAsync(
                        Direction.Forwards,
                        "events" + version + name, 
                        StreamPosition.Start,
                        1) 
            
                async {
                    let! readState = strExists.ReadState |> Async.AwaitTask
                    if readState = ReadState.StreamNotFound |> not then
                        let! _ =  _client.DeleteAsync("events" + version + name, StreamState.Any) |> Async.AwaitTask
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

                    if readState = ReadState.StreamNotFound |> not then
                        let! _ =  _client.DeleteAsync("snapshots" + version + name, StreamState.Any) |> Async.AwaitTask
                        return ()

                }
                |> Async.RunSynchronously

            member this.AddEvents version (events: List<string>) name =
                try
                    let streamName = "events" + version + name
                    let eventData = 
                        events 
                        |>> 
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
                    |> Ok
                with
                | ex -> Error(ex.Message)

            member this.AddSnapshot (eventId: UInt64) (version: string) (snapshot: string) (name: string) =
                let streamName = "snapshots" + version + name
                let eventData = 
                    EventData(
                        Uuid.NewUuid(), 
                        streamName,
                        Encoding.UTF8.GetBytes(snapshot),
                        new Nullable<ReadOnlyMemory<byte>>(Encoding.UTF8.GetBytes(eventId.ToString()))
                    )
                async {
                    let! _ = _client.AppendToStreamAsync(streamName, StreamState.Any, [eventData]) |> Async.AwaitTask
                    return ()
                }
                |> Async.RunSynchronously

            member this.ConsumeEventsFromPosition version name id =
                let streamName = "events" + version + name
                let position = new StreamPosition(id)

                async {
                    let events = _client.ReadStreamAsync(Direction.Forwards, streamName, position.Next())

                    let! readState = events.ReadState |> Async.AwaitTask
                    if readState = ReadState.StreamNotFound then
                        return [] |> Collections.Generic.List
                    else
                        let! ev = events.ToListAsync().AsTask() |> Async.AwaitTask
                        return ev
                }
                |> Async.RunSynchronously
                |>> (fun e -> (e.OriginalEventNumber.ToUInt64(), Encoding.UTF8.GetString(e.Event.Data.ToArray()))) |> List.ofSeq

            member this.TryGetLastSnapshot version name =            
                let streamName = "snapshots" + version + name
                let snapshotVals =
                    async {
                        let snapshots = _client.ReadStreamAsync(Direction.Backwards, streamName, StreamPosition.End)
                        let! readState = snapshots.ReadState |> Async.AwaitTask
                        if readState = ReadState.StreamNotFound then
                            return [] |> Collections.Generic.List
                        else
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
                    (eventId, snapshotData) |> Some

            
