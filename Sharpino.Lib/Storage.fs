namespace Sharpino
open System

module Storage =
    type Json = string
    type Name = string
    type version = string
    type StorageEventJson =
        {
            JsonEvent: Json
            Id: int
            Timestamp: System.DateTime
        }
    type StorageSnapshot = {
        Id: int
        Snapshot: Json
        TimeStamp: System.DateTime
        EventId: int
    }

    type StorageEvent<'E> =
        {
            Event: 'E
            Id: int
            Timestamp: System.DateTime
        }

    type ILightStorage =
        abstract member AddEvents: version -> List<Json> -> Name -> Result<unit, string>
        abstract member ResetEvents: version -> Name -> unit
        abstract member ResetSnapshots: version -> Name -> unit
        abstract member AddSnapshot: UInt64 -> version -> Json -> Name -> unit
        abstract member ConsumeEventsFromPosition: version -> Name -> uint64 -> (uint64 * Json) list
        abstract member TryGetLastSnapshot: version -> Name -> Option<UInt64 * Json>

    type IStorage =
        abstract member Reset: version -> Name -> unit
        abstract member TryGetLastSnapshot: version -> Name -> Option<int * int * 'A>
        abstract member TryGetLastEventId: version -> Name -> Option<int>
        abstract member TryGetLastSnapshotEventId: version -> Name -> Option<int>
        abstract member TryGetLastSnapshotId: version -> Name -> Option<int>
        abstract member TryGetEvent: version -> int -> Name -> Option<StorageEvent<obj>>
        abstract member SetSnapshot: version -> int * 'A -> Name -> Result<unit, string>
        abstract member AddEvents: version -> List<'E> -> Name -> Result<unit, string>
        abstract member MultiAddEvents:  List<List<obj> * version * Name>  -> Result<unit, string>
        abstract member GetEventsAfterId: version -> int -> Name -> Result< List< int * 'E >, string >
        abstract member GetEventsInATimeInterval: version -> Name -> DateTime -> DateTime -> List<int * 'E >