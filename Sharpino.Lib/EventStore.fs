namespace Sharpino
open System
open Sharpino.Definitions
open log4net

module Storage =
    open Confluent.Kafka
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
    }
    
    type StorageAggregateSnapshot = {
        Id: int
        AggregateId: Guid
        Snapshot: Json
        TimeStamp: System.DateTime
        EventId: Option<EventId>
    }

    type StorageEvent<'E> =
        {
            Event: 'E
            Id: int
            Timestamp: System.DateTime
        }

    type KafkaOffset = int64
    type KafkaPartitionId = int

    [<Obsolete "use IEventStore and Postgres/InMemory">]
    type ILightStorage =
        abstract member AddEvents: Version -> List<'E> -> Name -> Result<unit, string>
        abstract member ResetEvents: Version -> Name -> unit
        abstract member ResetSnapshots: Version -> Name -> unit
        abstract member AddSnapshot: UInt64 -> Version -> 'A -> Name -> unit
        abstract member ConsumeEventsFromPosition: Version -> Name -> uint64 -> (uint64 * 'E) list
        abstract member TryGetLastSnapshot: Version -> Name -> Option<UInt64 * 'A>
        abstract member ConsumeEventsInATimeInterval: Version -> Name -> DateTime -> DateTime -> List<uint64 * 'E>

    type IEventStore<'F> =
        abstract member Reset: Version -> Name -> unit

        abstract member ResetAggregateStream: Version -> Name -> unit

        abstract member TryGetLastSnapshot: Version -> Name -> Option<SnapId * EventId * 'F>
        abstract member TryGetLastEventId: Version -> Name -> Option<EventId>

        abstract member TryGetLastAggregateEventId: Version -> Name -> AggregateId -> Option<EventId> 

        abstract member TryGetLastSnapshotEventId: Version -> Name -> Option<EventId>
        abstract member TryGetLastSnapshotId: Version -> Name -> Option<EventId * SnapshotId>
        abstract member TryGetLastSnapshotIdByAggregateId: Version -> Name -> Guid -> Option<Option<EventId> * SnapshotId>
        abstract member TryGetLastAggregateSnapshotEventId: Version -> Name -> AggregateId -> Option<EventId>

        abstract member TryGetSnapshotById: Version -> Name -> int ->Option<EventId * 'F>
        abstract member TryGetAggregateSnapshotById: Version -> Name -> AggregateId -> EventId ->Option<Option<EventId> * 'F>

        abstract member TryGetEvent: Version -> EventId -> Name -> Option<StoragePgEvent<'F>>
        abstract member SetSnapshot: Version -> EventId * 'F -> Name -> Result<unit, string>
        abstract member SetAggregateSnapshot: Version -> AggregateId * EventId * 'F -> Name -> Result<unit, string>

        abstract member SetInitialAggregateState: AggregateId ->  Version -> Name -> 'F ->  Result<unit, string>

        abstract member AddEvents: EventId -> Version -> Name ->  List<'F> -> Result<List<int>, string>

        abstract member SetInitialAggregateStateAndAddEvents: EventId -> AggregateId -> Version -> Name -> 'F -> Version -> Name -> List<'F> -> Result<List<int>, string>
        
        abstract member SetInitialAggregateStateAndAddAggregateEvents: EventId -> AggregateId -> Version -> Name -> AggregateId -> 'F -> Version -> Name -> List<'F> -> Result<List<int>, string>

        abstract member AddAggregateEvents: EventId -> Version -> Name -> AggregateId ->  List<'F> -> Result<List<EventId>, string>

        abstract member MultiAddEvents:  List<EventId * List<'F> * Version * Name>  -> Result<List<List<EventId>>, string>
        abstract member MultiAddAggregateEvents: List<EventId * List<'F> * Version * Name * AggregateId>  -> Result<List<List<EventId>>, string>

        abstract member GetEventsAfterId: Version -> EventId -> Name -> Result< List< EventId * 'F >, string >

        abstract member GetAggregateEventsAfterId: Version ->  Name -> Guid -> EventId-> Result< List< EventId * 'F >, string >
        abstract member GetAggregateEvents: Version ->  Name -> Guid -> Result< List< EventId * 'F >, string >

        abstract member GetEventsInATimeInterval: Version -> Name -> DateTime -> DateTime -> List<EventId * 'F >

    type IEventBroker<'F> =
        {
            notify: Option<Version -> Name -> List<EventId * 'F> -> List<DeliveryResult<string, 'F>>>
            notifyAggregate: Option<Version -> Name -> AggregateId -> List<EventId * 'F> -> List<DeliveryResult<string, string>>>
        }

    type RowReaderByFormat<'F> = RowReader -> string ->'F
