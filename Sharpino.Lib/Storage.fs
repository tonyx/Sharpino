namespace Sharpino
open System
open Sharpino.Utils
open Sharpino.Core
open Sharpino.Definitions
open Sharpino.Lib.Core.Commons
open FsToolkit.ErrorHandling

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

    type IStorage =
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

module Repositories =

    // todo: repository should implement this
    type IRepository<'A when 'A : equality and 'A :> Entity> =
        abstract member Add: 'A -> 'A
        abstract member AddMany: List<'A> -> 'A
        abstract member Remove: ('A -> bool) -> 'A
        abstract member Remove: Guid -> Result<'A, string>
        abstract member Update: 'A -> 'A
        abstract member Get: ('A -> bool) -> 'A option
        abstract member Get: Guid -> 'A option
        abstract member Exists: ('A -> bool) -> bool
        abstract member IsEmpty: unit -> bool
        abstract member GetAll: unit -> List<'A>

    type Repository<'A when 'A : equality and 'A :> Entity> =
        {
            Items: List<'A>
        }
        with 
            static member Create (items: List<'A>) =
                { Items = items }
            static member Zero = { Items = [] :> List<'A> }
            member this.Add (x: 'A) =
                { this with Items = x::this.Items }
            member this.AddMany (xs: List<'A>) =
                { this with Items = xs @ this.Items }
            member this.Remove (f: 'A -> bool) =
                { this with Items = this.Items |> List.filter (fun y -> not (f y)) }

                // Preferred way: Use Result
            member this.Remove (id: Guid) =
                ResultCE.result {
                    let! itemExist = 
                        this.Items 
                        |> List.tryFind (fun x -> x.Id = id)
                        |> Result.ofOption (sprintf "Item with id '%A' does not exist" id)
                    return
                        { this with Items = this.Items |> List.filter (fun x -> x.Id <> id)}
                }
            member this.Update (x: 'A) =
                { this with Items = this.Items |> List.map (fun y -> if y.Id = x.Id then x else y) }
            member this.Get (f: 'A -> bool) =
                this.Items |> List.tryFind f
            member this.Get id =
                this.Items |> List.tryFind (fun x -> x.Id = id)
            member this.Exists (f: 'A -> bool) =
                this.Items |> List.exists f
            member this.Exists id =
                this.Items |> List.exists (fun x -> x.Id = id)
            member this.IsEmpty () =
                this.Items |> List.isEmpty
            member this.GetAll () =
                this.Items

        
