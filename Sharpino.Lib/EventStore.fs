namespace Sharpino
open System
open System.Threading
open System.Threading.Tasks
open Sharpino.Definitions

// the "md" version of any function is the one that takes a metadata parameter
// the md requires an extra text md field in any event and a proper new funcion on the db side
// like  insert_md{Version}{AggregateStorageName}_aggregate_event_and_return_id
module Storage =
    type StoragePgEvent<'T> =
        {
            JsonEvent: 'T
            Id: int
            Timestamp: System.DateTime
        }
        
    type StorageEventJsonRef =
        {
            AggregateId: Guid
            JsonEvent: Json
            Id: int
            KafkaOffset: Option<int64>
            KafkaPartition: Option<int>
            Timestamp: System.DateTime
        }
        
    type StorageSnapshot = {
        Id: int
        Snapshot: Json
        TimeStamp: System.DateTime
        EventId: int
        // Deleted: bool
    }
    
    type StorageAggregateSnapshot = {
        Id: int
        AggregateId: Guid
        Snapshot: Json
        TimeStamp: System.DateTime
        EventId: Option<EventId>
        Deleted: bool
    }

    type StorageEvent<'E> =
        {
            Event: 'E
            Id: int
            Timestamp: System.DateTime
        }
        

    type KafkaOffset = int64
    type KafkaPartitionId = int
    type Metadata = string // this needs to be moved

    type IEventStore<'F> =
        abstract member Reset: Version -> Name -> unit

        abstract member ResetAggregateStream: Version -> Name -> unit

        abstract member TryGetLastSnapshot: Version -> Name -> Option<SnapId * EventId * 'F>
        abstract member TryGetLastEventId: Version -> Name -> Option<EventId>

        abstract member TryGetLastAggregateEventId: Version -> Name -> AggregateId -> Option<EventId> 

        abstract member TryGetLastSnapshotEventId: Version -> Name -> Option<EventId>
        abstract member TryGetLastSnapshotId: Version -> Name -> Option<EventId * SnapshotId>
        abstract member TryGetLastSnapshotIdByAggregateId: Version -> Name -> Guid -> Option<Option<EventId> * SnapshotId>
        abstract member TryGetLastHistorySnapshotIdByAggregateId: Version -> Name -> Guid -> Option<Option<EventId> * SnapshotId>
        
        abstract member TryGetLastAggregateSnapshotEventId: Version -> Name -> AggregateId -> Option<EventId>
        
        abstract member TryGetFirstSnapshot: Version -> Name -> AggregateId -> Result<SnapId * 'F, string>
        
        abstract member TryGetLastAggregateSnapshot: Version -> Name -> AggregateId -> Result<Option<EventId> * 'F, string>
        abstract member TryGetLastAggregateSnapshotAsync: Version * Name * AggregateId * ?ct:CancellationToken -> Task<Result<Option<EventId> * 'F, string>>

        
        [<Obsolete("")>]
        abstract member TryGetSnapshotById: Version -> Name -> int ->Option<EventId * 'F>
        [<Obsolete("")>]
        abstract member TryGetAggregateSnapshotById: Version -> Name -> AggregateId -> EventId ->Option<Option<EventId> * 'F>

        [<Obsolete("")>]
        abstract member TryGetEvent: Version -> EventId -> Name -> Option<StoragePgEvent<'F>>
        abstract member SetSnapshot: Version -> EventId * 'F -> Name -> Result<unit, string>
        abstract member SetAggregateSnapshot: Version -> AggregateId * EventId * 'F -> Name -> Result<unit, string>

        abstract member AddEventsMd: EventId -> Version -> Name -> Metadata -> List<'F> -> Result<List<int>, string>

        abstract member SetInitialAggregateState: AggregateId ->  Version -> Name -> 'F ->  Result<unit, string>
        
        abstract member SetInitialAggregateStateAsync: AggregateId * Version * Name * 'F * ?ct:CancellationToken ->  Task<Result<unit, string>>
        
        abstract member SetInitialAggregateStates: Version -> Name -> (AggregateId * 'F)[] ->  Result<unit, string>
        
        abstract member SetInitialAggregateStatesAsync: Version * Name * (AggregateId * 'F)[] * ?ct:CancellationToken -> Task<Result<unit, string>>
        abstract member SetInitialAggregateStateAndAddEventsMd: EventId -> AggregateId -> Version -> Name -> 'F -> Version -> Name -> Metadata -> List<'F> -> Result<List<int>, string>
        
        abstract member SetInitialAggregateStateAndMultiAddAggregateEventsMd: AggregateId -> Version -> Name -> 'F -> Metadata -> List<EventId * List<'F> * Version * Name * AggregateId> -> Result<List<List<EventId>>, string>   
        
        abstract member SetInitialAggregateStateAndAddAggregateEventsMd: EventId -> AggregateId -> Version -> Name -> AggregateId -> 'F -> Version -> Name -> Metadata -> List<'F> -> Result<List<int>, string>
        
        abstract member SnapshotMarkDeletedAndAddAggregateEventsMd: Version -> Name -> EventId -> AggregateId -> 'F -> EventId -> Version -> Name -> AggregateId -> Metadata -> List<'F> -> Result<List<int>, string>
        abstract member SnapshotMarkDeletedAndMultiAddAggregateEventsMd: Metadata -> Version -> Name -> EventId -> AggregateId -> 'F -> List<EventId * List<'F> * Version * Name * AggregateId> -> Result<List<List<EventId>>, string>
        abstract member SnapshotAndMarkDeleted: Version -> Name -> EventId -> AggregateId -> 'F -> Result<unit, string>
        abstract member SnapshotAndMarkDeletedAsync: Version * Name * EventId * AggregateId * 'F * ?ct:CancellationToken -> Task<Result<unit, string>>

        abstract member AddAggregateEventsMd: EventId -> Version -> Name -> AggregateId ->  Metadata -> List<'F> -> Result<List<EventId>, string>
        abstract member AddAggregateEventsMdAsync: EventId * Version * Name * System.Guid * Metadata * List<'F> * ?ct:CancellationToken -> Task<Result<List<int>, string>> 

        abstract member MultiAddEventsMd:  Metadata -> List<EventId * List<'F> * Version * Name> -> Result<List<List<EventId>>, string>
        abstract member MultiAddAggregateEventsMd: Metadata -> List<EventId * List<'F> * Version * Name * AggregateId> -> Result<List<List<EventId>>, string>
        
        abstract member MultiAddAggregateEventsMdAsync: List<EventId * List<'F> * Version * Name * AggregateId> * Metadata * ?ct:CancellationToken -> Task<Result<List<List<EventId>>, string>>

        abstract member GetEventsAfterId: Version -> EventId -> Name -> Result< List< EventId * 'F >, string >

        abstract member GetAggregateEventsAfterId: Version ->  Name -> AggregateId -> EventId-> Result< List< EventId * 'F >, string >
        abstract member GetAggregateEventsAfterIdAsync: Version * Name * AggregateId * EventId * ?ct:CancellationToken -> Task<Result< List< EventId * 'F >, string >>
        abstract member GetAggregateEvents: Version ->  Name -> AggregateId -> Result< List< EventId * 'F >, string >
        abstract member GetAggregateEventsAsync: Version *  Name * AggregateId * ?ct:CancellationToken -> Task<Result< List< EventId * 'F >, string >>

        abstract member GetEventsInATimeInterval: Version -> Name -> DateTime -> DateTime -> Result<List<EventId * 'F >, string>
        abstract member GetEventsInATimeIntervalAsync: Version * Name * DateTime * DateTime * ?ct:CancellationToken -> Task<Result<List<EventId * 'F >, string>>
        
        abstract member GetAggregateEventsInATimeInterval: Version -> Name -> Guid -> DateTime -> DateTime -> Result<List<EventId * 'F >, string>
        abstract member GetAggregateEventsInATimeIntervalAsync: Version * Name * Guid * DateTime * DateTime * ?ct:CancellationToken -> Task<Result<List<EventId * 'F >, string>>
        abstract member GetMultipleAggregateEventsInATimeInterval: Version -> Name -> List<AggregateId> -> DateTime -> DateTime -> Result<List<EventId * AggregateId * 'F >, string>
        abstract member GetMultipleAggregateEventsInATimeIntervalAsync: Version * Name * List<AggregateId> * DateTime * DateTime * ?ct:CancellationToken -> Task<Result<List<EventId * AggregateId * 'F >, string>>
        abstract member GetAllAggregateEventsInATimeInterval: Version -> Name -> DateTime -> DateTime -> Result<List<EventId * 'F >, string>
        abstract member GetAllAggregateEventsInATimeIntervalAsync: Version * Name * DateTime * DateTime * ?ct: CancellationToken ->  Task<Result<ResizeArray<EventId * AggregateId * 'F >, string>>
        
        [<Obsolete("Use GetAllAggregateIds or GetAllAggregateIdsInATimeInterval")>]
        abstract member GetAggregateSnapshotsInATimeInterval: Version -> Name -> DateTime -> DateTime -> Result<List<int * AggregateId * DateTime * 'F >, string>
        abstract member GetAggregateIdsInATimeInterval: Version -> Name -> DateTime -> DateTime -> Result<List<AggregateId>, string>
        abstract member GetAggregateIds : Version -> Name -> Result<List<AggregateId>, string>
        
        abstract member GDPRReplaceSnapshotsAndEventsOfAnAggregate: Version -> Name -> AggregateId -> 'F -> 'F -> Result<unit, string>
        
    
    type IEventBroker<'F> =
        {
            notify: Option<Version -> Name -> List<EventId * 'F> -> List<Result<string, 'F>>>
            notifyAggregate: Option<Version -> Name -> AggregateId -> List<EventId * 'F> -> List<Result<string, string>>>
        }

    type RowReaderByFormat<'F> = RowReader -> string ->'F
