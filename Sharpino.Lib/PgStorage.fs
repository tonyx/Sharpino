
namespace Sharpino

open FsToolkit.ErrorHandling
open Npgsql.FSharp
open FSharpPlus
open Sharpino
open Sharpino.Utils
open Sharpino.Storage
open log4net
open log4net.Config

module PgStorage =
    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    // you can configure log here, or in the main program (see tests)
    type PgStorage(connection: string, serializer: JsonSerializer) =
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
                match res with
                | None -> None
                | Some (id, event_id, snapshot) ->
                    match (serializer.Deserialize snapshot) with
                    | Ok snapshot -> Some (id, event_id, snapshot)
                    | Error e -> failwith e
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
                match res with
                | None -> None    
                | Some x ->
                    match (serializer.Deserialize<'E> x.JsonEvent) with
                    | Ok event -> Some { Event = event; Id = x.Id; Timestamp = x.Timestamp }
                    | Error e -> failwith e

            member this.SetSnapshot version (id: int, snapshot: 'A) name =
                log.Debug "entered in setSnapshot"

                let command = sprintf "INSERT INTO snapshots%s%s (event_id, snapshot, timestamp) VALUES (@event_id, @snapshot, @timestamp)" version name
                let serializedSnapshot = serializer.Serialize<'A> snapshot
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
                                            ("snapshot",  Sql.jsonb serializedSnapshot);
                                            ("timestamp", Sql.timestamp event.Timestamp)
                                        ]
                                    ]
                            ]
                        |> Async.AwaitTask
                        |> Async.RunSynchronously
                    () |> Ok

            member this.AddEvents version (events: List<'E>) name =
                log.Debug (sprintf "AddEvents %s %s %A" version name events)
                let command = sprintf "INSERT INTO events%s%s (event, timestamp) VALUES (@event, @timestamp)" version name
                try
                    let _ =
                        connection
                        |> Sql.connect
                        |> Sql.executeTransactionAsync
                            [
                                command,
                                (events |>> (fun x -> serializer.Serialize<'E> x))
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

            member this.MultiAddEvents (arg: List<List<obj> * version * Name>) : Result<unit, string> = 
                log.Debug (sprintf "MultiAddEvents %A" arg)
                let cmdList = 
                    arg 
                    |> List.map 
                        (
                            fun (events, version,  name) -> 
                                let statement = sprintf "INSERT INTO events%s%s (event, timestamp) VALUES (@event, @timestamp)" version name
                                statement, (events |>> (fun x -> serializer.Serialize x))
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


            member this.GetEventsAfterId<'E> version id name =
                log.Debug (sprintf "GetEventsAfterId %s %s %d" version name id)
                let query = sprintf "SELECT id, event FROM events%s%s WHERE id > @id ORDER BY id"  version name
                let res =
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
                res 
                |> catchErrors 
                    (
                        fun (id, event) ->  
                        Result.map (fun x -> (id, x)) (serializer.Deserialize<'E> event)
                    )

            member this.GetEventsInATimeInterval(version: version) (name: Name) (dateFrom: System.DateTime) (dateTo: System.DateTime): List<int * 'E> = 
                log.Debug (sprintf "GetEventsInATimeInterval %s %s %A %A" version name dateFrom dateTo)
                try
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
                    res |>> (fun (id, event) -> (id, serializer.Deserialize<'E> event |> Result.get))
                with
                | _ as ex -> 
                    log.Error (sprintf "an error occurred: %A" ex.Message)
                    printf "an error occurred: %A\n" ex.Message
                    []

