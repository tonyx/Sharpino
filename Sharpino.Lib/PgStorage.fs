
namespace Sharpino

open FsToolkit.ErrorHandling
open Npgsql.FSharp
open FSharpPlus
open Sharpino
open Sharpino.Storage
open log4net
open log4net.Config

module PgStorage =

    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    type PgStorage(connection: string) =
        interface IStorage with

            // only test db should be able to reset
            member this.Reset(version: version) (name: Name): unit = 
                if (Conf.isTestEnv) then
                    try
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
                    with 
                        | _ as e -> failwith (e.ToString())
                else
                    failwith "operation allowed only in test db"

            member this.TryGetLastSnapshot version name =
                log.Debug "TryGetLastSnapshot"
                let query = sprintf "SELECT id, event_id, snapshot FROM snapshots%s%s ORDER BY id DESC LIMIT 1" version name
                let res =
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
                res

            member this.TryGetLastEventId version name =
                log.Debug (sprintf "TryGetLastEventId %s %s" version name)
                let query = sprintf "SELECT id FROM events%s%s ORDER BY id DESC LIMIT 1" version name
                connection
                |> Sql.connect
                |> Sql.query query 
                |> Sql.executeAsync  (fun read -> read.int "id")
                |> Async.AwaitTask
                |> Async.RunSynchronously
                |> Seq.tryHead

            member this.TryGetLastSnapshotEventId version name =
                log.Debug (sprintf "TryGetLastSnapshotEventId %s %s" version name)
                let query = sprintf "SELECT event_id FROM snapshots%s%s ORDER BY id DESC LIMIT 1" version name
                connection
                |> Sql.connect
                |> Sql.query query
                |> Sql.executeAsync  (fun read -> read.int "event_id")
                |> Async.AwaitTask
                |> Async.RunSynchronously
                |> Seq.tryHead
            member this.TryGetEvent version id name =
                log.Debug (sprintf "TryGetEvent %s %s" version name)
                let query = sprintf "SELECT * from events%s%s where id = @id" version name
                let res =
                    connection
                    |> Sql.connect
                    |> Sql.query query 
                    |> Sql.parameters ["id", Sql.int id]
                    |> Sql.executeAsync
                        (
                            fun read ->
                            {
                                Id = read.int "id"
                                JsonEvent = read.string "event"
                                Timestamp = read.dateTime "timestamp"
                            }
                        )
                        |> Async.AwaitTask
                        |> Async.RunSynchronously
                        |> Seq.tryHead
                res
            member this.AddEvents version name events =
                log.Debug (sprintf "AddEvents %s %s %A" version name events)
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
                    log.Debug "exiting from add events"
                    () |> Ok
                with
                    | _ as ex -> 
                        log.Debug (sprintf "an error occurred: %A" ex.Message)
                        ex.Message |> Error
            member this.MultiAddEvents(arg: List<List<Json> * version * Name>): Result<unit,string> = 
                log.Debug (sprintf "MultiAddEvents %A" arg)
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
                    | _ as ex -> ex.Message |> Error
            member this.GetEventsAfterId version id name =
                log.Debug (sprintf "GetEventsAfterId %s %s %d" version name id)
                let query = sprintf "SELECT id, event FROM events%s%s WHERE id > @id ORDER BY id"  version name
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
                |> Ok

            member this.SetSnapshot version (id: int, snapshot: Json) name =
                log.Debug "entered in setSnapshot"

                let command = sprintf "INSERT INTO snapshots%s%s (event_id, snapshot, timestamp) VALUES (@event_id, @snapshot, @timestamp)" version name
                // let serializedSnapshot = serializer.Serialize<'A> snapshot
                let tryEvent = ((this :> IStorage).TryGetEvent version id name)
                match tryEvent with
                | None -> Error (sprintf "event %d not found" id)
                | Some event -> 
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
                    () |> Ok

            member this.GetEventsInATimeInterval version name dateFrom dateTo =
                log.Debug (sprintf "GetEventsInATimeInterval %s %s %A %A" version name dateFrom dateTo)
                let query = sprintf "SELECT id, event FROM events%s%s WHERE timestamp >= @dateFrom AND timestamp <= @dateTo ORDER BY id" version name
                let res =
                    connection
                    |> Sql.connect
                    |> Sql.query query
                    |> Sql.parameters ["dateFrom", Sql.timestamp dateFrom; "dateTo", Sql.timestamp dateTo]
                    |> Sql.executeAsync ( fun read ->
                        (
                            read.int "id",
                            read.text "event"
                        )
                    )
                    |> Async.AwaitTask
                    |> Async.RunSynchronously
                    |> Seq.toList
                res 

            member this.TryGetLastSnapshotId version name =
                log.Debug (sprintf "TryGetLastSnapshotId %s %s" version name)
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

            member this.TryGetSnapshotById version name id =
                log.Debug (sprintf "TryGetSnapshotById %s %s %d" version name id)
                let query = sprintf "SELECT event_id, snapshot FROM snapshots%s%s WHERE id = @id" version name
                let res =
                    connection
                    |> Sql.connect
                    |> Sql.query query
                    |> Sql.parameters ["id", Sql.int id]
                    |> Sql.executeAsync (fun read ->
                        (
                            read.int "event_id",
                            read.text "snapshot"
                        )
                    )
                    |> Async.AwaitTask
                    |> Async.RunSynchronously
                    |> Seq.tryHead
                res
