
namespace Sharpino

open FsToolkit.ErrorHandling
open Npgsql.FSharp
open Npgsql
open FSharpPlus
open Sharpino
open Sharpino.Storage
open Sharpino.Definitions
open log4net
open log4net.Config

module PgStorage =

    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    // enable for quick debugging
    // log4net.Config.BasicConfigurator.Configure() |> ignore
    type PgEventStore(connection: string) =
        interface IEventStore with
            // only test db should be able to reset
            member this.Reset(version: Version) (name: Name): unit = 
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

            member this.TryGetLastSnapshotIdByAggregateId version name aggregateId =
                log.Debug (sprintf "TryGetLastSnapshotIdByAggregateId %s %s %A" version name aggregateId)
                let query = sprintf "SELECT event_id, id FROM snapshots%s%s WHERE aggregate_id = @aggregate_id ORDER BY id DESC LIMIT 1" version name
                connection
                |> Sql.connect
                |> Sql.query query
                |> Sql.parameters ["aggregate_id", Sql.uuid aggregateId]
                |> Sql.executeAsync (fun read ->
                    (
                        read.intOrNone "event_id",
                        read.int "id"
                    )
                )
                |> Async.AwaitTask
                |> Async.RunSynchronously
                |> Seq.tryHead

            member this.TryGetEvent version id name =
                log.Debug (sprintf "TryGetEvent %s %s" version name)
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
                            JsonEvent = read.string "event"
                            KafkaOffset = read.int64OrNone "kafkaoffset"
                            KafkaPartition = read.intOrNone "kafkapartition"
                            Timestamp = read.dateTime "timestamp"
                        }
                    )
                    |> Async.AwaitTask
                    |> Async.RunSynchronously
                    |> Seq.tryHead
            member this.AddEvents version name events =
                log.Debug (sprintf "AddEvents %s %s %A" version name events)
                let stream_name = version + name
                let command = sprintf "SELECT insert%s_event_and_return_id(@event);" stream_name
                let conn = new NpgsqlConnection(connection)
                conn.Open()
                async {
                    return
                        try
                            let transaction = conn.BeginTransaction() 
                            let ids =
                                events
                                |> List.map 
                                    (
                                        fun x -> 
                                            let command' = new NpgsqlCommand(command, conn)
                                            let param = new NpgsqlParameter("@event", NpgsqlTypes.NpgsqlDbType.Json)
                                            param.Value <- x
                                            command'.Parameters.AddWithValue("event", x ) |> ignore
                                            let result = command'.ExecuteScalar() 
                                            result :?> int
                                    )
                            transaction.Commit()
                            conn.Close()
                            ids |> Ok
                        with
                            | _ as ex -> 
                                log.Error (sprintf "an error occurred: %A" ex.Message)
                                ex.Message |> Error
                }
                |> Async.RunSynchronously

            member this.MultiAddEvents(arg: List<List<Json> * Version * Name>) =
                log.Debug (sprintf "MultiAddEvents %A" arg)
                let conn = new NpgsqlConnection(connection)
                conn.Open()
                let transaction = conn.BeginTransaction() 
                async {
                    return
                        try
                            let cmdList = 
                                arg 
                                |> List.map 
                                    (
                                        fun (events, version,  name) -> 
                                            let stream_name = version + name
                                            let command = new NpgsqlCommand(sprintf "SELECT insert%s_event_and_return_id(@event);" stream_name, conn)
                                            events
                                            |>> 
                                            (
                                                fun x ->
                                                    (
                                                        let param = new NpgsqlParameter("@event", NpgsqlTypes.NpgsqlDbType.Json)
                                                        param.Value <- x
                                                        command.Parameters.AddWithValue("event", x ) |> ignore
                                                        let result = command.ExecuteScalar() 
                                                        result :?> int
                                                    )
                                            )
                                    )
                            transaction.Commit()    
                            conn.Close()
                            cmdList |> Ok
                        with
                            | _ as ex -> 
                                log.Error (sprintf "an error occurred: %A" ex.Message)
                                ex.Message |> Error
                }
                |> Async.RunSynchronously
            member this.GetEventsAfterId version id name =
                log.Debug (sprintf "GetEventsAfterId %s %s %d" version name id)
                let query = sprintf "SELECT id, event FROM events%s%s WHERE id > @id ORDER BY id"  version name
                try 
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
                with 
                | _ as ex -> 
                    log.Error (sprintf "an error occurred: %A" ex.Message)
                    ex.Message |> Error
            member this.SetSnapshot version (id: int, snapshot: Json) name =
                log.Debug "entered in setSnapshot"
                let command = sprintf "INSERT INTO snapshots%s%s (event_id, snapshot, timestamp) VALUES (@event_id, @snapshot, @timestamp)" version name
                let tryEvent = ((this :> IEventStore).TryGetEvent version id name)
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

            member this.SetInitialAggregateState aggregateId version name json =
                log.Debug "entered in setSnapshot"
                let command = sprintf "INSERT INTO snapshots%s%s (aggregate_id, snapshot, timestamp) VALUES (@aggregate_id, @snapshot, @timestamp)" version name
                let _ =
                    connection
                    |> Sql.connect
                    |> Sql.executeTransactionAsync
                        [
                            command,
                                [
                                    [
                                        ("@aggregate_id", Sql.uuid aggregateId);
                                        ("snapshot",  Sql.jsonb json);
                                        ("timestamp", Sql.timestamp System.DateTime.Now)
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
                let query = sprintf "SELECT event_id, id FROM snapshots%s%s ORDER BY id DESC LIMIT 1" version name
                connection
                |> Sql.connect
                |> Sql.query query
                |> Sql.executeAsync (fun read ->
                    (
                        read.int "event_id",
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

            member this.TryGetAggregateSnapshotById version name id =
                log.Debug (sprintf "TryGetSnapshotById %s %s %d" version name id)
                let query = sprintf "SELECT event_id, snapshot FROM snapshots%s%s WHERE id = @id" version name
                let res =
                    connection
                    |> Sql.connect
                    |> Sql.query query
                    |> Sql.parameters ["id", Sql.int id]
                    |> Sql.executeAsync (fun read ->
                        (
                            read.intOrNone "event_id",
                            read.text "snapshot"
                        )
                    )
                    |> Async.AwaitTask
                    |> Async.RunSynchronously
                    |> Seq.tryHead
                res
            member this.SetPublished version name id kafkaOffset partition =
                try
                    let streamName = version + name
                    let updateQuery = sprintf "UPDATE events%s SET published = true, kafkaoffset = '%d', kafkapartition = '%d' WHERE id = '%d'"  streamName kafkaOffset partition id
                    connection 
                    |> Sql.connect
                    |> Sql.query updateQuery
                    |> Sql.executeNonQueryAsync
                    |> Async.AwaitTask
                    |> Async.RunSynchronously
                    |> ignore
                    |> Ok
                with
                | _ ->
                    Error("Not persisted")

            member this.TryGetLastEventIdByAggregateIdWithKafkaOffSet version name aggregateId =
                log.Debug (sprintf "TryGetLastEventId %s %s" version name)
                let query = sprintf "SELECT id, kafkaoffset, kafkapartition FROM events%s%s where aggregate_id = '%A' ORDER BY id DESC LIMIT 1" version name aggregateId
                connection
                |> Sql.connect
                |> Sql.query query 
                |> Sql.executeAsync  (fun read ->
                        (
                            read.int "id",
                            read.int64OrNone "kafkaoffset",
                            read.intOrNone "kafkapartition"
                        )     
                    )
                |> Async.AwaitTask
                |> Async.RunSynchronously
                |> Seq.tryHead
            member this.TryGetLastEventIdWithKafkaOffSet version name =
                log.Debug (sprintf "TryGetLastEventId %s %s" version name)
                let query = sprintf "SELECT id, kafkaoffset, kafkapartition FROM events%s%s ORDER BY id DESC LIMIT 1" version name
                connection
                |> Sql.connect
                |> Sql.query query 
                |> Sql.executeAsync  (fun read ->
                        (
                            read.int "id",
                            read.int64OrNone "kafkaoffset",
                            read.intOrNone "kafkapartition"
                        )     
                    )
                |> Async.AwaitTask
                |> Async.RunSynchronously
                |> Seq.tryHead
            member this.AddEventsRefactored(version: Version) (name: Name) (aggregateId: System.Guid) (events: List<Json>): Result<List<int>,string> = 
                log.Debug (sprintf "AddEventsRefactored %s %s %A %A" version name aggregateId events)
                let stream_name = version + name
                printf "stream name: %s\n" stream_name
                let command = sprintf "SELECT insert%s_event_and_return_id(@event, @aggregate_id);" stream_name
                let conn = new NpgsqlConnection(connection)
                conn.Open()
                async {
                    return
                        try
                            let transaction = conn.BeginTransaction() 
                            let ids =
                                events
                                |> List.map 
                                    (
                                        fun x -> 
                                            let command' = new NpgsqlCommand(command, conn)
                                            command'.Parameters.AddWithValue("event", x ) |> ignore
                                            command'.Parameters.AddWithValue("@aggregate_id", aggregateId ) |> ignore
                                            let result = command'.ExecuteScalar() 
                                            result :?> int
                                    )
                            transaction.Commit()
                            conn.Close()
                            ids |> Ok
                        with
                            | _ as ex -> 
                                log.Error (sprintf "an error occurred: %A" ex.Message)
                                ex.Message |> Error
                }
                |> Async.RunSynchronously

            member this.GetEventsAfterIdRefactored version name aggregateId id: Result<List<EventId * Json>,string> = 
                log.Debug (sprintf "GetEventsAfterIdrefactorer %s %s %A %d" version name aggregateId id)
                let query = sprintf "SELECT id, event FROM events%s%s WHERE id > @id and aggregate_id = @aggregateId ORDER BY id"  version name
                try 
                    connection
                    |> Sql.connect
                    |> Sql.query query
                    |> Sql.parameters ["id", Sql.int id; "aggregateId", Sql.uuid aggregateId]
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
                with
                | _ as ex -> 
                    log.Error (sprintf "an error occurred: %A" ex.Message)
                    ex.Message |> Error
            member this.GetEventsAfterNoneRefactored version name aggregateId: Result<List<EventId * Json>,string> = 
                log.Debug (sprintf "GetEventsAfterIdrefactorer %s %s %A" version name aggregateId)
                let query = sprintf "SELECT id, event FROM events%s%s WHERE aggregate_id = @aggregateId ORDER BY id"  version name
                try 
                    connection
                    |> Sql.connect
                    |> Sql.query query
                    |> Sql.parameters ["aggregateId", Sql.uuid aggregateId]
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
                with
                | _ as ex -> 
                    log.Error (sprintf "an error occurred: %A" ex.Message)
                    ex.Message |> Error
