namespace Tonyx.EventSourcing

open FSharp.Data.Sql
open System.Runtime.CompilerServices
open FSharpPlus
open System
open System.Collections

module MemoryStorage =
    type MemoryStorage () =
        let event_id_seq_dic = new Generic.Dictionary<version, Generic.Dictionary<Name,int>>()
        let snapshot_id_seq_dic = new Generic.Dictionary<version, Generic.Dictionary<Name,int>>()
        let events_dic = new Generic.Dictionary<version, Generic.Dictionary<string, List<StorageEvent>>>()
        let snapshots_dic = new Generic.Dictionary<version, Generic.Dictionary<string, List<StorageSnapshot>>>()

        [<MethodImpl(MethodImplOptions.Synchronized)>]
        let next_event_id version name =
            let event_id_seq =
                if (event_id_seq_dic.ContainsKey version |> not) then
                    1
                else
                    if (event_id_seq_dic.[version].ContainsKey name |> not) then
                        1
                    else
                        event_id_seq_dic.[version].[name]
            if (event_id_seq_dic.ContainsKey version |> not) then
                let dic = new Generic.Dictionary<Name, int>()
                dic.Add(name, event_id_seq + 1)
                event_id_seq_dic.Add(version, dic)
            else
                let dic = event_id_seq_dic.[version]
                if (dic.ContainsKey name |> not) then
                    dic.Add(name, event_id_seq + 1)
                else
                    dic.[name] <- event_id_seq + 1
            event_id_seq

        [<MethodImpl(MethodImplOptions.Synchronized)>]
        let next_snapshot_id version name =
            let snapshot_id_seq =
                if (snapshot_id_seq_dic.ContainsKey version |> not) then
                    1
                else
                    if (snapshot_id_seq_dic.[version].ContainsKey name |> not) then
                        1
                    else
                        snapshot_id_seq_dic.[version].[name]
            if (snapshot_id_seq_dic.ContainsKey version |> not) then
                let dic = new Generic.Dictionary<Name, int>()
                dic.Add(name, snapshot_id_seq + 1)
                snapshot_id_seq_dic.Add(version, dic)
            else
                let dic = snapshot_id_seq_dic.[version]
                if (dic.ContainsKey name |> not) then
                    dic.Add(name, snapshot_id_seq + 1)
                else
                    dic.[name] <- snapshot_id_seq + 1
            snapshot_id_seq

        interface IStorage with
            [<MethodImpl(MethodImplOptions.Synchronized)>]
            member this.Reset version name =
                events_dic.Clear()
                snapshots_dic.Clear()
                event_id_seq_dic.Clear()
                snapshot_id_seq_dic.Clear()

            member this.TryGetLastSnapshot version name =
                let result =
                    if snapshots_dic.ContainsKey version |> not then 
                        None 
                    else
                        if snapshots_dic.[version].ContainsKey name |> not then
                            None
                        else
                            snapshots_dic.[version].[name]
                            |> List.tryLast
                            |>> (fun x -> x.Id, x.EventId, x.Snapshot)
                result

            member this.TryGetLastEventId version name =
                let result =
                    if events_dic.ContainsKey version |> not then 
                        None 
                    else
                        if events_dic.[version].ContainsKey name |> not then
                            None
                        else
                            events_dic.[version].[name]
                            |> List.tryLast
                            |>> (fun x -> x.Id)
                result

            member this.TryGetLastSnapshotEventId version name =
                let result =    
                    if snapshots_dic.ContainsKey version |> not then 
                        None 
                    else
                        if snapshots_dic.[version].ContainsKey name |> not then
                            None
                        else
                            snapshots_dic.[version].[name]
                            |> List.tryLast
                            |>> (fun x -> x.EventId)
                result

            member this.TryGetLastSnapshotId version name =
                if snapshots_dic.ContainsKey version |> not then 
                    None 
                else
                    if snapshots_dic.[version].ContainsKey name |> not then
                        None
                    else
                        snapshots_dic.[version].[name]
                        |> List.tryLast
                        |>> (fun x -> x.Id)

            member this.TryGetEvent version (id: int) name =
                let result =
                    if events_dic.ContainsKey version |> not then 
                        None 
                    else
                        if events_dic.[version].ContainsKey name |> not then
                            None
                        else
                            events_dic.[version].[name]
                            |> List.tryFind (fun x -> x.Id = id)
                result
            member this.AddEvents version xs name =
                let existingEvents =
                    if events_dic.ContainsKey version |> not then 
                        [] 
                    else
                        if (events_dic.[version]).ContainsKey name |> not then
                            []
                        else
                            events_dic.[version].[name]

                let result =
                    existingEvents@
                        [for e in xs do
                            yield {
                                Id = next_event_id version name
                                Event = e
                                Timestamp = DateTime.Now
                            }
                        ]

                if (events_dic.ContainsKey version |> not) then
                    let toAdd = new Generic.Dictionary<Name, List<StorageEvent>>() 
                    toAdd.Add(name, result)
                    events_dic.Add(version, toAdd) |> ignore
                else
                    if (events_dic.[version]).ContainsKey name |> not then
                        let toAdd = new Generic.Dictionary<Name, List<StorageEvent>>() 
                        toAdd.Add(name, result)
                        events_dic.[version].Add(name, result) |> ignore
                    else
                        events_dic.[version].[name] <- result
                () |> Ok

            member this.MultiAddEvents (arg: List<List<Json> * version * Name>) : Result<unit, string> = 
                arg 
                |> List.iter 
                    (fun (xs, version, name) ->
                        (this :> IStorage).AddEvents version xs name |> ignore
                    ) 
                () |> Ok

            member this.GetEventsAfterId version id name =
                if events_dic.ContainsKey version |> not then 
                    [] 
                else
                    if events_dic.[version].ContainsKey name |> not then
                        []
                    else
                        events_dic.[version].[name]
                        |> List.filter (fun x -> x.Id > id)
                        |>> (fun x -> x.Id, x.Event)
            member this.SetSnapshot version (id, snapshot) name =
                let existingSnapshots =
                    if snapshots_dic.ContainsKey version |> not then 
                        [] 
                    else
                        if (snapshots_dic.[version]).ContainsKey name |> not then
                            []
                        else
                            snapshots_dic.[version].[name]
                let newSnapshot =
                    {
                        Id = next_snapshot_id version name
                        Snapshot = snapshot
                        TimeStamp = DateTime.Now
                        EventId = id
                    }
                if snapshots_dic.ContainsKey version then
                    if (snapshots_dic.[version]).ContainsKey name then
                        snapshots_dic.[version].Remove(name) |> ignore
                    snapshots_dic.[version].Add(name, existingSnapshots@[newSnapshot]) |> ignore
                else
                    let toAdd = new Generic.Dictionary<Name, List<StorageSnapshot>>()
                    toAdd.Add(name, existingSnapshots@[newSnapshot])
                    snapshots_dic.Add(version, toAdd) |> ignore
                () |> Ok