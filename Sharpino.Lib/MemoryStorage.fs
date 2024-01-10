
namespace Sharpino

open System.Runtime.CompilerServices
open FSharpPlus
open FSharpPlus.Operators
open System
open System.Collections
open Sharpino.Storage
open Sharpino.Definitions

open log4net
open log4net.Config

// should be called like InMemoryEventStore 
module MemoryStorage =
    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    // enable for quick debugging
    // log4net.Config.BasicConfigurator.Configure() |> ignore

    type MemoryStorage() =
        let event_id_seq_dic = new Generic.Dictionary<Version, Generic.Dictionary<Name,int>>()
        let snapshot_id_seq_dic = new Generic.Dictionary<Version, Generic.Dictionary<Name,int>>()
        let events_dic = new Generic.Dictionary<Version, Generic.Dictionary<string, List<StorageEventJson>>>()
        let snapshots_dic = new Generic.Dictionary<Version, Generic.Dictionary<string, List<StorageSnapshot>>>()
        [<MethodImpl(MethodImplOptions.Synchronized)>]
        let next_event_id version name =
            log.Debug (sprintf "next_event_id %s %s" version name)
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
            log.Debug (sprintf "next_snapshot_id %s %s" version name)

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
            log.Debug (sprintf "storeEvents %s %s" version name)
            if (events_dic.ContainsKey version |> not) then
                let dic = new Generic.Dictionary<string, List<StorageEventJson>>()
                dic.Add(name, events)
                events_dic.Add(version, dic)
            else
                let dic = events_dic.[version]
                if (dic.ContainsKey name |> not) then
                    dic.Add(name, events)
                else
                    dic.[name] <- events

        [<MethodImpl(MethodImplOptions.Synchronized)>]
        let storeEvents' version name events =
            log.Debug (sprintf "storeEvents %s %s" version name)
            if (events_dic.ContainsKey version |> not) then
                let dic = new Generic.Dictionary<string, List<StorageEventJson>>()
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
            log.Debug (sprintf "storeSnapshots %s %s" version name)
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
            log.Debug (sprintf "getExistingSnapshots %s %s" version name)
            if (snapshots_dic.ContainsKey version |> not) || (snapshots_dic.[version].ContainsKey name |> not) then
                []
            else
                snapshots_dic.[version].[name]

        let getExistingEvents version name =
            log.Debug (sprintf "getExistingEvents %s %s" version name)
            if (events_dic.ContainsKey version |> not) || (events_dic.[version].ContainsKey name |> not) then
                []
            else
                events_dic.[version].[name]

        [<MethodImpl(MethodImplOptions.Synchronized)>]
        let storeSnapshots version name snapshots =
            log.Debug (sprintf "storeSnapshots %s %s" version name)
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
            log.Debug (sprintf "getExistingSnapshots %s %s" version name)
            if (snapshots_dic.ContainsKey version |> not) || (snapshots_dic.[version].ContainsKey name |> not) then
                []
            else
                snapshots_dic.[version].[name]

        let getExistingEvents version name =
            log.Debug (sprintf "getExistingEvents %s %s" version name)
            if (events_dic.ContainsKey version |> not) || (events_dic.[version].ContainsKey name |> not) then
                []
            else
                events_dic.[version].[name]

        interface IEventStore with
            [<MethodImpl(MethodImplOptions.Synchronized)>]
            member this.Reset version name =
                log.Debug (sprintf "Reset %s %s" version name)
                events_dic.Clear()
                snapshots_dic.Clear()
                event_id_seq_dic.Clear()
                snapshot_id_seq_dic.Clear()

            [<MethodImpl(MethodImplOptions.Synchronized)>]
            member this.AddEvents version name xs: Result<List<int>, string> = 
                log.Debug (sprintf "AddEvents %s %s" version name)
                let newEvents =
                    [for e in xs do
                        yield {
                            Id = next_event_id version name
                            JsonEvent = e
                            KafkaOffset = None
                            KafkaPartition = None 
                            Timestamp = DateTime.Now
                        }
                    ]
                let events = getExistingEvents version name @ newEvents
                storeEvents version name events
                let ids = newEvents |> List.map (fun x -> x.Id)
                ids |> Ok

            member this.GetEventsAfterId version id name =
                log.Debug (sprintf "GetEventsAfterId %s %A %s" version id name)
                if (events_dic.ContainsKey version |> not) || (events_dic.[version].ContainsKey name |> not) then
                    [] |> Ok
                else
                    events_dic.[version].[name]
                    |> List.filter (fun x -> x.Id > id)
                    |>> (fun x -> x.Id, x.JsonEvent)
                    |> Ok
            member this.MultiAddEvents (arg: List<List<Json> * Version * Name>) =
                log.Debug (sprintf "MultiAddEvents %A" arg)
                let cmds =
                    arg 
                    |> List.map 
                        (fun (xs, version, name) ->
                            (this :> IEventStore).AddEvents version name xs |> Result.get
                        ) 
                cmds |> Ok

            member this.SetSnapshot  version (id, snapshot) name =
                log.Debug (sprintf "SetSnapshot %s %A %s" version id name)
                let newSnapshot =
                    {
                        Id = next_snapshot_id version name
                        Snapshot = snapshot 
                        TimeStamp = DateTime.Now
                        EventId = id
                    }

                let snapshots = getExistingSnapshots version name @ [newSnapshot]
                storeSnapshots version name snapshots
                () |> Ok

            member this.TryGetEvent version id name =
                log.Debug (sprintf "TryGetEvent %s %A %s" version id name)
                if (events_dic.ContainsKey version |> not) || (events_dic.[version].ContainsKey name |> not) then
                    None
                else
                    let res =
                        events_dic.[version].[name]
                        |> List.tryFind (fun x -> x.Id = id)
                    res

            member this.TryGetLastEventId  version  name = 
                log.Debug (sprintf "TryGetLastEventId %s %s" version name)
                if (events_dic.ContainsKey version |> not) || (events_dic.[version].ContainsKey name |> not) then
                    None
                else
                    events_dic.[version].[name]
                    |> List.tryLast
                    |>> (fun x -> x.Id)
            member this.TryGetLastSnapshot version name =
                log.Debug (sprintf "TryGetLastSnapshot %s %s" version name)
                if (snapshots_dic.ContainsKey version |> not)|| (snapshots_dic.[version].ContainsKey name |> not) then
                    None
                else
                    let res =
                        snapshots_dic.[version].[name]
                        |> List.tryLast
                    match res with
                    | None -> None
                    | Some x ->
                        Some (x.Id, x.EventId, x.Snapshot)

            member this.TryGetLastSnapshotEventId version name =
                log.Debug (sprintf "TryGetLastSnapshotEventId %s %s" version name)
                if (snapshots_dic.ContainsKey version |> not) || (snapshots_dic.[version].ContainsKey name |> not) then
                    None
                else
                    snapshots_dic.[version].[name]
                    |> List.tryLast
                    |>> (fun x -> x.EventId)
            member this.TryGetLastSnapshotId version name =
                log.Debug (sprintf "TryGetLastSnapshotId %s %s" version name)
                if (snapshots_dic.ContainsKey version |> not) || (snapshots_dic.[version].ContainsKey name |> not) then
                    None
                else
                    snapshots_dic.[version].[name]
                    |> List.tryLast
                    |>> (fun x -> x.EventId, x.Id)

            member this.TryGetSnapshotById version name id =
                log.Debug (sprintf "TryGetSnapshotById %s %s %A" version name id)
                if (snapshots_dic.ContainsKey version |> not) || (snapshots_dic.[version].ContainsKey name |> not) then
                    None
                else
                    snapshots_dic.[version].[name]
                    |> List.tryFind (fun x -> x.Id = id)
                    |>> (fun x -> (x.EventId, x.Snapshot))

            // Issue: it will not survive after a version migration because the timestamps will be different
            member this.GetEventsInATimeInterval (version: Version) (name: Name) (dateFrom: DateTime) (dateTo: DateTime) =
                log.Debug (sprintf "GetEventsInATimeInterval %s %s %A %A" version name dateFrom dateTo)
                if (events_dic.ContainsKey version |> not) || (events_dic.[version].ContainsKey name |> not) then
                    []
                else
                    events_dic.[version].[name]
                    |> List.filter (fun x -> x.Timestamp >= dateFrom && x.Timestamp <= dateTo)
                    |>> (fun x -> x.Id, x.JsonEvent)
                    |>> (fun (id, event) -> id, event)

            member this.SetPublished version name id kafkaOffset partition = 
                if (events_dic.ContainsKey version |> not) || (events_dic.[version].ContainsKey name |> not) then
                    Error (sprintf "not found stream " + version + " " + name)
                else
                    let res =
                        events_dic.[version].[name]
                        |> List.tryFind (fun x -> x.Id = id)
                    match res with
                    | None -> Error (sprintf "element %d not found in stream %s - %s" id version name)
                    | Some x ->
                        events_dic.[version].[name]
                            <- events_dic.[version].[name]
                                |> List.filter (fun x -> x.Id <> id)
                                |> List.append
                                        [
                                            { x with
                                                KafkaOffset = kafkaOffset |> Some
                                                KafkaPartition = partition |> Some }
                                        ]
                        Ok ()

            member this.TryGetLastEventIdWithKafkaOffSet version name  = 
                log.Debug (sprintf "TryGetLastEventIdWithKafkaOffSet %s %s" version name)
                if (events_dic.ContainsKey version |> not) || (events_dic.[version].ContainsKey name |> not) then
                    None
                else
                    events_dic.[version].[name]
                    |> List.tryLast
                    |>> (fun x -> x.Id, x.KafkaOffset, x.KafkaPartition)


