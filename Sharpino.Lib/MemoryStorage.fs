
namespace Sharpino

open System.Runtime.CompilerServices
open System.Threading
open FSharpPlus
open FSharpPlus.Operators
open System
open System.Collections
open FsToolkit.ErrorHandling
open Sharpino.PgStorage
open Sharpino.Storage
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions
open Sharpino.Definitions

// should be called like InMemoryEventStore 
module MemoryStorage =
    let logger: ILogger ref = ref NullLogger.Instance
    let setLogger (newLogger: ILogger) =
        logger := newLogger

    type MemoryStorage() =
        let event_id_seq_dic = Generic.Dictionary<Version, Generic.Dictionary<Name,int>>()
        let event_aggregate_id_seq_dic = Generic.Dictionary<Version, Generic.Dictionary<Name,Generic.Dictionary<AggregateId,int>>>()
        let snapshot_id_seq_dic = Generic.Dictionary<Version, Generic.Dictionary<Name,int>>()
        let events_dic = Generic.Dictionary<Version, Generic.Dictionary<string, List<StoragePgEvent<_>>>>()
        let aggregate_events_dic = Generic.Dictionary<Version, Generic.Dictionary<string, Generic.Dictionary<AggregateId, List<StorageEventJsonRef>>>>()
        let snapshots_dic = Generic.Dictionary<Version, Generic.Dictionary<string, List<StorageSnapshot>>>()
        let aggregate_snapshots_dic = Generic.Dictionary<Version, Generic.Dictionary<Name, Generic.Dictionary<AggregateId, List<StorageAggregateSnapshot>>>>()
        let aggregate_undo_command_buffer = Generic.Dictionary<Version, Generic.Dictionary<Name, Generic.Dictionary<AggregateId, List<StorageEventJsonRef>>>>()
        
        [<MethodImpl(MethodImplOptions.Synchronized)>]
        let next_event_id version name =
            logger.Value.LogDebug (sprintf "next_event_id %s %s" version name)
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
            logger.Value.LogDebug (sprintf "next_aggregate_event_id %s %s %A" version name aggregateId)
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
            logger.Value.LogDebug (sprintf "next_snapshot_id %s %s" version name)

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
            logger.Value.LogDebug (sprintf "storeEvents %s %s" version name)
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
            logger.Value.LogDebug (sprintf "storeAggregateEvents %s %s %A" version name aggregateId)
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
            logger.Value.LogDebug (sprintf "storeSnapshots %s %s" version name)
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
            logger.Value.LogDebug (sprintf "AddAggregateSnapshots %s %s" version name)
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
            logger.Value.LogDebug (sprintf "getExistingSnapshots %s %s" version name)
            if (snapshots_dic.ContainsKey version |> not) || (snapshots_dic.[version].ContainsKey name |> not) then
                []
            else
                snapshots_dic.[version].[name]
        let getExistingAggregateSnapshots version name aggregateId =
            logger.Value.LogDebug (sprintf "getExistingAggregateSnapshots %s %s %A" version name aggregateId)
            if (aggregate_snapshots_dic.ContainsKey version |> not) || (aggregate_snapshots_dic.[version].ContainsKey name |> not) || (aggregate_snapshots_dic.[version].[name].ContainsKey aggregateId |> not) then
                []
            else
                aggregate_snapshots_dic.[version].[name].[aggregateId]

        let getExistingEvents version name =
            logger.Value.LogDebug (sprintf "getExistingEvents %s %s" version name)
            if (events_dic.ContainsKey version |> not) || (events_dic.[version].ContainsKey name |> not) then
                []
            else
                events_dic.[version].[name]
        
        let getExistingAggregateEvents version name aggregateId =
            logger.Value.LogDebug (sprintf "getExistingAggregateEvents %s %s %A" version name aggregateId)
            if (aggregate_events_dic.ContainsKey version |> not) || (aggregate_events_dic.[version].ContainsKey name |> not) || (aggregate_events_dic.[version].[name].ContainsKey aggregateId |> not) then
                []
            else
                aggregate_events_dic.[version].[name].[aggregateId]       

        [<MethodImpl(MethodImplOptions.Synchronized)>]
        let storeSnapshots version name snapshots =
            logger.Value.LogDebug (sprintf "storeSnapshots %s %s" version name)
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
            logger.Value.LogDebug (sprintf "getExistingSnapshots %s %s" version name)
            if (snapshots_dic.ContainsKey version |> not) || (snapshots_dic.[version].ContainsKey name |> not) then
                []
            else
                snapshots_dic.[version].[name]

        let getExistingEvents version name =
            logger.Value.LogDebug (sprintf "getExistingEvents %s %s" version name)
            if (events_dic.ContainsKey version |> not) || (events_dic.[version].ContainsKey name |> not) then
                []
            else
                events_dic.[version].[name]

        member this.Reset version name =
            logger.Value.LogDebug (sprintf "Reset %s %s" version name)
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
            member this.AddAggregateEventsMdAsync (eventId: EventId, version: Version, name: Name, aggregateId: System.Guid, md: Metadata, events: List<string>, ?ct: CancellationToken) =
                taskResult
                    {
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
                        return ids
                    }

            [<MethodImpl(MethodImplOptions.Synchronized)>]
            member this.AddEvents _ version name xs: Result<List<int>, string> =
                logger.Value.LogDebug (sprintf "AddEvents %s %s" version name)
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
                
            // not handling metadata in memory storage      
            member this.AddEventsMd _ version name md xs: Result<List<int>, string> =
                logger.Value.LogDebug (sprintf "AddEvents %s %s" version name)
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
                        Deleted = false
                    }
                let snapshots = Generic.Dictionary<AggregateId, List<StorageAggregateSnapshot>>()
                snapshots.Add (aggregateId, [initialState])
                addAggregateSnapshots aggregateVersion aggregatename aggregateId initialState
                (this:> IEventStore<string>).AddEvents 0 contextVersion contextName events
                
            member this.SetInitialAggregateStateAndAddEventsMd _ aggregateId aggregateVersion aggregatename json contextVersion contextName _ events =
                let initialState =
                    {
                        Id = next_snapshot_id aggregateVersion aggregatename
                        AggregateId = aggregateId
                        Snapshot = json
                        TimeStamp = DateTime.UtcNow
                        EventId = None
                        Deleted = false
                    }
                let snapshots = Generic.Dictionary<AggregateId, List<StorageAggregateSnapshot>>()
                snapshots.Add (aggregateId, [initialState])
                addAggregateSnapshots aggregateVersion aggregatename aggregateId initialState
                (this:> IEventStore<string>).AddEvents 0 contextVersion contextName events
                
            member this.SetInitialAggregateStateAndAddAggregateEvents _ aggregateId aggregateVersion aggregatename secondAggregateId json contextVersion contextName events =
                let initialState =
                    {
                        Id = next_snapshot_id aggregateVersion aggregatename
                        AggregateId = aggregateId
                        Snapshot = json
                        TimeStamp = DateTime.UtcNow
                        EventId = None
                        Deleted = false
                    }
                let snapshots = Generic.Dictionary<AggregateId, List<StorageAggregateSnapshot>>()
                snapshots.Add (aggregateId, [initialState])
                addAggregateSnapshots aggregateVersion aggregatename aggregateId initialState
                (this:> IEventStore<string>).AddAggregateEvents 0 contextVersion contextName secondAggregateId events
            member this.SetInitialAggregateStateAndAddAggregateEventsMd _ aggregateId aggregateVersion aggregatename secondAggregateId json contextVersion contextName _ events  =
                let initialState =
                    {
                        Id = next_snapshot_id aggregateVersion aggregatename
                        AggregateId = aggregateId
                        Snapshot = json
                        TimeStamp = DateTime.UtcNow
                        EventId = None
                        Deleted = false
                    }
                let snapshots = Generic.Dictionary<AggregateId, List<StorageAggregateSnapshot>>()
                snapshots.Add (aggregateId, [initialState])
                addAggregateSnapshots aggregateVersion aggregatename aggregateId initialState
                (this:> IEventStore<string>).AddAggregateEvents 0 contextVersion contextName secondAggregateId events
                
            member this.SetInitialAggregateStateAndMultiAddAggregateEvents aggregateId Version Name jsonSnapshot events =
                let initialState =
                    {
                        Id = next_snapshot_id Version Name
                        AggregateId = aggregateId
                        Snapshot = jsonSnapshot
                        TimeStamp = DateTime.UtcNow
                        EventId = None
                        Deleted = false
                    }
                let snapshots = Generic.Dictionary<AggregateId, List<StorageAggregateSnapshot>>()
                snapshots.Add (aggregateId, [initialState])
                addAggregateSnapshots Version Name aggregateId initialState
                (this:> IEventStore<string>).MultiAddAggregateEvents events
                
            member this.SetInitialAggregateStateAndMultiAddAggregateEventsMd aggregateId Version Name jsonSnapshot md events =
                let initialState =
                    {
                        Id = next_snapshot_id Version Name
                        AggregateId = aggregateId
                        Snapshot = jsonSnapshot
                        TimeStamp = DateTime.UtcNow
                        EventId = None
                        Deleted = false
                    }
                let snapshots = Generic.Dictionary<AggregateId, List<StorageAggregateSnapshot>>()
                snapshots.Add (aggregateId, [initialState])
                addAggregateSnapshots Version Name aggregateId initialState
                (this:> IEventStore<string>).MultiAddAggregateEvents events
                
            member this.GetEventsAfterId version id name =
                logger.Value.LogDebug (sprintf "GetEventsAfterId %s %A %s" version id name)
                if (events_dic.ContainsKey version |> not) || (events_dic.[version].ContainsKey name |> not) then
                    [] |> Ok
                else
                    events_dic.[version].[name]
                    |> List.filter (fun x -> x.Id > id)
                    |>> (fun x -> x.Id, x.JsonEvent)
                    |> Ok
                    
            member this.MultiAddEvents (arg: List< _ * List<Json> * Version * Name>) =
                logger.Value.LogDebug (sprintf "MultiAddEvents %A" arg)
                let cmds =
                    arg 
                    |> List.map 
                        (fun (_, xs, version, name) ->
                            (this :> IEventStore<string>).AddEvents 0 version name xs |> Result.get
                        ) 
                cmds |> Ok
                
            member this.MultiAddEventsMd md (arg: List< _ * List<Json> * Version * Name>) =
                logger.Value.LogDebug (sprintf "MultiAddEvents %A" arg)
                let cmds =
                    arg 
                    |> List.map 
                        (fun (_, xs, version, name) ->
                            (this :> IEventStore<string>).AddEvents 0 version name xs |> Result.get
                        ) 
                cmds |> Ok
                
            member this.SetSnapshot  version (id, snapshot) name =
                logger.Value.LogDebug (sprintf "SetSnapshot %s %A %s" version id name)
                let newSnapshot: StorageSnapshot =
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
                logger.Value.LogDebug (sprintf "TryGetEvent %s %A %s" version id name)
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
                        Deleted = false
                    }
                let snapshots = Generic.Dictionary<AggregateId, List<StorageAggregateSnapshot>>()
                snapshots.Add(aggregateId, [initialState])
                addAggregateSnapshots version name aggregateId initialState
                () |> Ok
           
            member this.SetInitialAggregateStates version name idsAndSnapshots =
                logger.Value.LogDebug (sprintf "SetInitialAggregateStates %s %s" version name)
                idsAndSnapshots
                |> List.ofArray
                |> List.iter (fun (aggregateId, snapshot) ->
                    let initialState =
                        {
                            Id = next_snapshot_id version name
                            AggregateId = aggregateId
                            Snapshot = snapshot
                            TimeStamp = DateTime.UtcNow
                            EventId = None 
                            Deleted = false
                        }
                    let snapshots = Generic.Dictionary<AggregateId, List<StorageAggregateSnapshot>>()
                    snapshots.Add(aggregateId, [initialState])
                    addAggregateSnapshots version name aggregateId initialState
                )
                () |> Ok
                
            member this.TryGetLastEventId  version  name =
                logger.Value.LogDebug (sprintf "TryGetLastEventId %s %s" version name)
                if (events_dic.ContainsKey version |> not) || (events_dic.[version].ContainsKey name |> not) then
                    None
                else
                    events_dic.[version].[name]
                    |> List.tryLast
                    |>> (fun x -> x.Id)
            member this.TryGetLastSnapshot version name =
                logger.Value.LogDebug (sprintf "TryGetLastSnapshot %s %s" version name)
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
                logger.Value.LogDebug (sprintf "TryGetLastSnapshotIdByAggregateId %s %s %A" version name aggregateId)
                if (aggregate_snapshots_dic.ContainsKey version |> not) || 
                   (aggregate_snapshots_dic.[version].ContainsKey name |> not) || 
                   (aggregate_snapshots_dic.[version].[name].ContainsKey aggregateId |> not) then
                        None
                else
                    let result =
                        aggregate_snapshots_dic.[version].[name].[aggregateId]
                        |> List.tryLast
                        |>> (fun x -> (x.EventId, x.Id, x.Deleted))
                    match result with
                    | None -> None
                    | Some (_, _, true) -> None
                    | Some (eventId, id, false) -> Some (eventId, id)
                    
            member this.TryGetLastHistorySnapshotIdByAggregateId version name aggregateId  = 
                logger.Value.LogDebug (sprintf "TryGetLastSnapshotIdByAggregateId %s %s %A" version name aggregateId)
                if (aggregate_snapshots_dic.ContainsKey version |> not) || 
                   (aggregate_snapshots_dic.[version].ContainsKey name |> not) || 
                   (aggregate_snapshots_dic.[version].[name].ContainsKey aggregateId |> not) then
                        None
                else
                    let result =
                        aggregate_snapshots_dic.[version].[name].[aggregateId]
                        |> List.tryLast
                        |>> (fun x -> (x.EventId, x.Id))
                    result    

            member this.TryGetLastSnapshotEventId version name =
                logger.Value.LogDebug (sprintf "TryGetLastSnapshotEventId %s %s" version name)
                if (snapshots_dic.ContainsKey version |> not) || (snapshots_dic.[version].ContainsKey name |> not) then
                    None
                else
                    snapshots_dic.[version].[name]
                    |> List.tryLast
                    |>> (fun x -> x.EventId)
                    
                    
            member this.TryGetLastAggregateSnapshot version name aggregateId =
                if (aggregate_snapshots_dic.ContainsKey version |> not) || (aggregate_snapshots_dic.[version].ContainsKey name |> not) ||
                   (aggregate_snapshots_dic.[version].[name].ContainsKey aggregateId |> not) then
                        Error "not found"
                else
                    let result =
                        aggregate_snapshots_dic.[version].[name].[aggregateId]
                        |> List.tryLast
                        |>> (fun x -> (x.EventId, x.Id, x.Deleted, x.Snapshot))
                    match result with
                    | None -> Error "not found"
                    | Some (_, _, true, _) -> Error "was deleted"
                    | Some (eventId, id, _, json) -> Ok (eventId, json)
                    
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
                logger.Value.LogDebug (sprintf "TryGetLastSnapshotId %s %s" version name)
                if (snapshots_dic.ContainsKey version |> not) || (snapshots_dic.[version].ContainsKey name |> not) then
                    None
                else
                    snapshots_dic.[version].[name]
                    |> List.tryLast
                    |>> (fun x -> x.EventId, x.Id)

            member this.TryGetFirstSnapshot version name aggregateId =
                logger.Value.LogDebug (sprintf "TryGetFirstSnapshot %s %s %A" version name aggregateId)
                if (aggregate_snapshots_dic.ContainsKey version |> not) || (aggregate_snapshots_dic.[version].ContainsKey name |> not) || (aggregate_snapshots_dic.[version].[name].ContainsKey aggregateId |> not) then
                    Error "not found"
                else     
                    aggregate_snapshots_dic.[version].[name].[aggregateId]
                    |> List.tryHead
                    |> Result.ofOption (sprintf "aggregate %s%s id %A not found" version name aggregateId)
                    >>= (fun x -> (x.Id, x.Snapshot) |> Ok)
            
            member this.TryGetSnapshotById version name id =
                logger.Value.LogDebug  (sprintf "TryGetSnapshotById %s %s %A" version name id)
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
                logger.Value.LogDebug (sprintf "GetEventsInATimeInterval %s %s %A %A" version name dateFrom dateTo)
                if (events_dic.ContainsKey version |> not) || (events_dic.[version].ContainsKey name |> not) then
                    [] |> Ok
                else
                    events_dic.[version].[name]
                    |> List.filter (fun x -> x.Timestamp >= dateFrom && x.Timestamp <= dateTo)
                    |>> (fun x -> x.Id, x.JsonEvent)
                    |>> (fun (id, event) -> id, event)
                    |> Ok

            member this.GetAggregateSnapshotsInATimeInterval version name dateFrom dateTo =
                logger.Value.LogDebug (sprintf "GetAggregateSnapshotsInATimeInterval %s %s %A %A" version name dateFrom dateTo)
                if (aggregate_snapshots_dic.ContainsKey version |> not) || (aggregate_snapshots_dic.[version].ContainsKey name |> not) then
                    [] |> Ok
                else
                    aggregate_snapshots_dic.[version].[name]
                    |> Dictionary.keys
                    |> Seq.toList
                    |> List.map (fun x -> aggregate_snapshots_dic.[version].[name].[x])
                    |> List.collect (fun x -> x)
                    |> List.filter (fun x -> x.TimeStamp >= dateFrom && x.TimeStamp <= dateTo)
                    |>> (fun x -> x.Id, x.AggregateId, x.TimeStamp, x.Snapshot)
                    |> Ok
                    
            member this.GetAggregateIdsInATimeInterval version name dateFrom dateTo =
                logger.Value.LogDebug (sprintf "GetAggregateIdsInATimeInterval %s %s %A %A" version name dateFrom dateTo)
                if (aggregate_snapshots_dic.ContainsKey version |> not) || (aggregate_snapshots_dic.[version].ContainsKey name |> not) then
                    [] |> Ok
                else
                    aggregate_snapshots_dic.[version].[name]
                    |> Dictionary.keys
                    |> Seq.toList
                    |> List.map (fun x -> aggregate_snapshots_dic.[version].[name].[x])
                    |> List.collect (fun x -> x)
                    |> List.filter (fun x -> x.TimeStamp >= dateFrom && x.TimeStamp <= dateTo)
                    |>> (fun x -> x.AggregateId)
                    |> List.distinct
                    |> Ok
            
            member this.GetAggregateIds version name =
                logger.Value.LogDebug (sprintf "GetAggregateIds %s %s" version name)
                if (aggregate_snapshots_dic.ContainsKey version |> not) || (aggregate_snapshots_dic.[version].ContainsKey name |> not) then
                    [] |> Ok
                else
                    aggregate_snapshots_dic.[version].[name]
                    |> Dictionary.keys
                    |> Seq.toList
                    |> Ok        
            
            member this.GetAggregateEventsInATimeInterval (version: Version) (name: Name) (aggregateId: AggregateId) (dateFrom: DateTime) (dateTo: DateTime) =
                logger.Value.LogDebug (sprintf "GetAggregateEventsInATimeInterval %s %s %A %A %A" version name aggregateId dateFrom dateTo)
                if
                    ( aggregate_events_dic.ContainsKey version |> not)
                    || (aggregate_events_dic.[version].ContainsKey name |> not)
                    || (aggregate_events_dic.[version].[name].ContainsKey aggregateId |> not )
                then
                    [] |> Ok
                else
                    aggregate_events_dic.[version].[name].[aggregateId]
                    |> List.filter (fun x -> x.Timestamp >= dateFrom && x.Timestamp <= dateTo)
                    |>> (fun x -> x.Id, x.JsonEvent)
                    |>> (fun (id, event) -> id, event)
                    |> Ok
                    
            
            member this.GetAllAggregateEventsInATimeInterval version name dateFrom dateTo =
                logger.Value.LogDebug (sprintf "GetAllaggregateEventsInAtimeInterval %s %s %A %A" version name dateFrom dateTo)
                if (aggregate_events_dic.ContainsKey version |> not) || (aggregate_events_dic.[version].ContainsKey name |> not) then
                    [] |> Ok
                else
                    aggregate_events_dic.[version].[name]
                    |> Dictionary.keys
                    |> Seq.toList
                    |> List.map (fun x -> aggregate_events_dic.[version].[name].[x])
                    |> List.collect (fun x -> x)
                    |> List.filter (fun x -> x.Timestamp >= dateFrom && x.Timestamp <= dateTo)
                    |>> (fun x -> x.Id, x.JsonEvent)
                    |> Ok
           
            member this.GetMultipleAggregateEventsInATimeInterval version name aggregateIds dateFrom dateTo =
                logger.Value.LogDebug (sprintf "GetMultipleAggregateEventsInATimeInterval %s %s %A %A %A" version name aggregateIds dateFrom dateTo)
                if (aggregate_events_dic.ContainsKey version |> not) || (aggregate_events_dic.[version].ContainsKey name |> not) then
                    [] |> Ok
                else
                    aggregate_events_dic.[version].[name]
                    |> Dictionary.keys
                    |> Seq.toList
                    |> List.filter (fun x -> aggregateIds |> List.contains x)
                    |> List.map (fun x -> aggregate_events_dic.[version].[name].[x])
                    |> List.collect (fun x -> x)
                    |> List.filter (fun x -> x.Timestamp >= dateFrom && x.Timestamp <= dateTo)
                    |>> (fun x -> x.Id, x.AggregateId, x.JsonEvent)
                    |> Ok
            
            member this.MultiAddAggregateEvents (arg: List< _* List<Json> * Version * Name * AggregateId>) =
                logger.Value.LogDebug (sprintf "MultiAddAggregateEvents %A" arg)
                let cmds =
                    arg
                    |> List.map
                        (fun (_, xs, version, name, aggregateId) ->
                            (this :> IEventStore<string>).AddAggregateEvents 0 version name aggregateId xs |> Result.get
                        )
                cmds |> Ok
                
            member this.MultiAddAggregateEventsMd md (arg: List< _* List<Json> * Version * Name * AggregateId>) =
                logger.Value.LogDebug (sprintf "MultiAddAggregateEvents %A" arg)
                let cmds =
                    arg
                    |> List.map
                        (fun (_, xs, version, name, aggregateId) ->
                            (this :> IEventStore<string>).AddAggregateEvents 0 version name aggregateId xs |> Result.get
                        )
                cmds |> Ok        

            [<MethodImpl(MethodImplOptions.Synchronized)>]
            member this.AddAggregateEvents _ version name aggregateId events =
                logger.Value.LogDebug (sprintf "AddAggregateEvents %s %s %A" version name aggregateId)
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
                
            [<MethodImpl(MethodImplOptions.Synchronized)>]
            member this.AddAggregateEventsMd _ version name aggregateId _ events =
                logger.Value.LogDebug (sprintf "AddAggregateEvents %s %s %A" version name aggregateId)
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
                logger.Value.LogDebug (sprintf "TryGetLastAggregateEventId %s %s %A" version name aggregateId)
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
                logger.Value.LogDebug (sprintf "GetAggregateEvents %s %s %A" version name aggregateId)
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
                        Deleted = false
                    }
                addAggregateSnapshots version name aggregateId state
                () |> Ok
                
            member this.GDPRReplaceSnapshotsAndEventsOfAnAggregate version name aggregateId snapshot event =
                logger.Value.LogDebug (sprintf "GDPRReplaceSnapshotsAndEventsOfAnAggregate %s %s %A %A %A" version name aggregateId snapshot event)
                () |> Ok

            member this.SnapshotAndMarkDeleted version name eventId aggregateId snapshot =
                let newSnapshot: StorageAggregateSnapshot =
                    {
                        Id = next_snapshot_id version name
                        AggregateId = aggregateId
                        Snapshot = snapshot 
                        TimeStamp = DateTime.UtcNow
                        EventId = None 
                        Deleted = true
                    }
                addAggregateSnapshots version name aggregateId newSnapshot
                () |> Ok
            member this.SnapshotMarkDeletedAndAddAggregateEventsMd
                s1Version
                s1name
                s1EventId
                s1AggregateId
                s1Snapshot
                streamEventId
                streamAggregateVersion
                streamAggregateName
                streamAggregateId
                metaData
                events =
                    
                (this:> IEventStore<string>).SnapshotAndMarkDeleted s1Version s1name s1EventId s1AggregateId s1Snapshot |> ignore
                (this:> IEventStore<string>).AddAggregateEventsMd streamEventId streamAggregateVersion streamAggregateName streamAggregateId metaData events
                
            member this.SnapshotMarkDeletedAndMultiAddAggregateEventsMd 
                md
                s1Version
                s1name
                s1EventId
                s1AggregateId
                s1Snapshot
                (arg: List<EventId * List<string> * Version * Name * AggregateId>) =
                    (this:> IEventStore<string>).SnapshotAndMarkDeleted s1Version s1name s1EventId s1AggregateId s1Snapshot |> ignore
                    (this:> IEventStore<string>).MultiAddAggregateEventsMd md arg 
                    
