namespace Sharpino
open System
open Sharpino.Utils
open Sharpino.Core
open Sharpino.Definitions
open Sharpino.Lib.Core.Commons
open FsToolkit.ErrorHandling
open log4net


module Storage =
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
        // toto: the following two can be unified
        abstract member TryGetLastSnapshotEventId: Version -> Name -> Option<EventId>
        abstract member TryGetLastSnapshotId: Version -> Name -> Option<EventId * SnapshotId>
        abstract member TryGetSnapshotById: Version -> Name -> int ->Option<EventId * Json>
        abstract member TryGetEvent: Version -> int -> Name -> Option<StorageEventJson>
        abstract member SetSnapshot: Version -> int * Json -> Name -> Result<unit, string>
        abstract member AddEvents: Version -> Name -> List<Json> -> Result<List<int>, string>
        abstract member MultiAddEvents:  List<List<Json> * Version * Name>  -> Result<List<List<int>>, string>
        abstract member GetEventsAfterId: Version -> int -> Name -> Result< List< EventId * Json >, string >
        abstract member GetEventsInATimeInterval: Version -> Name -> DateTime -> DateTime -> List<EventId * Json >

    type IEventBroker =
        {
            notify: Option<Version -> Name -> List<int * Json> -> Result< unit, string >>
        }
