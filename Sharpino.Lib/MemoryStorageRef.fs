
namespace Sharpino

open Sharpino.Storage
open FSharp.Data.Sql
open System.Runtime.CompilerServices
open FSharpPlus
open System
open System.Collections

module MemoryStorageRef =
    type MemoryStorageRef(serializer: Utils.JsonSerializer) = 
        let event_id_seq_dic = new Generic.Dictionary<version, Generic.Dictionary<Name,int>>()
        let snapshot_id_seq_dic = new Generic.Dictionary<version, Generic.Dictionary<Name,int>>()
        let events_dic = new Generic.Dictionary<version, Generic.Dictionary<string, List<StorageEvent>>>()
        let snapshots_dic = new Generic.Dictionary<version, Generic.Dictionary<string, List<StorageSnapshot>>>()

        // member this.foo = "bar"
        [<MethodImpl(MethodImplOptions.Synchronized)>]
        let next_event_id version name =
            let event_id_seq =
                if (event_id_seq_dic.ContainsKey version |> not) || (event_id_seq_dic.[version].ContainsKey name |> not) then
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
                if (snapshot_id_seq_dic.ContainsKey version |> not) || (snapshot_id_seq_dic.[version].ContainsKey name |> not) then
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

        [<MethodImpl(MethodImplOptions.Synchronized)>]
        let storeEvents version name events =
            if (events_dic.ContainsKey version |> not) then
                let dic = new Generic.Dictionary<string, List<StorageEvent>>()
                dic.Add(name, events)
                events_dic.Add(version, dic)
            else
                let dic = events_dic.[version]
                if (dic.ContainsKey name |> not) then
                    dic.Add(name, events)
                else
                    dic.[name] <- events

        [<MethodImpl(MethodImplOptions.Synchronized)>]
        let storeSnapshots version name snapshots =
            if (snapshots_dic.ContainsKey version |> not) then
                let dic = new Generic.Dictionary<string, List<StorageSnapshot>>()
                dic.Add(name, snapshots)
                snapshots_dic.Add(version, dic)
            else
                let dic = snapshots_dic.[version]
                if (dic.ContainsKey name |> not) then
                    dic.Add(name, snapshots)
                else
                    dic.[name] <- snapshots

        let getExistingSnapshots version name =
            if (snapshots_dic.ContainsKey version |> not) || (snapshots_dic.[version].ContainsKey name |> not) then
                []
            else
                snapshots_dic.[version].[name]

        let getExistingEvents version name =
            if (events_dic.ContainsKey version |> not) || (events_dic.[version].ContainsKey name |> not) then
                []
            else
                events_dic.[version].[name]

        interface IStorageRefactor with
            [<MethodImpl(MethodImplOptions.Synchronized)>]
            member this.Reset version name =
                events_dic.Clear()
                snapshots_dic.Clear()
                event_id_seq_dic.Clear()
                snapshot_id_seq_dic.Clear()

            [<MethodImpl(MethodImplOptions.Synchronized)>]
            member this.AddEvents version xs name: Result<unit,string> = 
                let newEvents =
                    [for e in xs do
                        yield {
                            Id = next_event_id version name
                            Event = e |> serializer.Serialize
                            Timestamp = DateTime.Now
                        }
                    ]
                let events = getExistingEvents version name @ newEvents
                storeEvents version name events
                () |> Ok

            member this.GetEventsAfterId<'E> version id name =
                if (events_dic.ContainsKey version |> not) || (events_dic.[version].ContainsKey name |> not) then
                    []
                else
                    events_dic.[version].[name]
                    |> List.filter (fun x -> x.Id > id)
                    |>> (fun x -> x.Id, x.Event |> serializer.Deserialize<'E> |> Result.get)
            member this.MultiAddEvents(arg: List<List<obj> * version * Name>): Result<unit,string> = 
                arg 
                |> List.iter 
                    (fun (xs, version, name) ->
                        (this :> IStorageRefactor).AddEvents version xs name |> ignore
                    ) 
                () |> Ok
            member this.SetSnapshot  version (id, snapshot) name =
                let newSnapshot =
                    {
                        Id = next_snapshot_id version name
                        Snapshot = snapshot |> serializer.Serialize
                        TimeStamp = DateTime.Now
                        EventId = id
                    }

                let snapshots = getExistingSnapshots version name @ [newSnapshot]
                storeSnapshots version name snapshots
                () |> Ok

            member this.TryGetEvent version id name =
                if (events_dic.ContainsKey version |> not) || (events_dic.[version].ContainsKey name |> not) then
                    None
                else
                    let res =
                        events_dic.[version].[name]
                        |> List.tryFind (fun x -> x.Id = id)
                    match res with
                    | None -> None
                    | Some x ->
                        match (serializer.Deserialize x.Event) with
                        | Ok event -> Some { EventRef = event; Id = x.Id; Timestamp = x.Timestamp }
                        | Error e -> failwith e

            member this.TryGetLastEventId  version  name = 
                if (events_dic.ContainsKey version |> not) || (events_dic.[version].ContainsKey name |> not) then
                    None
                else
                    events_dic.[version].[name]
                    |> List.tryLast
                    |>> (fun x -> x.Id)
            member this.TryGetLastSnapshot version name =
                if (snapshots_dic.ContainsKey version |> not)|| (snapshots_dic.[version].ContainsKey name |> not) then
                    None
                else
                    let res =
                        snapshots_dic.[version].[name]
                        |> List.tryLast
                    match res with
                    | None -> None
                    | Some x ->
                        match (serializer.Deserialize x.Snapshot) with
                        | Ok snapshot -> Some (x.Id, x.EventId, snapshot)
                        | Error e -> failwith e

            member this.TryGetLastSnapshotEventId version name =
                if (snapshots_dic.ContainsKey version |> not) || (snapshots_dic.[version].ContainsKey name |> not) then
                    None
                else
                    snapshots_dic.[version].[name]
                    |> List.tryLast
                    |>> (fun x -> x.EventId)
            member this.TryGetLastSnapshotId version name =
                if (snapshots_dic.ContainsKey version |> not) || (snapshots_dic.[version].ContainsKey name |> not) then
                    None
                else
                    snapshots_dic.[version].[name]
                    |> List.tryLast
                    |>> (fun x -> x.Id)

