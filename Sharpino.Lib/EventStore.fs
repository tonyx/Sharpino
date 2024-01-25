namespace Sharpino
open System
// open Sharpino.Utils
// open Sharpino.Core
open Sharpino.Definitions
// open Sharpino.Lib.Core.Commons
// open FsToolkit.ErrorHandling
open log4net

module Storage =
    type StorageEventJson =
        {
            JsonEvent: Json
            Id: int
            KafkaOffset: Option<int64>
            KafkaPartition: Option<int>
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
        EventId: Option<int>
    }

    type StorageEvent<'E> =
        {
            Event: 'E
            Id: int
            Timestamp: System.DateTime
        }

    type KafkaOffset = int64
    type KafkaPartitionId = int

    type ILightStorage =
        abstract member AddEvents: Version -> List<'E> -> Name -> Result<unit, string>
        abstract member ResetEvents: Version -> Name -> unit
        abstract member ResetSnapshots: Version -> Name -> unit
        abstract member AddSnapshot: UInt64 -> Version -> 'A -> Name -> unit
        abstract member ConsumeEventsFromPosition: Version -> Name -> uint64 -> (uint64 * 'E) list
        abstract member TryGetLastSnapshot: Version -> Name -> Option<UInt64 * 'A>
        abstract member ConsumeEventsInATimeInterval: Version -> Name -> DateTime -> DateTime -> List<uint64 * 'E>

    type IEventStore =
        abstract member Reset: Version -> Name -> unit
        abstract member TryGetLastSnapshot: Version -> Name -> Option<SnapId * EventId * Json>
        abstract member TryGetLastEventId: Version -> Name -> Option<EventId>
        abstract member TryGetLastEventIdWithKafkaOffSet: Version -> Name -> Option<EventId * Option<KafkaOffset> * Option<KafkaPartitionId>>
        abstract member TryGetLastEventIdByAggregateIdWithKafkaOffSet: Version -> Name -> AggregateId -> Option<EventId * Option<KafkaOffset> * Option<KafkaPartitionId>>
        // todo: the following two can be unified
        abstract member TryGetLastSnapshotEventId: Version -> Name -> Option<EventId>
        abstract member TryGetLastSnapshotId: Version -> Name -> Option<EventId * SnapshotId>
        abstract member TryGetLastSnapshotIdByAggregateId: Version -> Name -> Guid -> Option<Option<EventId> * SnapshotId>

        abstract member TryGetSnapshotById: Version -> Name -> int ->Option<EventId * Json>
        abstract member TryGetAggregateSnapshotById: Version -> Name -> AggregateId -> EventId ->Option<Option<EventId> * Json>
        abstract member TryGetEvent: Version -> EventId -> Name -> Option<StorageEventJson>
        abstract member SetSnapshot: Version -> EventId * Json -> Name -> Result<unit, string>
        abstract member SetAggregateSnapshot: Version -> AggregateId * EventId * Json -> Name -> Result<unit, string>
        abstract member SetInitialAggregateState: AggregateId -> Version -> Name -> Json ->  Result<unit, string>
        abstract member AddEvents: Version -> Name -> List<Json> -> Result<List<int>, string>

        abstract member AddAggregateEvents: Version -> Name -> Guid -> List<Json> -> Result<List<EventId>, string>

        abstract member MultiAddEvents:  List<List<Json> * Version * Name>  -> Result<List<List<EventId>>, string>
        abstract member MultiAddAggregateEvents:  List<List<Json> * Version * Name * Guid>  -> Result<List<List<EventId>>, string>

        abstract member GetEventsAfterId: Version -> EventId -> Name -> Result< List< EventId * Json >, string >

        abstract member GetAggregateEventsAfterId: Version ->  Name -> Guid -> EventId-> Result< List< EventId * Json >, string >
        abstract member GetAggregateEvents: Version ->  Name -> Guid -> Result< List< EventId * Json >, string >

        abstract member GetEventsInATimeInterval: Version -> Name -> DateTime -> DateTime -> List<EventId * Json >
        abstract member SetPublished: Version -> Name -> EventId -> KafkaOffset -> KafkaPartitionId ->  Result<unit, string>

    type IEventBroker =
        {
            notify: Option<Version -> Name -> List<EventId * Json> -> Result<List<Confluent.Kafka.DeliveryResult<Confluent.Kafka.Null,string>>, string >>
        }

