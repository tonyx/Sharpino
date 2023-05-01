namespace Tonyx.EventSourcing

open FSharp.Data.Sql
open Npgsql.FSharp
open FSharpPlus
open Tonyx.EventSourcing.Utils
open Tonyx.EventSourcing

type Json = string
type Name = string

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
    abstract member Reset: Name -> unit
    abstract member TryGetLastSnapshot: Name -> Option<int * int * Json>
    abstract member TryGetLastEventId: Name -> Option<int>
    abstract member TryGetLastSnapshotEventId: Name -> Option<int>
    abstract member TryGetLastSnapshotId: Name -> Option<int>
    abstract member TryGetEvent: int -> Name -> Option<StorageEvent>
    abstract member SetSnapshot: int * Json -> Name -> Result<unit, string>
    abstract member AddEvents: List<Json> -> Name -> Result<unit, string>
    abstract member MultiAddEvents: (List<Json> * Name) -> (List<Json> * Name)  -> Result<unit, string>
    abstract member GetEventsAfterId: int -> Name -> List<int * string >

module DbStorage =
    let TPConnectionString = Conf.connectionString
    let ceResult = CeResultBuilder()
    type PgDb() =
        interface IStorage with
            member this.Reset name =
                if (Conf.isTestEnv) then
                    // additional precautions to avoid deleting data in non dev/test env 
                    // is configuring the db user rights in prod accordingly (only read and write/append)
                    let _ =
                        TPConnectionString
                        |> Sql.connect
                        |> Sql.query (sprintf "DELETE from snapshots%s" name)
                        |> Sql.executeNonQuery
                    let _ =
                        TPConnectionString
                        |> Sql.connect
                        |> Sql.query (sprintf "DELETE from events%s" name)
                        |> Sql.executeNonQuery
                    ()
                else
                    failwith "operation allowed only in test db"

            member this.TryGetLastSnapshot name =
                let query = sprintf "SELECT id, event_id, snapshot FROM snapshots%s ORDER BY id DESC LIMIT 1" name
                TPConnectionString
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

            member this.TryGetLastSnapshotId name =
                let query = sprintf "SELECT id FROM snapshots%s ORDER BY id DESC LIMIT 1" name
                TPConnectionString
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

            member this.TryGetLastEventId name =
                let query = sprintf "SELECT id FROM events%s ORDER BY id DESC LIMIT 1" name
                TPConnectionString
                |> Sql.connect
                |> Sql.query query 
                |> Sql.executeAsync  (fun read -> read.int "id")
                |> Async.AwaitTask
                |> Async.RunSynchronously
                |> Seq.tryHead

            member this.TryGetLastSnapshotEventId name =
                let query = sprintf "SELECT event_id FROM snapshots%s ORDER BY id DESC LIMIT 1" name
                TPConnectionString
                |> Sql.connect
                |> Sql.query query
                |> Sql.executeAsync  (fun read -> read.int "event_id")
                |> Async.AwaitTask
                |> Async.RunSynchronously
                |> Seq.tryHead

            member this.TryGetEvent id name =
                let query = sprintf "SELECT * from events%s where id = @id" name
                TPConnectionString
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

            member this.SetSnapshot (id: int, snapshot: Json) name =
                let command = sprintf "INSERT INTO snapshots%s (event_id, snapshot, timestamp) VALUES (@event_id, @snapshot, @timestamp)" name
                ceResult
                    {
                        let! event = ((this :> IStorage).TryGetEvent id name) |> optionToResult
                        let _ =
                            TPConnectionString
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

            member this.AddEvents (events: List<Json>) name =
                let command = sprintf "INSERT INTO events%s (event, timestamp) VALUES (@event, @timestamp)" name
                try
                    let _ =
                        TPConnectionString
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

            member this.MultiAddEvents(arg1: List<Json> * Name) (arg2: List<Json> * Name): Result<unit,string> = 
                let (events1, name1) = arg1
                let (events2, name2) = arg2
                let statement1 = sprintf "INSERT INTO events%s (event, timestamp) VALUES (@event, @timestamp)" name1
                let statement2 = sprintf "INSERT INTO events%s (event, timestamp) VALUES (@event, @timestamp)" name2

                try 
                    let _ =
                        TPConnectionString
                        |> Sql.connect
                        |> Sql.executeTransactionAsync
                            [
                                statement1,
                                events1
                                |>> 
                                (
                                    fun x ->
                                        [
                                            ("@event", Sql.jsonb x);
                                            ("timestamp", Sql.timestamp (System.DateTime.Now))
                                        ]
                                )

                                statement2,
                                events2
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

            member this.GetEventsAfterId id name =
                let query = sprintf "SELECT id, event FROM events%s WHERE id > @id ORDER BY id" name
                TPConnectionString
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

