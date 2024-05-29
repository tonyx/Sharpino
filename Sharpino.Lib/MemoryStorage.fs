
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
    // enable following line for debugging
    // log4net.Config.BasicConfigurator.Configure() |> ignore

    type MemoryStorage() =
        let event_id_seq_dic = Generic.Dictionary<Version, Generic.Dictionary<Name,int>>()
        let event_aggregate_id_seq_dic = Generic.Dictionary<Version, Generic.Dictionary<Name,Generic.Dictionary<AggregateId,int>>>()
        let snapshot_id_seq_dic = Generic.Dictionary<Version, Generic.Dictionary<Name,int>>()
        let events_dic = Generic.Dictionary<Version, Generic.Dictionary<string, List<StoragePgEvent<_>>>>()
        let aggregate_events_dic = Generic.Dictionary<Version, Generic.Dictionary<string, Generic.Dictionary<AggregateId, List<StorageEventJsonRef>>>>()
        let snapshots_dic = Generic.Dictionary<Version, Generic.Dictionary<string, List<StorageSnapshot>>>()
        let aggregate_snapshots_dic = Generic.Dictionary<Version, Generic.Dictionary<Name, Generic.Dictionary<AggregateId, List<StorageAggregateSnapshot>>>>()
        [<MethodImpl(MethodImplOptions.Synchronized)>]
        let next_event_id version name =
            log.Debug (sprintf "next_event_id %s %s" version name)
            let event_id_seq =
                if (event_id_seq_dic.ContainsKey version |> not) || (event_id_seq_dic.[version].ContainsKey name |> not) then
                    1
                else
                    event_id_seq_dic.[version].[name]

            if (event_id_seq_dic.ContainsKey version |> not) then
                let dic = Generic.Dictionary<Name, int>()
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
        let next_aggregate_event_id version name aggregateId =
            log.Debug (sprintf "next_aggregate_event_id %s %s %A" version name aggregateId)
            let event_id_seq =
                if (event_aggregate_id_seq_dic.ContainsKey version |> not) || (event_aggregate_id_seq_dic.[version].ContainsKey name |> not) || (event_aggregate_id_seq_dic.[version].[name].ContainsKey aggregateId |> not) then
                    1
                else
                    event_aggregate_id_seq_dic.[version].[name].[aggregateId]
            if (event_aggregate_id_seq_dic.ContainsKey version |> not) then
                let dic = Generic.Dictionary<Name, Generic.Dictionary<AggregateId, int>>()
                let dic2 = Generic.Dictionary<AggregateId, int>()
                dic2.Add(aggregateId, event_id_seq + 1)
                dic.Add(name, dic2)
                event_aggregate_id_seq_dic.Add(version, dic)
            else
                let dic = event_aggregate_id_seq_dic.[version]
                if (dic.ContainsKey name |> not) then
                    let dic2 = Generic.Dictionary<AggregateId, int>()
                    dic2.Add(aggregateId, event_id_seq + 1)
                    dic.Add(name, dic2)
                else
                    let dic2 = dic.[name]
                    if (dic2.ContainsKey aggregateId |> not) then
                        dic2.Add(aggregateId, event_id_seq + 1)
                    else
                        dic2.[aggregateId] <- event_id_seq + 1
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
                let dic = new Generic.Dictionary<string, List<StoragePgEvent<_>>>()
                dic.Add(name, events)
                events_dic.Add(version, dic)
            else
                let dic = events_dic.[version]
                if (dic.ContainsKey name |> not) then
                    dic.Add(name, events)
                else
                    dic.[name] <- events
        
        [<MethodImpl(MethodImplOptions.Synchronized)>]
        let storeAggregateEvents version name aggregateId events =
            log.Debug (sprintf "storeAggregateEvents %s %s %A" version name aggregateId)
            if (aggregate_events_dic.ContainsKey version |> not) then
                let dic = Generic.Dictionary<Name, Generic.Dictionary<AggregateId, List<StorageEventJsonRef>>>()
                let dic2 = Generic.Dictionary<AggregateId, List<StorageEventJsonRef>>()
                dic2.Add(aggregateId, events)
                dic.Add(name, dic2)
                aggregate_events_dic.Add(version, dic)
            else 
                let dic = aggregate_events_dic.[version]
                if (dic.ContainsKey name |> not) then
                    let dic2 = Generic.Dictionary<AggregateId, List<StorageEventJsonRef>>()
                    dic2.Add(aggregateId, events)
                    dic.Add(name, dic2)
                else
                    let dic2 = dic.[name]
                    if (dic2.ContainsKey aggregateId |> not) then
                        dic2.Add(aggregateId, events)
                    else
                        dic2.[aggregateId] <- events

        [<MethodImpl(MethodImplOptions.Synchronized)>]
        let storeSnapshots version name snapshots =
            log.Debug (sprintf "storeSnapshots %s %s" version name)
            if (snapshots_dic.ContainsKey version |> not) then
                let dic = Generic.Dictionary<string, List<StorageSnapshot>>()
                dic.Add(name, snapshots)
                snapshots_dic.Add(version, dic)
            else
                let dic = snapshots_dic.[version]
                if (dic.ContainsKey name |> not) then
                    dic.Add(name, snapshots)
                else
                    dic.[name] <- snapshots
                    
        [<MethodImpl(MethodImplOptions.Synchronized)>]
        let addAggregateSnapshots version name aggregateId snapshot =
            log.Debug (sprintf "AddAggregateSnapshots %s %s" version name)
            if (aggregate_snapshots_dic.ContainsKey version |> not) then
                let dic = Generic.Dictionary<Name, Generic.Dictionary<Guid, List<StorageAggregateSnapshot>>>()
                let aggregateDic = Generic.Dictionary<Guid, List<StorageAggregateSnapshot>>()
                aggregateDic.Add(aggregateId, [snapshot])
                dic.Add(name, aggregateDic)
                aggregate_snapshots_dic.Add(version, dic)
            else 
                let dic = aggregate_snapshots_dic.[version]
                if (dic.ContainsKey name |> not) then
                    let aggregateDic = Generic.Dictionary<Guid, List<StorageAggregateSnapshot>>()
                    aggregateDic.Add(aggregateId, [snapshot])
                    dic.Add(name, aggregateDic)
                else
                    if (aggregate_snapshots_dic.[version].[name].ContainsKey aggregateId |> not) then
                        aggregate_snapshots_dic.[version].[name].Add(aggregateId, [snapshot])
                    else
                        aggregate_snapshots_dic.[version].[name].[aggregateId] <- [snapshot]
                    
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
        
        let getExistingAggregateEvents version name aggregateId =
            log.Debug (sprintf "getExistingAggregateEvents %s %s %A" version name aggregateId)
            if (aggregate_events_dic.ContainsKey version |> not) || (aggregate_events_dic.[version].ContainsKey name |> not) || (aggregate_events_dic.[version].[name].ContainsKey aggregateId |> not) then
                []
            else
                aggregate_events_dic.[version].[name].[aggregateId]       

        [<MethodImpl(MethodImplOptions.Synchronized)>]
        let storeSnapshots version name snapshots =
            log.Debug (sprintf "storeSnapshots %s %s" version name)
            if (snapshots_dic.ContainsKey version |> not) then
                let dic = Generic.Dictionary<string, List<StorageSnapshot>>()
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

        member this.Reset version name =
            log.Debug (sprintf "Reset %s %s" version name)
            events_dic.Clear()
            snapshots_dic.Clear()
            event_id_seq_dic.Clear()
            snapshot_id_seq_dic.Clear()
            aggregate_events_dic.Clear()
            aggregate_snapshots_dic.Clear()
            
        interface IEventStore<string> with
            [<MethodImpl(MethodImplOptions.Synchronized)>]
            member this.Reset version name =
                this.Reset version name
            member this.ResetAggregateStream version name =
                this.Reset version name

            [<MethodImpl(MethodImplOptions.Synchronized)>]
            member this.AddEvents _ version name xs: Result<List<int>, string> = 
                log.Debug (sprintf "AddEvents %s %s" version name)
                let newEvents =
                    [ for e in xs do
                        yield {
                            Id = next_event_id version name
                            JsonEvent = e
                            Timestamp = DateTime.UtcNow
                        }
                    ]
                let events = getExistingEvents version name @ newEvents
                storeEvents version name events
                let ids = newEvents |>> (fun x -> x.Id)
                ids |> Ok
            member this.SetInitialAggregateStateAndAddEvents _ aggregateId aggregateVersion aggregatename json contextVersion contextName events =
                let initialState =
                    {
                        Id = next_snapshot_id aggregateVersion aggregatename
                        AggregateId = aggregateId
                        Snapshot = json
                        TimeStamp = DateTime.UtcNow
                        EventId = None
                    }
                let snapshots = Generic.Dictionary<AggregateId, List<StorageAggregateSnapshot>>()
                snapshots.Add (aggregateId, [initialState])
                addAggregateSnapshots aggregateVersion aggregatename aggregateId initialState
                (this:> IEventStore<string>).AddEvents 0 contextVersion contextName events
                
            member this.GetEventsAfterId version id name =
                log.Debug (sprintf "GetEventsAfterId %s %A %s" version id name)
                if (events_dic.ContainsKey version |> not) || (events_dic.[version].ContainsKey name |> not) then
                    [] |> Ok
                else
                    events_dic.[version].[name]
                    |> List.filter (fun x -> x.Id > id)
                    |>> (fun x -> x.Id, x.JsonEvent)
                    |> Ok
            member this.MultiAddEvents (arg: List< _ * List<Json> * Version * Name>) =
                log.Debug (sprintf "MultiAddEvents %A" arg)
                let cmds =
                    arg 
                    |> List.map 
                        (fun (_, xs, version, name) ->
                            (this :> IEventStore<string>).AddEvents 0 version name xs |> Result.get
                        ) 
                cmds |> Ok

            member this.SetSnapshot  version (id, snapshot) name =
                log.Debug (sprintf "SetSnapshot %s %A %s" version id name)
                let newSnapshot =
                    {
                        Id = next_snapshot_id version name
                        Snapshot = snapshot 
                        TimeStamp = DateTime.UtcNow
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

            member this.SetInitialAggregateState aggregateId version name snapshot =
                let initialState =
                    {
                        Id = next_snapshot_id version name
                        AggregateId = aggregateId
                        Snapshot = snapshot
                        TimeStamp = DateTime.UtcNow
                        EventId = None 
                    }
                let snapshots = Generic.Dictionary<AggregateId, List<StorageAggregateSnapshot>>()
                snapshots.Add(aggregateId, [initialState])
                addAggregateSnapshots version name aggregateId initialState
                () |> Ok
                
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

            member this.TryGetLastSnapshotIdByAggregateId version name aggregateId =
                log.Debug (sprintf "TryGetLastSnapshotIdByAggregateId %s %s %A" version name aggregateId)
                if (aggregate_snapshots_dic.ContainsKey version |> not) || 
                   (aggregate_snapshots_dic.[version].ContainsKey name |> not) || 
                   (aggregate_snapshots_dic.[version].[name].ContainsKey aggregateId |> not) then
                        None
                else
                    aggregate_snapshots_dic.[version].[name].[aggregateId]
                    |> List.tryLast
                    |>> (fun x -> (x.EventId, x.Id))

            member this.TryGetLastSnapshotEventId version name =
                log.Debug (sprintf "TryGetLastSnapshotEventId %s %s" version name)
                if (snapshots_dic.ContainsKey version |> not) || (snapshots_dic.[version].ContainsKey name |> not) then
                    None
                else
                    snapshots_dic.[version].[name]
                    |> List.tryLast
                    |>> (fun x -> x.EventId)
            member this.TryGetLastAggregateSnapshotEventId version name aggregateId =
                if (aggregate_snapshots_dic.ContainsKey version |> not) || (aggregate_snapshots_dic.[version].ContainsKey name |> not) ||
                   (aggregate_snapshots_dic.[version].[name].ContainsKey aggregateId |> not) then
                    None
                else
                    aggregate_snapshots_dic.[version].[name].[aggregateId]
                    |> List.tryLast
                    |> tryLast
                    |>> (fun x -> x.EventId) 
                    |> Option.bind (fun x -> x)
                
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

            member this.TryGetAggregateSnapshotById version name aggregateId id =
                if (aggregate_snapshots_dic.ContainsKey version |> not
                    || aggregate_snapshots_dic.[version].ContainsKey name |> not
                    || aggregate_snapshots_dic.[version].[name].ContainsKey aggregateId |> not)
                    then
                    None
                else
                    aggregate_snapshots_dic.[version].[name].[aggregateId]
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

            member this.MultiAddAggregateEvents (arg: List< _* List<Json> * Version * Name * AggregateId> ) =
                log.Debug (sprintf "MultiAddAggregateEvents %A" arg)
                let cmds =
                    arg
                    |> List.map
                        (fun (_, xs, version, name, aggregateId) ->
                            (this :> IEventStore<string>).AddAggregateEvents 0 version name aggregateId xs |> Result.get
                        )
                cmds |> Ok        

            [<MethodImpl(MethodImplOptions.Synchronized)>]
            member this.AddAggregateEvents _ version name aggregateId events =
                log.Debug (sprintf "AddAggregateEvents %s %s %A" version name aggregateId)
                let newEvents =
                    [
                        for e in events do
                            yield {
                                AggregateId = aggregateId
                                Id = next_aggregate_event_id version name aggregateId
                                JsonEvent = e
                                KafkaOffset = None
                                KafkaPartition = None
                                Timestamp = DateTime.UtcNow
                            }
                    ]
                let events' = getExistingAggregateEvents version name aggregateId @ newEvents
                storeAggregateEvents version name aggregateId events'
                let ids = newEvents |>> (fun x -> x.Id)
                ids |> Ok
                    
            member this.TryGetLastAggregateEventId(version: Version) (name: Name) (aggregateId: AggregateId): Option<EventId> =
                log.Debug (sprintf "TryGetLastEentdByAggregateId %s %s %A"  name version aggregateId)
                if (aggregate_events_dic.ContainsKey version |> not) then 
                    None
                else
                    if (aggregate_events_dic.[version].ContainsKey name |> not) then
                        None
                    else
                        if (aggregate_events_dic.[version].[name].ContainsKey aggregateId) then
                            aggregate_events_dic.[version].[name].[aggregateId]
                            |> List.tryLast
                            |>> (fun x ->  x.Id) //, x.KafkaOffset, x.KafkaPartition ))
                        else
                            None 
                
            member this.GetAggregateEventsAfterId version name aggregateId id =
                if (aggregate_events_dic.ContainsKey version |> not) then
                    [] |> Ok
                else
                    if (aggregate_events_dic.[version].ContainsKey name |> not) then
                        [] |> Ok
                    else
                        if (aggregate_events_dic.[version].[name].ContainsKey aggregateId |> not) then
                            [] |> Ok
                        else
                            aggregate_events_dic.[version].[name].[aggregateId]
                            |> List.filter (fun x -> x.Id > id)
                            |>> (fun x -> x.Id, x.JsonEvent)
                            |> Ok
            member this.GetAggregateEvents version name aggregateId: Result<List<EventId * Json>,string> =
                log.Debug (sprintf "GetAggregateEvents %s %s %A" version name aggregateId)
                if (aggregate_events_dic.ContainsKey version |> not) then
                    [] |> Ok
                else
                    if (aggregate_events_dic.[version].ContainsKey name |> not) then
                        [] |> Ok
                    else
                        if (aggregate_events_dic.[version].[name].ContainsKey aggregateId |> not) then
                            [] |> Ok
                        else
                            aggregate_events_dic.[version].[name].[aggregateId]
                            |>> (fun x -> (x.Id, x.JsonEvent))
                            |> Ok

            member this.SetAggregateSnapshot version (aggregateId, eventId, snapshot) name =
                let state =
                    {
                        Id = next_snapshot_id version name
                        AggregateId = aggregateId
                        Snapshot = snapshot
                        TimeStamp = DateTime.UtcNow
                        EventId = eventId |> Some
                    }
                addAggregateSnapshots version name aggregateId state
                () |> Ok     

