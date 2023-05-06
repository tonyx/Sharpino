namespace Tonyx.EventSourcing

open FSharp.Data.Sql
open System.Runtime.CompilerServices
open FSharpPlus
open System

// memory storage should be used only for testing and developing
module MemoryStorage =
    type MemoryStorage () =
        let mutable event_id_seq': Map<version, Map<Name, obj>> = [] |> Map.ofList
        let mutable event_id_seq = [] |> Map.ofList
        let mutable snapshot_id_seq': Map<version, Map<Name, obj>>  = [] |> Map.ofList
        let mutable snapshot_id_seq = [] |> Map.ofList
        let mutable events': Map<version,Map<string, List<StorageEvent>>> = [] |> Map.ofList
        let mutable events: Map<string, List<StorageEvent>> = [] |> Map.ofList
        let mutable snapshots': Map<string, Map<string, List<StorageSnapshot>>> = [] |> Map.ofList
        let mutable snapshots: Map<string, List<StorageSnapshot> > = [] |> Map.ofList
        let getSnapshots name =
            if (snapshots |> Map.containsKey name |> not) then
                []
            else
                snapshots.[name]
        let getSnapshots' version name =
            if 
                (   
                    snapshots'.ContainsKey version |> not
                    || 
                    snapshots'.[version].ContainsKey name |> not
                ) then
                []
            else
                (snapshots'.[version]).[name]

        let getEvents' version name =
            if 
                (
                    events'.ContainsKey version |> not
                    ||
                    (events'.[version]).ContainsKey name |> not
                ) then
                    []
            else        
                (events'.[version]).[name]

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

        // static member Instance = instance

        interface IStorage with
            [<MethodImpl(MethodImplOptions.Synchronized)>]
            member this.Reset version name =
                events <- events.Add(name, [])
                snapshots <- snapshots.Add(name, [])
                event_id_seq <- event_id_seq.Add(name, 1)
                snapshot_id_seq <- snapshot_id_seq.Add(name, 1)

            member this.TryGetLastSnapshot version name =
                name 
                |> getSnapshots 
                |> List.tryLast 
                |>> (fun x -> (x.Id, x.EventId, x.Snapshot))

            member this.TryGetLastEventId version name =
                name 
                |> getEvents 
                |> List.tryLast 
                |>> (fun x -> x.Id)
            member this.TryGetLastSnapshotEventId version name =
                name 
                |> getSnapshots 
                |> List.tryLast 
                |>> (fun x -> x.EventId)

            member this.TryGetLastSnapshotId version name =
                name 
                |> getSnapshots 
                |> List.tryLast 
                |>> (fun x -> x.Id)

            member this.TryGetEvent version (id: int) name =
                name |> getEvents  |> List.tryFind (fun x -> x.Id = id)
            member this.AddEvents version xs name =
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

            member this.MultiAddEvents (arg: List<List<Json> * version * Name>) : Result<unit,string> = 
                arg
                |> List.iter 
                    (fun (xs, _, name) ->
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

            member this.GetEventsAfterId version id name =
                name 
                |> getEvents 
                |> List.filter (fun x -> x.Id > id) 
                |>> fun x -> x.Id, x.Event
            member this.SetSnapshot version (id, snapshot) name =
                let newSnapshot =
                    {
                        Id = next_snapshot_id name
                        Snapshot = snapshot
                        TimeStamp = DateTime.Now
                        EventId = id
                    }
                snapshots <- snapshots.Add (name, (name |> getSnapshots)@[newSnapshot])
                () |> Ok