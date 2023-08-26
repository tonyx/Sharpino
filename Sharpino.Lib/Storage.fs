namespace Sharpino
open System

module Storage =
    type Json = string
    type Name = string
    type version = string
    type StorageEvent =
        {
            Event: Json
            Id: int
            Timestamp: System.DateTime
        }
    type StorageSnapshot = {
        Id: int
        Snapshot: Json
        TimeStamp: System.DateTime
        EventId: int
    }

    type StorageEventRef<'E> =
        {
            EventRef: 'E
            Id: int
            Timestamp: System.DateTime
        }
    type StorageSnapshotRef<'A> = {
        Id: int
        SnapshotRef: 'A
        TimeStamp: System.DateTime
        EventId: int
    }


    type IStorage =
        abstract member Reset: version -> Name -> unit
        abstract member TryGetLastSnapshot: version -> Name -> Option<int * int * Json>
        abstract member TryGetLastEventId: version -> Name -> Option<int>
        abstract member TryGetLastSnapshotEventId: version -> Name -> Option<int>
        abstract member TryGetLastSnapshotId: version -> Name -> Option<int>
        abstract member TryGetEvent: version -> int -> Name -> Option<StorageEvent>
        abstract member SetSnapshot: version -> int * Json -> Name -> Result<unit, string>
        abstract member AddEvents: version -> List<Json> -> Name -> Result<unit, string>
        abstract member MultiAddEvents:  List<List<Json> * version * Name>  -> Result<unit, string>
        abstract member GetEventsAfterId: version -> int -> Name -> List<int * string >
    type ILightStorage =
        abstract member AddEvents: version -> List<Json> -> Name -> unit
        abstract member ResetEvents: version -> Name -> unit
        abstract member ResetSnapshots: version -> Name -> unit
        abstract member AddSnapshot: UInt64 -> version -> Json -> Name -> unit
        abstract member ConsumeEvents: version -> Name -> (uint64 * Json) list
        abstract member GetLastSnapshot: version -> Name -> Option<UInt64 * Json>

    type IStorageRefactor =
        abstract member Reset: version -> Name -> unit
        abstract member TryGetLastSnapshot: version -> Name -> Option<int * int * 'A>
        abstract member TryGetLastEventId: version -> Name -> Option<int>
        abstract member TryGetLastSnapshotEventId: version -> Name -> Option<int>
        abstract member TryGetLastSnapshotId: version -> Name -> Option<int>
        abstract member TryGetEvent: version -> int -> Name -> Option<StorageEventRef<obj>>
        abstract member SetSnapshot: version -> int * 'A -> Name -> Result<unit, string>
        abstract member AddEvents: version -> List<'E> -> Name -> Result<unit, string>
        abstract member MultiAddEvents:  List<List<obj> * version * Name>  -> Result<unit, string>
        abstract member GetEventsAfterId: version -> int -> Name -> List<int * 'E >