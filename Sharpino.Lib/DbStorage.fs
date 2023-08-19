namespace Sharpino

open FsToolkit.ErrorHandling
open FSharp.Data.Sql
open Npgsql.FSharp
open FSharpPlus
open Sharpino
open System

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
    abstract member AddSnapshot: UInt64 -> version -> Json -> Name
    // abstract member ConsumeEvents: version -> name

module DbStorage =
    type PgDb(connection) =
        interface IStorage with
            member this.Reset version name =
                if (Conf.isTestEnv) then
                    // additional precautions to avoid deleting data in non dev/test env 
                    // is configuring the db user rights in prod accordingly (only read and write/append)
                    let res1 =
                        connection
                        |> Sql.connect
                        |> Sql.query (sprintf "DELETE from snapshots%s%s" version name)
                        |> Sql.executeNonQuery
                    let res2 =
                        connection
                        |> Sql.connect
                        |> Sql.query (sprintf "DELETE from events%s%s" version name)
                        |> Sql.executeNonQuery
                    ()
                else
                    failwith "operation allowed only in test db"

            member this.TryGetLastSnapshot version name =
                let query = sprintf "SELECT id, event_id, snapshot FROM snapshots%s%s ORDER BY id DESC LIMIT 1" version name
                connection
                |> Sql.connect
                |> Sql.query query
                |> Sql.executeAsync (fun read ->
                    (
                        read.int "id",
                        read.int "event_id",
                        read.text "snapshot"
                    )
                )
                |> Async.AwaitTask
                |> Async.RunSynchronously
                |> Seq.tryHead

            member this.TryGetLastSnapshotId version name =
                let query = sprintf "SELECT id FROM snapshots%s%s ORDER BY id DESC LIMIT 1" version name
                connection
                |> Sql.connect
                |> Sql.query query
                |> Sql.executeAsync (fun read ->
                    (
                        read.int "id"
                    )
                )
                |> Async.AwaitTask
                |> Async.RunSynchronously
                |> Seq.tryHead

            member this.TryGetLastEventId version name =
                let query = sprintf "SELECT id FROM events%s%s ORDER BY id DESC LIMIT 1" version name
                connection
                |> Sql.connect
                |> Sql.query query 
                |> Sql.executeAsync  (fun read -> read.int "id")
                |> Async.AwaitTask
                |> Async.RunSynchronously
                |> Seq.tryHead

            member this.TryGetLastSnapshotEventId version name =
                let query = sprintf "SELECT event_id FROM snapshots%s%s ORDER BY id DESC LIMIT 1" version name
                connection
                |> Sql.connect
                |> Sql.query query
                |> Sql.executeAsync  (fun read -> read.int "event_id")
                |> Async.AwaitTask
                |> Async.RunSynchronously
                |> Seq.tryHead

            member this.TryGetEvent version id name =
                let query = sprintf "SELECT * from events%s%s where id = @id" version name
                connection
                |> Sql.connect
                |> Sql.query query 
                |> Sql.parameters ["id", Sql.int id]
                |> Sql.executeAsync
                    (
                        fun read ->
                        {
                            Id = read.int "id"
                            Event = read.string "event"
                            Timestamp = read.dateTime "timestamp"
                        }
                    )
                    |> Async.AwaitTask
                    |> Async.RunSynchronously
                    |> Seq.tryHead

            member this.SetSnapshot version (id: int, snapshot: Json) name =
                let command = sprintf "INSERT INTO snapshots%s%s (event_id, snapshot, timestamp) VALUES (@event_id, @snapshot, @timestamp)" version name
                ResultCE.result
                    {
                        let! event = ((this :> IStorage).TryGetEvent version id name) |> Result.ofOption "event not found"
                        let _ =
                            connection
                            |> Sql.connect
                            |> Sql.executeTransactionAsync
                                [
                                    command,
                                        [
                                            [
                                                ("@event_id", Sql.int event.Id);
                                                ("snapshot",  Sql.jsonb snapshot);
                                                ("timestamp", Sql.timestamp event.Timestamp)
                                            ]
                                        ]
                                ]
                            |> Async.AwaitTask
                            |> Async.RunSynchronously
                        return ()
                    }

            member this.AddEvents version (events: List<Json>) name =
                let command = sprintf "INSERT INTO events%s%s (event, timestamp) VALUES (@event, @timestamp)" version name
                try
                    let _ =
                        connection
                        |> Sql.connect
                        |> Sql.executeTransactionAsync
                            [
                                command,
                                events
                                |>>
                                (
                                    fun x ->
                                        [
                                            ("@event", Sql.jsonb x);
                                            ("timestamp", Sql.timestamp (System.DateTime.Now))
                                        ]
                                )
                            ]
                            |> Async.AwaitTask
                            |> Async.RunSynchronously
                    () |> Ok
                with
                    | _ as ex -> (ex.ToString()) |> Error

            member this.MultiAddEvents (arg: List<List<Json> * version * Name>) : Result<unit,string> = 
                let cmdList = 
                    arg 
                    |> List.map 
                        (
                            fun (events, version,  name) -> 
                                let statement = sprintf "INSERT INTO events%s%s (event, timestamp) VALUES (@event, @timestamp)" version name
                                statement, events
                                |>> 
                                (
                                    fun x ->
                                        [
                                            ("@event", Sql.jsonb x);
                                            ("timestamp", Sql.timestamp (System.DateTime.Now))
                                        ]
                                )
                        )
                try 
                    let _ =
                        connection
                        |> Sql.connect
                        |> Sql.executeTransactionAsync cmdList
                        |> Async.AwaitTask
                        |> Async.RunSynchronously
                    () |> Ok
                with
                    | _ as ex -> (ex.ToString()) |> Error

            member this.GetEventsAfterId version id name =    
                let query = sprintf "SELECT id, event FROM events%s%s WHERE id > @id ORDER BY id" version name
                connection
                |> Sql.connect
                |> Sql.query query
                |> Sql.parameters ["id", Sql.int id]
                |> Sql.executeAsync ( fun read ->
                    (
                        read.int "id",
                        read.text "event"
                    )
                )
                |> Async.AwaitTask
                |> Async.RunSynchronously
                |> Seq.toList

