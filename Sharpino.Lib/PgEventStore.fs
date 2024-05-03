
namespace Sharpino

open FsToolkit.ErrorHandling
open Npgsql.FSharp
open Npgsql
open FSharpPlus
open FSharpPlus.Operators
open Sharpino
open Sharpino.Storage
open Sharpino.Definitions
open log4net
open log4net.Config

module PgStorage =
    open Conf

    let config = Conf.config ()
    let sqlJson = 
        match config.PgSqlJsonFormat with
        | PgSqlJson.PlainText -> Sql.text
        | PgSqlJson.PgJson -> Sql.jsonb

    let sqlBinary = Sql.bytea

    let readAsText<'F> = fun (r: RowReader) -> r.text 
    let readAsBinary:RowReaderByFormat<'F> = fun (r: RowReader) -> r.bytea

    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    // enable for quick debugging
    // log4net.Config.BasicConfigurator.Configure() |> ignore
    type PgEventStore(connection: string, readAsText: RowReader -> (string -> string)) =
        new (connection: string) =
            PgEventStore(connection, readAsText)

        member this.Reset version name = 
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
        member this.ResetAggregateStream version name =
            if (Conf.isTestEnv) then
                try
                    let res1 =
                        connection
                        |> Sql.connect
                        |> Sql.query (sprintf "DELETE from aggregate_events%s%s" version name)
                        |> Sql.executeNonQuery
                    ()    
                with
                    | _ as e -> failwith (e.ToString())
            else
                failwith "operation allowed only in test db"
                
        interface IEventStore<string> with
            // only test db should be resettable (erasable)
            member this.Reset(version: Version) (name: Name): unit =
                this.Reset version name
            member this.ResetAggregateStream(version: Version) (name: Name): unit =
                this.ResetAggregateStream version name    
            member this.TryGetLastSnapshot version name =
                log.Debug "TryGetLastSnapshot"
                let query = sprintf "SELECT id, event_id, snapshot FROM snapshots%s%s ORDER BY id DESC LIMIT 1" version name
                try
                    async {
                        return
                            connection
                            |> Sql.connect
                            |> Sql.query query
                            |> Sql.executeAsync (fun read ->
                                (
                                    read.int "id",
                                    read.int "event_id",
                                    readAsText read "snapshot"
                                )
                            )
                            |> Async.AwaitTask
                            |> Async.RunSynchronously
                            |> Seq.tryHead
                    }
                    |> Async.RunSynchronously
                with
                | _ as ex ->
                    log.Info (sprintf "an error occurred in retrieving snapshot: %A" ex.Message)
                    None

            // todo: start using result datatype when error is possible (wrap try catch into Result)
            member this.TryGetLastEventId version name =
                log.Debug (sprintf "TryGetLastEventId %s %s" version name)
                let query = sprintf "SELECT id FROM events%s%s ORDER BY id DESC LIMIT 1" version name
                async {
                    return 
                        connection
                        |> Sql.connect
                        |> Sql.query query 
                        |> Sql.executeAsync  (fun read -> read.int "id")
                        |> Async.AwaitTask
                        |> Async.RunSynchronously
                        |> Seq.tryHead
                }
                |> Async.RunSynchronously

            member this.TryGetLastSnapshotEventId version name =
                log.Debug (sprintf "TryGetLastSnapshotEventId %s %s" version name)
                let query = sprintf "SELECT event_id FROM snapshots%s%s ORDER BY id DESC LIMIT 1" version name
                async {
                    return
                        connection
                        |> Sql.connect
                        |> Sql.query query
                        |> Sql.executeAsync  (fun read -> read.int "event_id")
                        |> Async.AwaitTask
                        |> Async.RunSynchronously
                        |> Seq.tryHead
                }
                |> Async.RunSynchronously

            member this.TryGetLastSnapshotIdByAggregateId version name aggregateId =
                log.Debug (sprintf "TryGetLastSnapshotIdByAggregateId %s %s %A" version name aggregateId)
                let query = sprintf "SELECT event_id, id FROM snapshots%s%s WHERE aggregate_id = @aggregate_id ORDER BY id DESC LIMIT 1" version name
                async {
                    return
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
                }
                |> Async.RunSynchronously

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
                            JsonEvent = readAsText read "event"
                            KafkaOffset = read.int64OrNone "kafkaoffset"
                            KafkaPartition = read.intOrNone "kafkapartition"
                            Timestamp = read.dateTime "timestamp"
                        }
                    )
                    |> Async.AwaitTask
                    |> Async.RunSynchronously
                    |> Seq.tryHead
                    
            member this.AddEvents version name contextStateId events =
                log.Debug (sprintf "AddEvents %s %s %A" version name events)
                let stream_name = version + name
                let command = sprintf "SELECT insert%s_event_and_return_id(@event, @context_state_id);" stream_name
                let conn = new NpgsqlConnection(connection)

                let uniqueStateIds =
                    [  
                        for i in 1..events.Length -> 
                            if i = 1 then contextStateId
                            else System.Guid.NewGuid()
                    ]

                let eventsAndStatesIds = List.zip events uniqueStateIds

                conn.Open()
                let transaction = conn.BeginTransaction() 
                async {
                    let result =
                        try
                            let ids =
                                eventsAndStatesIds
                                |>>
                                    fun (event, contextStateId) -> 
                                        let command' = new NpgsqlCommand(command, conn)
                                        command'.Parameters.AddWithValue("event", event ) |> ignore
                                        command'.Parameters.AddWithValue("@context_state_id", contextStateId ) |> ignore
                                        let result = command'.ExecuteScalar() 
                                        result :?> int
                            transaction.Commit()
                            ids |> Ok
                        with
                            | _ as ex -> 
                                transaction.Rollback()
                                log.Error (sprintf "an error occurred: %A" ex.Message)
                                ex.Message |> Error
                    try
                        return result
                    finally
                        conn.Close()

                }
                |> Async.RunSynchronously

            member this.MultiAddEvents(arg: List<List<Json> * Version * Name * ContextStateId>) =
                log.Debug (sprintf "MultiAddEvents %A" arg)
                let conn = new NpgsqlConnection(connection)
                conn.Open()
                let transaction = conn.BeginTransaction() 
                async {
                    let result =
                        try
                            let cmdList = 
                                arg 
                                |>>
                                    fun (events, version,  name, contextStateId) -> 
                                        let stream_name = version + name
                                        let command = new NpgsqlCommand(sprintf "SELECT insert%s_event_and_return_id(@event, @context_state_id);" stream_name, conn)
                                        let uniqueContextStateIds =
                                            [  
                                                for i in 1..events.Length -> 
                                                    if i = 1 then contextStateId
                                                    else System.Guid.NewGuid()
                                            ]
                                        let eventsAndStatesIds = List.zip events uniqueContextStateIds
                                        // events
                                        eventsAndStatesIds
                                        |>> 
                                            fun (event, contextStateId) ->
                                                command.Parameters.AddWithValue("event", event ) |> ignore
                                                command.Parameters.AddWithValue("@context_state_id", contextStateId ) |> ignore
                                                let result = command.ExecuteScalar() 
                                                result :?> int
                                    
                            transaction.Commit()    
                            cmdList |> Ok
                        with
                            | _ as ex -> 
                                log.Error (sprintf "an error occurred: %A" ex.Message)
                                transaction.Rollback()
                                ex.Message |> Error
                    try
                        return result
                    finally
                        conn.Close()
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
                            readAsText read "event"
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
                let tryEvent = ((this :> IEventStore<string>).TryGetEvent version id name)
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
                                            ("snapshot",  sqlJson snapshot);
                                            ("timestamp", Sql.timestamp event.Timestamp)
                                        ]
                                    ]
                            ]
                        |> Async.AwaitTask
                        |> Async.RunSynchronously
                    () |> Ok

            member this.SetInitialAggregateState aggregateId aggregateStateId version name json =
                log.Debug "entered in setSnapshot"
                let command = sprintf "INSERT INTO snapshots%s%s (aggregate_id, aggregate_state_id, snapshot, timestamp) VALUES (@aggregate_id, @aggregate_state_id, @snapshot, @timestamp)" version name
                let command2 = sprintf "INSERT INTO aggregate_events%s%s (aggregate_id, aggregate_state_id) VALUES (@aggregate_id, @aggregate_state_id)" version name
                try
                    let _ =
                        connection
                        |> Sql.connect
                        |> Sql.executeTransactionAsync
                            [
                                command,
                                    [
                                        [
                                            ("@aggregate_id", Sql.uuid aggregateId);
                                            ("aggregate_state_id", Sql.uuid aggregateStateId);
                                            ("snapshot",  sqlJson json);
                                            ("timestamp", Sql.timestamp System.DateTime.UtcNow)
                                        ]
                                    ]
                                command2,
                                    [
                                        [
                                            ("@aggregate_id", Sql.uuid aggregateId)
                                            ("aggregate_state_id", Sql.uuid aggregateStateId)
                                        ]
                                    ]
                            ]
                        |> Async.AwaitTask
                        |> Async.RunSynchronously
                    () |> Ok
                with
                | _ as ex -> 
                    log.Error (sprintf "an error occurred: %A" ex.Message)
                    ex.Message |> Error

            member this.SetInitialAggregateStateAndAddEvents aggregateId aggregateStateId aggregateVersion aggregatename json contextVersion contextName contextStateId events =
                log.Debug "entered in setSnapshot"
                let command = sprintf "INSERT INTO snapshots%s%s (aggregate_id, aggregate_state_id, snapshot, timestamp) VALUES (@aggregate_id, @aggregate_state_id, @snapshot, @timestamp)" aggregateVersion aggregatename
                let command2 = sprintf "INSERT INTO aggregate_events%s%s (aggregate_id, aggregate_state_id) VALUES (@aggregate_id, @aggregate_state_id)" aggregateVersion aggregatename
                let command3 = sprintf "SELECT insert%s_event_and_return_id(@event, @context_state_id);" (contextVersion + contextName)
                let conn = new NpgsqlConnection(connection)
                let uniqueStateIds =
                    [  
                        for i in 1..events.Length -> 
                            if i = 1 then contextStateId
                            else System.Guid.NewGuid()
                    ]
                let eventsAndStatesIds = List.zip events uniqueStateIds
                conn.Open()
                let transaction = conn.BeginTransaction() 
                async {
                    let result =
                        try
                            let transaction = conn.BeginTransaction() 
                            let ids =
                                eventsAndStatesIds
                                |>>
                                    fun (event, contextStateId) -> 
                                        let command' = new NpgsqlCommand(command3, conn)
                                        command'.Parameters.AddWithValue("event", event ) |> ignore
                                        command'.Parameters.AddWithValue("@context_state_id", contextStateId ) |> ignore
                                        let result = command'.ExecuteScalar() 
                                        result :?> int
                            let _ =
                                connection
                                |> Sql.connect
                                |> Sql.executeTransactionAsync
                                    [
                                        command,
                                            [
                                                [
                                                    ("@aggregate_id", Sql.uuid aggregateId);
                                                    ("aggregate_state_id", Sql.uuid aggregateStateId);
                                                    ("snapshot",  sqlJson json);
                                                    ("timestamp", Sql.timestamp System.DateTime.UtcNow)
                                                ]
                                            ]
                                        command2,
                                            [
                                                [
                                                    ("@aggregate_id", Sql.uuid aggregateId)
                                                    ("aggregate_state_id", Sql.uuid aggregateStateId)
                                                ]
                                            ]
                                    ]
                                |> Async.AwaitTask
                                |> Async.RunSynchronously
                            transaction.Commit()
                            conn.Close()
                            ids |> Ok
                        with
                            | _ as ex -> 
                                log.Error (sprintf "an error occurred: %A" ex.Message)
                                transaction.Rollback()
                                ex.Message |> Error
                    try
                        return result
                    finally
                        conn.Close()
                }
                |> Async.RunSynchronously

            member this.SetAggregateSnapshot version (aggregateId: AggregateId, eventId: int, snapshot: Json) name =
                log.Debug "entered in setSnapshot"
                let command = sprintf "INSERT INTO snapshots%s%s (aggregate_id, event_id, snapshot, timestamp) VALUES (@aggregate_id, @event_id, @snapshot, @timestamp)" version name
                let tryEvent = ((this :> IEventStore<string>).TryGetEvent version eventId name)
                match tryEvent with
                | None -> Error (sprintf "event %d not found" eventId)
                | Some event -> 
                    try
                        let _ =
                            connection
                            |> Sql.connect
                            |> Sql.executeTransactionAsync
                                [
                                    command,
                                        [
                                            [
                                                ("@aggregate_id", Sql.uuid aggregateId);
                                                ("@event_id", Sql.int event.Id);
                                                ("snapshot",  sqlJson snapshot);
                                                ("timestamp", Sql.timestamp event.Timestamp)
                                            ]
                                        ]
                                ]
                            |> Async.AwaitTask
                            |> Async.RunSynchronously
                        () |> Ok
                    with
                    | _ as ex -> 
                        log.Error (sprintf "an error occurred: %A" ex.Message)
                        ex.Message |> Error
            
            member this.GetEventsInATimeInterval version name dateFrom dateTo =
                log.Debug (sprintf "GetEventsInATimeInterval %s %s %A %A" version name dateFrom dateTo)
                let query = sprintf "SELECT id, event FROM events%s%s WHERE timestamp >= @dateFrom AND timestamp <= @dateTo ORDER BY id" version name
                connection
                |> Sql.connect
                |> Sql.query query
                |> Sql.parameters ["dateFrom", Sql.timestamp dateFrom; "dateTo", Sql.timestamp dateTo]
                |> Sql.executeAsync ( fun read ->
                    (
                        read.int "id",
                        readAsText read "event"
                    )
                )
                |> Async.AwaitTask
                |> Async.RunSynchronously
                |> Seq.toList

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
                connection
                |> Sql.connect
                |> Sql.query query
                |> Sql.parameters ["id", Sql.int id]
                |> Sql.executeAsync (fun read ->
                    (
                        read.int "event_id",
                        readAsText read "snapshot"
                    )
                )
                |> Async.AwaitTask
                |> Async.RunSynchronously
                |> Seq.tryHead

            member this.TryGetAggregateSnapshotById version name aggregateId id =
                log.Debug (sprintf "TryGetSnapshotById %s %s %d" version name id)
                let query = sprintf "SELECT event_id, snapshot FROM snapshots%s%s WHERE id = @id" version name
                connection
                |> Sql.connect
                |> Sql.query query
                |> Sql.parameters ["id", Sql.int id]
                |> Sql.executeAsync (fun read ->
                    (
                        read.intOrNone "event_id",
                        readAsText read "snapshot"
                    )
                )
                |> Async.AwaitTask
                |> Async.RunSynchronously
                |> Seq.tryHead
            
            member this.TryGetLastAggregateSnapshotEventId version name aggregateId =
                log.Debug (sprintf "TryGetLastSnapshotEventId %s %s" version name)
                let query = sprintf "SELECT event_id FROM snapshots%s%s WHERE aggregate_id = @aggregateId ORDER BY id DESC LIMIT 1" version name
                try
                    connection
                    |> Sql.connect
                    |> Sql.query query
                    |> Sql.parameters ["aggregateId", Sql.uuid aggregateId]
                    |> Sql.executeAsync (fun read -> read.int "event_id")
                    |> Async.AwaitTask
                    |> Async.RunSynchronously
                    |> Seq.tryHead
                with
                    | _ as ex ->
                        log.Error (sprintf "TryGetLastAggregateSnapshotEventId an error occurred: %A" ex.Message)
                        None
                
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
            
            member this.SetClassicOptimisticLock version name =
                log.Debug (sprintf "SetClassicOptimisticLock %s %s " version name)
                let command = sprintf "CALL set_classic_optimistic_lock%s%s()" version name
                let conn = new NpgsqlConnection(connection)
                conn.Open()
                let transaction = conn.BeginTransaction() 
                async {
                    let result =
                        try
                            let _ =
                                connection
                                |> Sql.connect
                                |> Sql.query command
                                |> Sql.executeNonQueryAsync
                                |> Async.AwaitTask
                                |> Async.RunSynchronously
                                |> ignore
                            transaction.Commit()
                            () |> Ok
                        with
                            | _ as ex -> 
                                transaction.Rollback()
                                log.Error (sprintf "an error occurred: %A" ex.Message)
                                ex.Message |> Error
                    try
                        return result
                    finally
                        conn.Close()
                }
                |> Async.RunSynchronously
                
            member this.UnSetClassicOptimisticLock version name =
                log.Debug (sprintf "SetClassicOptimisticLock %s %s " version name)
                let command = sprintf "CALL un_set_classic_optimistic_lock%s%s()" version name
                let conn = new NpgsqlConnection(connection)
                conn.Open()
                let transaction = conn.BeginTransaction() 
                async {
                    let result = 
                        try
                            let _ =
                                connection
                                |> Sql.connect
                                |> Sql.query command
                                |> Sql.executeNonQueryAsync
                                |> Async.AwaitTask
                                |> Async.RunSynchronously
                                |> ignore
                            transaction.Commit()
                            () |> Ok
                        with
                            | _ as ex -> 
                                log.Error (sprintf "an error occurred: %A" ex.Message)
                                transaction.Rollback()
                                ex.Message |> Error
                    try
                        return result
                    finally
                        conn.Close()
                }
                |> Async.RunSynchronously
                
            member this.AddAggregateEvents(version: Version) (name: Name) (aggregateId: System.Guid) (aggregateStateId: System.Guid) (events: List<Json>): Result<List<int>,string> =
                log.Debug (sprintf "AddAggregateEvents %s %s %A %A" version name aggregateId events)
                let stream_name = version + name
                let command = sprintf "SELECT insert%s_aggregate_event_and_return_id(@event, @aggregate_id, @aggregate_state_id);" stream_name
                let conn = new NpgsqlConnection(connection)
                let uniqueStateIds =
                    [  
                        for i in 1..events.Length -> 
                            if i = 1 then aggregateStateId
                            else System.Guid.NewGuid()
                    ]
                let eventsAndAggregateStateId = List.zip events uniqueStateIds
                conn.Open()
                let transaction = conn.BeginTransaction() 
                async {
                    let result =
                        try
                            let ids =
                                eventsAndAggregateStateId
                                |>> 
                                    (
                                        fun (x, aggregateStateId) -> 
                                            let command' = new NpgsqlCommand(command, conn)
                                            command'.Parameters.AddWithValue("event", x ) |> ignore
                                            command'.Parameters.AddWithValue("@aggregate_id", aggregateId ) |> ignore
                                            command'.Parameters.AddWithValue("@aggregate_state_id", aggregateStateId ) |> ignore
                                            let result = command'.ExecuteScalar() 
                                            result :?> int
                                    )
                            transaction.Commit()
                            ids |> Ok
                        with
                            | _ as ex -> 
                                log.Error (sprintf "an error occurred: %A" ex.Message)
                                transaction.Rollback()
                                ex.Message |> Error
                    try
                        return result
                    finally
                        conn.Close()
                }
                |> Async.RunSynchronously

            member this.MultiAddAggregateEvents (arg: List<List<Json> * Version * Name * System.Guid * System.Guid>) =
                log.Debug (sprintf "MultiAddAggregateEvents %A" arg)
                let conn = new NpgsqlConnection(connection)
                conn.Open()
                let transaction = conn.BeginTransaction() 
                async {
                    let result =
                        try
                            let cmdList = 
                                arg 
                                |>>
                                    (
                                        fun (events, version,  name, aggregateId, aggregateStateId) ->
                                            let stream_name = version + name
                                            let command = new NpgsqlCommand(sprintf "SELECT insert%s_event_and_return_id(@event, @aggregate_id, @aggregate_state_id);" stream_name, conn)
                                            let uniqueAggregateStateIds =
                                                [  
                                                    for i in 1..events.Length -> 
                                                        if i = 1 then aggregateStateId
                                                        else System.Guid.NewGuid()
                                                ]
                                            let eventsAndStatesIds = List.zip events uniqueAggregateStateIds
                                            eventsAndStatesIds
                                            |>> 
                                                fun (event, aggregateStateId) ->
                                                    (
                                                        command.Parameters.AddWithValue("event", event ) |> ignore
                                                        command.Parameters.AddWithValue("@aggregate_id", aggregateId ) |> ignore
                                                        command.Parameters.AddWithValue("@aggregate_state_id", aggregateStateId ) |> ignore
                                                        let result = command.ExecuteScalar() 
                                                        result :?> int
                                                    )
                                            
                                    )
                            transaction.Commit()    
                            cmdList |> Ok
                        with
                            | _ as ex -> 
                                log.Error (sprintf "an error occurred: %A" ex.Message)
                                transaction.Rollback()
                                ex.Message |> Error
                    try
                        return result
                    finally
                        conn.Close()
                }
                |> Async.RunSynchronously

            member this.GetAggregateEventsAfterId version name aggregateId id: Result<List<EventId * Json>,string> = 
                // log.Debug (sprintf "GetEventsAfterIdrefactorer %s %s %A %d" version name aggregateId id)
                let query = sprintf "SELECT id, event FROM events%s%s WHERE id > @id and aggregate_id = @aggregateId ORDER BY id"  version name
                try 
                    connection
                    |> Sql.connect
                    |> Sql.query query
                    |> Sql.parameters ["id", Sql.int id; "aggregateId", Sql.uuid aggregateId]
                    |> Sql.executeAsync ( fun read ->
                        (
                            read.int "id",
                            readAsText read "event"
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
            member this.GetAggregateEvents version name aggregateId: Result<List<EventId * Json>,string> = 
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
                            readAsText read "event"
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
