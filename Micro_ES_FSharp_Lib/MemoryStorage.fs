namespace Tonyx.EventSourcing

open FSharp.Data.Sql
open System.Runtime.CompilerServices
open FSharpPlus
open System

// memory storage should be used only for testing and developing
module MemoryStorage =
    type MemoryStorage private() =
        let mutable event_id_seq = [] |> Map.ofList
        let mutable snapshot_id_seq = [] |> Map.ofList
        let mutable events: Map<string, List<StorageEvent>> = [] |> Map.ofList
        let mutable snapshots: Map<string, List<StorageSnapshot> > = [] |> Map.ofList
        static let instance = MemoryStorage()
        let getSnapshots name =
            if (snapshots |> Map.containsKey name |> not) then
                []
            else
                snapshots.[name]
        let getEvents name =
            if (events |> Map.containsKey name |> not) then
                []
            else
                events.[name]
        [<MethodImpl(MethodImplOptions.Synchronized)>]
        let next_event_id name =
            if (event_id_seq |> Map.containsKey name |> not) then
                event_id_seq <- event_id_seq.Add(name, 1)
            let result = event_id_seq.[name]
            event_id_seq <- event_id_seq.Add(name, result + 1)
            result

        [<MethodImpl(MethodImplOptions.Synchronized)>]
        let next_snapshot_id name =
            if (snapshot_id_seq |> Map.containsKey name |> not) then
                snapshot_id_seq <- snapshot_id_seq.Add(name, 1)
            let result = snapshot_id_seq.[name]
            snapshot_id_seq <- snapshot_id_seq.Add(name, result + 1)
            result

        static member Instance = instance

        interface IStorage with
            [<MethodImpl(MethodImplOptions.Synchronized)>]
            member this.Reset name =
                events <- events.Add(name, [])
                snapshots <- snapshots.Add(name, [])
                event_id_seq <- event_id_seq.Add(name, 1)
                snapshot_id_seq <- snapshot_id_seq.Add(name, 1)

            member this.TryGetLastSnapshot name =
                name 
                |> getSnapshots 
                |> List.tryLast 
                |>> (fun x -> (x.Id, x.EventId, x.Snapshot))

            member this.TryGetLastEventId name =
                name 
                |> getEvents 
                |> List.tryLast 
                |>> (fun x -> x.Id)
            member this.TryGetLastSnapshotEventId name =
                name 
                |> getSnapshots 
                |> List.tryLast 
                |>> (fun x -> x.EventId)

            member this.TryGetLastSnapshotId name =
                name 
                |> getSnapshots 
                |> List.tryLast 
                |>> (fun x -> x.Id)

            member this.TryGetEvent(id: int) name =
                name |> getEvents  |> List.tryFind (fun x -> x.Id = id)
            member this.AddEvents xs name =
                let ev =
                    (name |> getEvents)
                    @
                    [
                        for e in xs do
                            yield {
                                Id = next_event_id name
                                Event = e
                                Timestamp = DateTime.Now
                            }
                    ]
                events <- events.Add(name, ev)
                () |> Result.Ok

            member this.MultiAddEvents(arg: List<List<Json> * Name>) : Result<unit,string> = 
                arg
                |> List.iter 
                    (fun (xs, name) ->
                        let ev =
                            (name |> getEvents)
                            @
                            [
                                for e in xs do
                                    yield {
                                        Id = next_event_id name
                                        Event = e
                                        Timestamp = DateTime.Now
                                    }
                            ]
                        events <- events.Add(name, ev)
                    )
                () |> Ok

            member this.GetEventsAfterId id name =
                name 
                |> getEvents 
                |> List.filter (fun x -> x.Id > id) 
                |>> fun x -> x.Id, x.Event
            member this.SetSnapshot (id, snapshot) name =
                let newSnapshot =
                    {
                        Id = next_snapshot_id name
                        Snapshot = snapshot
                        TimeStamp = DateTime.Now
                        EventId = id
                    }
                snapshots <- snapshots.Add (name, (name |> getSnapshots)@[newSnapshot])
                () |> Ok