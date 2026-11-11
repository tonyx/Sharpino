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
        { JsonEvent: 'T
          Id: int
          CorrelationId: Option<Guid>
          CausationId: Option<Guid>
          Timestamp: System.DateTime }

    type StorageEventJsonRef =
        { AggregateId: Guid
          JsonEvent: Json
          Id: int
          CorrelationId: Option<Guid>
          CausationId: Option<Guid>
          KafkaOffset: Option<int64>
          KafkaPartition: Option<int>
          Timestamp: System.DateTime }

    type StorageSnapshot =
        { Id: int
          Snapshot: Json
          TimeStamp: System.DateTime
          EventId: int
        // Deleted: bool
        }

    type StorageAggregateSnapshot =
        { Id: int
          AggregateId: Guid
          Snapshot: Json
          TimeStamp: System.DateTime
          EventId: Option<EventId>
          Deleted: bool }

    type StorageEvent<'E> =
        { Event: 'E
          Id: int
          Timestamp: System.DateTime }


    type KafkaOffset = int64
    type KafkaPartitionId = int
    type Metadata = string // this needs to be moved

    type IEventStore<'F> =
        abstract member Reset: Version -> Name -> unit

        abstract member ResetAggregateStream: Version -> Name -> unit

        [<Obsolete("Use Async methods instead")>]
        abstract member TryGetLastSnapshot: Version -> Name -> Option<SnapId * EventId * 'F>

        abstract member TryGetLastSnapshotAsync:
            Version * Name * ?ct: CancellationToken -> Task<Option<SnapId * EventId * 'F>>

        [<Obsolete("Use Async methods instead")>]
        abstract member TryGetLastEventId: Version -> Name -> Option<EventId>

        abstract member TryGetLastEventIdAsync:
            Version * Name * ?ct: CancellationToken -> Task<Option<EventId>>

        [<Obsolete("Use Async methods instead")>]
        abstract member TryGetLastAggregateEventId: Version -> Name -> AggregateId -> Option<EventId>

        abstract member TryGetLastAggregateEventIdAsync:
            Version * Name * AggregateId * ?ct: CancellationToken -> Task<Option<EventId>>

        abstract member TryGetLastSnapshotEventId: Version -> Name -> Option<EventId>
        abstract member TryGetLastSnapshotId: Version -> Name -> Option<EventId * SnapshotId>

        abstract member TryGetLastSnapshotIdByAggregateId:
            Version -> Name -> Guid -> Option<Option<EventId> * SnapshotId>

        abstract member TryGetLastHistorySnapshotIdByAggregateId:
            Version -> Name -> Guid -> Option<Option<EventId> * SnapshotId>

        abstract member TryGetLastAggregateSnapshotEventId: Version -> Name -> AggregateId -> Option<EventId>

        [<Obsolete("Use Async methods instead")>]
        abstract member TryGetFirstSnapshot: Version -> Name -> AggregateId -> Result<SnapId * 'F, string>

        abstract member TryGetFirstSnapshotAsync:
            Version * Name * AggregateId * ?ct: CancellationToken -> Task<Result<SnapId * 'F, string>>

        [<Obsolete("Use Async methods instead")>]
        abstract member TryGetLastAggregateSnapshot:
            Version -> Name -> AggregateId -> Result<Option<EventId> * 'F, string>

        abstract member TryGetLastAggregateSnapshotAsync:
            Version * Name * AggregateId * ?ct: CancellationToken -> Task<Result<Option<EventId> * 'F, string>>

        abstract member GetDistanceFromLatestSnapshotAsync:
            Version * Name * AggregateId * ?ct: CancellationToken -> Task<int>

        [<Obsolete("Use Async methods instead")>]
        abstract member TryGetSnapshotById: Version -> Name -> int -> Option<EventId * 'F>

        [<Obsolete("Use Async methods instead")>]
        abstract member TryGetAggregateSnapshotById:
            Version -> Name -> AggregateId -> EventId -> Option<Option<EventId> * 'F>

        [<Obsolete("Use Async methods instead")>]
        abstract member TryGetEvent: Version -> EventId -> Name -> Option<StoragePgEvent<'F>>

        abstract member TryGetEventAsync:
            Version * EventId * Name * ?ct: CancellationToken -> Task<Option<StoragePgEvent<'F>>>

        abstract member SetSnapshot: Version -> EventId * 'F -> Name -> Result<unit, string>
        abstract member SetAggregateSnapshot: Version -> AggregateId * EventId * 'F -> Name -> Result<unit, string>

        [<Obsolete("Use Async methods instead")>]
        abstract member AddEventsMd: EventId -> Version -> Name -> Metadata -> List<'F> -> Result<List<int>, string>

        [<Obsolete("Use Async methods instead")>]
        abstract member AddEventsWithCausalityMd:
            EventId -> Version -> Name -> Metadata -> List<'F> -> Option<Guid> -> Option<Guid> -> Result<List<int>, string>

        abstract member AddEventsMdAsync:
            EventId * Version * Name * Metadata * List<'F> * ?ct: CancellationToken -> Task<Result<List<int>, string>>

        abstract member AddEventsMdAsync:
            EventId * Version * Name * Metadata * List<'F> * Option<Guid> * Option<Guid> * ?ct: CancellationToken -> Task<Result<List<int>, string>>

        [<Obsolete("Use Async methods instead")>]
        abstract member SetInitialAggregateState: AggregateId -> Version -> Name -> 'F -> Result<unit, string>

        abstract member SetInitialAggregateStateAsync:
            AggregateId * Version * Name * 'F * ?ct: CancellationToken -> Task<Result<unit, string>>

        [<Obsolete("Use Async methods instead")>]
        abstract member SetInitialAggregateStates: Version -> Name -> (AggregateId * 'F)[] -> Result<unit, string>

        abstract member SetInitialAggregateStatesAsync:
            Version * Name * (AggregateId * 'F)[] * ?ct: CancellationToken -> Task<Result<unit, string>>

        [<Obsolete("Use Async methods instead")>]
        abstract member SetInitialAggregateStateAndAddEventsMd:
            EventId ->
            AggregateId ->
            Version ->
            Name ->
            'F ->
            Version ->
            Name ->
            Metadata ->
            List<'F> ->
                Result<List<int>, string>

        [<Obsolete("Use Async methods instead")>]
        abstract member SetInitialAggregateStateAndMultiAddAggregateEventsMd:
            AggregateId ->
            Version ->
            Name ->
            'F ->
            Metadata ->
            List<EventId * List<'F> * Version * Name * AggregateId> ->
                Result<List<List<EventId>>, string>

        abstract member SetInitialAggregateStateAndMultiAddAggregateEventsMdAsync:
            AggregateId *
            Version *
            Name *
            'F *
            Metadata *
            List<EventId * List<'F> * Version * Name * AggregateId> *
            ?ct: CancellationToken ->
                Task<Result<List<List<EventId>>, string>>

        [<Obsolete("Use Async methods instead")>]
        abstract member SetInitialAggregateStateAndAddAggregateEventsMd:
            EventId ->
            AggregateId ->
            Version ->
            Name ->
            AggregateId ->
            'F ->
            Version ->
            Name ->
            Metadata ->
            List<'F> ->
                Result<List<int>, string>

        abstract member SetInitialAggregateStateAndAddAggregateEventsMdAsync:
            EventId *
            AggregateId *
            Version *
            Name *
            AggregateId *
            'F *
            Version *
            Name *
            Metadata *
            List<'F> *
            ?ct: CancellationToken ->
                Task<Result<List<int>, string>>

        [<Obsolete("Use Async methods instead")>]
        abstract member SnapshotMarkDeletedAndAddAggregateEventsMd:
            Version ->
            Name ->
            EventId ->
            AggregateId ->
            'F ->
            EventId ->
            Version ->
            Name ->
            AggregateId ->
            Metadata ->
            List<'F> ->
                Result<List<int>, string>

        [<Obsolete("Use Async methods instead")>]
        abstract member SnapshotMarkDeletedAndMultiAddAggregateEventsMd:
            Metadata ->
            Version ->
            Name ->
            EventId ->
            AggregateId ->
            'F ->
            List<EventId * List<'F> * Version * Name * AggregateId> ->
                Result<List<List<EventId>>, string>

        [<Obsolete("Use Async methods instead")>]
        abstract member SnapshotAndMarkDeleted: Version -> Name -> EventId -> AggregateId -> 'F -> Result<unit, string>

        abstract member SnapshotAndMarkDeletedAsync:
            Version * Name * EventId * AggregateId * 'F * ?ct: CancellationToken -> Task<Result<unit, string>>

        [<Obsolete("Use Async methods instead")>]
        abstract member AddAggregateEventsMd:
            EventId -> Version -> Name -> AggregateId -> Metadata -> List<'F> -> Result<List<EventId>, string>

        [<Obsolete("Use Async methods instead")>]
        abstract member AddAggregateEventsWithCausalityMd:
            EventId -> Version -> Name -> AggregateId -> Metadata -> List<'F> -> Option<Guid> -> Option<Guid> -> Result<List<EventId>, string>

        abstract member AddAggregateEventsMdAsync:
            EventId * Version * Name * System.Guid * Metadata * List<'F> * ?ct: CancellationToken ->
                Task<Result<List<int>, string>>

        abstract member AddAggregateEventsMdAsync:
            EventId * Version * Name * System.Guid * Metadata * List<'F> * Option<Guid> * Option<Guid> * ?ct: CancellationToken ->
                Task<Result<List<int>, string>>

        abstract member AddAggregateEventsMdAsync2:
            EventId * Version * Name * System.Guid * Metadata * List<'F> * extraConstraints:Map<System.Guid * string, EventId> * ?ct: CancellationToken ->
                Task<Result<List<int>, string>>

        abstract member AddAggregateEventsMdAsync2:
            EventId * Version * Name * System.Guid * Metadata * List<'F> * extraConstraints:Map<System.Guid * string, EventId> * Option<Guid> * Option<Guid> * ?ct: CancellationToken ->
                Task<Result<List<int>, string>>

        [<Obsolete("Use Async methods instead")>]
        abstract member MultiAddEventsMd:
            Metadata -> List<EventId * List<'F> * Version * Name> -> Result<List<List<EventId>>, string>

        [<Obsolete("Use Async methods instead")>]
        abstract member MultiAddAggregateEventsMd:
            Metadata -> List<EventId * List<'F> * Version * Name * AggregateId> -> Result<List<List<EventId>>, string>

        abstract member MultiAddAggregateEventsMdAsync:
            List<EventId * List<'F> * Version * Name * AggregateId> * Metadata * ?ct: CancellationToken ->
                Task<Result<List<List<EventId>>, string>>

        abstract member MultiAddAggregateEventsMdAsync2:
            List<EventId * List<'F> * Version * Name * AggregateId> * Metadata * extraConstraints:Map<AggregateId * string, EventId> * ?ct: CancellationToken ->
                Task<Result<List<List<EventId>>, string>>

        [<Obsolete("Use Async methods instead")>]
        abstract member GetEventsAfterId: Version -> EventId -> Name -> Result<List<EventId * 'F>, string>

        abstract member GetEventsAfterIdAsync:
            Version * EventId * Name * ?ct: CancellationToken -> Task<Result<List<EventId * 'F>, string>>

        [<Obsolete("Use Async methods instead")>]
        abstract member GetAggregateEventsAfterId:
            Version -> Name -> AggregateId -> EventId -> Result<List<EventId * 'F>, string>

        abstract member GetAggregateEventsAfterIdAsync:
            Version * Name * AggregateId * EventId * ?ct: CancellationToken -> Task<Result<List<EventId * 'F>, string>>

        [<Obsolete("Use Async methods instead")>]
        abstract member GetAggregateEvents: Version -> Name -> AggregateId -> Result<List<EventId * 'F>, string>

        abstract member GetAggregateEventsAsync:
            Version * Name * AggregateId * ?ct: CancellationToken -> Task<Result<List<EventId * 'F>, string>>

        [<Obsolete("Use Async methods instead")>]
        abstract member GetEventsInATimeInterval:
            Version -> Name -> DateTime -> DateTime -> Result<List<EventId * 'F>, string>

        abstract member GetEventsInATimeIntervalAsync:
            Version * Name * DateTime * DateTime * ?ct: CancellationToken -> Task<Result<List<EventId * 'F>, string>>

        [<Obsolete("Use Async methods instead")>]
        abstract member GetAggregateEventsInATimeInterval:
            Version -> Name -> Guid -> DateTime -> DateTime -> Result<List<EventId * 'F>, string>

        abstract member GetAggregateEventsInATimeIntervalAsync:
            Version * Name * Guid * DateTime * DateTime * ?ct: CancellationToken ->
                Task<Result<List<EventId * 'F>, string>>

        [<Obsolete("Use Async methods instead")>]
        abstract member GetMultipleAggregateEventsInATimeInterval:
            Version ->
            Name ->
            List<AggregateId> ->
            DateTime ->
            DateTime ->
                Result<List<EventId * AggregateId * 'F>, string>

        abstract member GetMultipleAggregateEventsInATimeIntervalAsync:
            Version * Name * List<AggregateId> * DateTime * DateTime * ?ct: CancellationToken ->
                Task<Result<List<EventId * AggregateId * 'F>, string>>

        [<Obsolete("Use Async methods instead")>]
        abstract member GetAllAggregateEventsInATimeInterval:
            Version -> Name -> DateTime -> DateTime -> Result<List<EventId * 'F>, string>

        abstract member GetAllAggregateEventsInATimeIntervalAsync:
            Version * Name * DateTime * DateTime * ?ct: CancellationToken ->
                Task<Result<ResizeArray<EventId * AggregateId * 'F>, string>>

        [<Obsolete("Use GetAllAggregateIds or GetAllAggregateIdsInATimeInterval")>]
        abstract member GetAggregateSnapshotsInATimeInterval:
            Version -> Name -> DateTime -> DateTime -> Result<List<int * AggregateId * DateTime * 'F>, string>

        [<Obsolete("Use Async methods instead")>]
        abstract member GetAggregateIdsInATimeInterval:
            Version -> Name -> DateTime -> DateTime -> Result<List<AggregateId>, string>

        abstract member GetAggregateIdsInATimeIntervalAsync:
            Version * Name * DateTime * DateTime * ?ct: CancellationToken -> Task<Result<List<AggregateId>, string>>

        [<Obsolete("Use Async methods instead")>]
        abstract member GetAggregateIds: Version -> Name -> Result<List<AggregateId>, string>
        [<Obsolete("Use Async methods instead")>]
        abstract member GetUndeletedAggregateIds: Version -> Name -> Result<List<AggregateId>, string>

        abstract member GetAggregateIdsAsync:
            Version * Name * ?ct: CancellationToken -> Task<Result<List<AggregateId>, string>>

        abstract member GetUndeletedAggregateIdsAsync:
            Version * Name * ?ct: CancellationToken -> Task<Result<List<AggregateId>, string>>

        [<Obsolete("Use Async methods instead")>]
        abstract member GDPRReplaceSnapshotsAndEventsOfAnAggregate:
            Version -> Name -> AggregateId -> 'F -> 'F -> Result<unit, string>

        [<Obsolete("Use Async methods instead")>]
        abstract member GDPRReplaceEventsByPredicate:
            Version -> Name -> AggregateId -> ('F -> Result<bool, string>) -> 'F -> Result<unit, string>

        [<Obsolete("Use Async methods instead")>]
        abstract member GDPRPartialUpdateSnapshots:
            Version -> Name -> AggregateId -> ('F -> Result<'F, string>) -> Result<unit, string>

        abstract member GDPRPartialUpdateSnapshotsAsync:
            Version * Name * AggregateId * ('F -> Result<'F, string>) * ?ct: CancellationToken ->
                Task<Result<unit, string>>

        abstract member GDPRReplaceEventsByPredicateAsync:
            Version * Name * AggregateId * ('F -> Result<bool, string>) * 'F * ?ct: CancellationToken ->
                Task<Result<unit, string>>

        abstract member BulkSnapshotsUpcast:
            Version * Name * ('F -> Result<'F, string>) * ?ct: CancellationToken -> Task<Result<int, string>>

        [<Obsolete("Use Async methods instead")>]
        abstract member PatchEventWithNoOp:
            Version -> Name -> AggregateId -> EventId -> 'F -> Result<unit, string>

        abstract member PatchEventWithNoOpAsync:
            Version * Name * AggregateId * EventId * 'F * ?ct: CancellationToken -> Task<Result<unit, string>>

        [<Obsolete("Use Async methods instead")>]
        abstract member PatchContextEventWithNoOp:
            Version -> Name -> EventId -> 'F -> Result<unit, string>

        abstract member PatchContextEventWithNoOpAsync:
            Version * Name * EventId * 'F * ?ct: CancellationToken -> Task<Result<unit, string>>

        [<Obsolete("Use Async methods instead")>]
        abstract member DeleteSnapshotsFromEventId:
            Version -> Name -> AggregateId -> EventId -> Result<unit, string>

        abstract member DeleteSnapshotsFromEventIdAsync:
            Version * Name * AggregateId * EventId * ?ct: CancellationToken -> Task<Result<unit, string>>

        [<Obsolete("Use Async methods instead")>]
        abstract member FindEventsByCorrelationId:
            Version -> Name -> Guid -> Result<List<AggregateId * EventId * 'F>, string>

        abstract member FindEventsByCorrelationIdAsync:
            Version * Name * Guid * ?ct: CancellationToken -> Task<Result<List<AggregateId * EventId * 'F>, string>>

    // this must be deleted
    type IEventBroker<'F> =
        { notify: Option<Version -> Name -> List<EventId * 'F> -> List<Result<string, 'F>>>
          notifyAggregate: Option<Version -> Name -> AggregateId -> List<EventId * 'F> -> List<Result<string, string>>> }

    type RowReaderByFormat<'F> = RowReader -> string -> 'F
