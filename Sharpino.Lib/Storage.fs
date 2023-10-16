namespace Sharpino
open System
open Sharpino.Utils

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
        abstract member AddEvents: version -> List<'E> -> Name -> Result<unit, string>
        abstract member ResetEvents: version -> Name -> unit
        abstract member ResetSnapshots: version -> Name -> unit
        abstract member AddSnapshot: UInt64 -> version -> 'A -> Name -> unit
        abstract member ConsumeEventsFromPosition: version -> Name -> uint64 -> (uint64 * 'E) list
        abstract member TryGetLastSnapshot: version -> Name -> Option<UInt64 * 'A>
        abstract member ConsumeEventsInATimeInterval: version -> Name -> DateTime -> DateTime -> List<uint64 * 'E>

    type IStorage =
        abstract member Reset: version -> Name -> unit
        abstract member TryGetLastSnapshot: version -> Name -> Option<int * int * Json>
        abstract member TryGetLastEventId: version -> Name -> Option<int>
        abstract member TryGetLastSnapshotEventId: version -> Name -> Option<int>
        abstract member TryGetLastSnapshotId: version -> Name -> Option<int>
        abstract member TryGetEvent: version -> int -> Name -> Option<StorageEventJson>
        abstract member SetSnapshot: version -> int * Json -> Name -> Result<unit, string>
        abstract member AddEvents: version -> List<Json> -> Name -> Result<unit, string>
        abstract member MultiAddEvents:  List<List<Json> * version * Name>  -> Result<unit, string>
        abstract member GetEventsAfterId: version -> int -> Name -> Result< List< int * Json >, string >
        abstract member GetEventsInATimeInterval: version -> Name -> DateTime -> DateTime -> List<int * Json >
