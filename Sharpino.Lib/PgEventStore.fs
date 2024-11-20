
namespace Sharpino

open System
open System.Threading
open FsToolkit.ErrorHandling
open Npgsql.FSharp
open Npgsql
open FSharpPlus
open FSharpPlus.Operators
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions
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
    let evenStoreTimeout = config.EventStoreTimeout    

    let sqlBinary = Sql.bytea

    let readAsText<'F> = fun (r: RowReader) -> r.text 
    let readAsBinary:RowReaderByFormat<'F> = fun (r: RowReader) -> r.bytea

    // this logger will be deprecated in the next release
    // let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    
    //todo: will just use the constructors to pass the logger in next releases
    let logger: ILogger ref = ref NullLogger.Instance
    let setLogger (newLogger: ILogger) =
        logger := newLogger
    
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
                logger.Value.LogDebug("TryGetLastSnapshot")
                let query = sprintf "SELECT id, event_id, snapshot FROM snapshots%s%s ORDER BY id DESC LIMIT 1" version name
                try
                    Async.RunSynchronously(
                        async {
                            return
                                connection
                                |> Sql.connect
                                |> Sql.query query
                                |> Sql.execute (fun read ->
                                    (
                                        read.int "id",
                                        read.int "event_id",
                                        readAsText read "snapshot"
                                    )
                                )
                                |> Seq.tryHead
                        }, evenStoreTimeout)
                with
                | _ as ex ->
                    logger.Value.LogInformation (sprintf "an error occurred in retrieving snapshot: %A" ex.Message)
                    None

            member this.TryGetLastEventId version name =
                logger.Value.LogDebug(sprintf "TryGetLastEventId %s %s" version name)
                let query = sprintf "SELECT id FROM events%s%s ORDER BY id DESC LIMIT 1" version name
                Async.RunSynchronously
                    (async {
                        return
                            connection
                            |> Sql.connect
                            |> Sql.query query
                            |> Sql.execute (fun read -> read.int "id")
                            |> Seq.tryHead
                        }, evenStoreTimeout)
            member this.TryGetLastSnapshotEventId version name =
                logger.Value.LogDebug (sprintf "TryGetLastSnapshotEventId %s %s" version name)
                let query = sprintf "SELECT event_id FROM snapshots%s%s ORDER BY id DESC LIMIT 1" version name
                try
                    Async.RunSynchronously
                        (async {
                            return 
                                connection
                                |> Sql.connect
                                |> Sql.query query 
                                |> Sql.execute  (fun read -> read.int "event_id")
                                |> Seq.tryHead
                        }, evenStoreTimeout)
                with
                | _ as ex ->
                    logger.Value.LogError (sprintf "TryGetLastSnapshotEventId: an error occurred: %A" ex.Message)
                    None

            member this.TryGetLastSnapshotIdByAggregateId version name aggregateId =
                logger.Value.LogDebug (sprintf "TryGetLastSnapshotIdByAggregateId %s %s %A" version name aggregateId)
                let query = sprintf "SELECT event_id, id FROM snapshots%s%s WHERE aggregate_id = @aggregate_id ORDER BY id DESC LIMIT 1" version name
                Async.RunSynchronously
                    (async {
                        return 
                            connection
                            |> Sql.connect
                            |> Sql.query query
                            |> Sql.parameters ["aggregate_id", Sql.uuid aggregateId]
                            |> Sql.execute (fun read ->
                                (
                                    read.intOrNone "event_id",
                                    read.int "id"
                                )
                            )
                            |> Seq.tryHead
                    }, evenStoreTimeout)

            // not good that returns none in case of error 
            member this.TryGetEvent version id name =
                logger.Value.LogDebug (sprintf "TryGetEvent %s %s" version name)
                let query = sprintf "SELECT * from events%s%s where id = @id" version name
                try
                    Async.RunSynchronously
                        (async {
                            return
                                connection
                                |> Sql.connect
                                |> Sql.query query 
                                |> Sql.parameters ["id", Sql.int id]
                                |> Sql.execute
                                    (
                                        fun read ->
                                        {
                                            Id = read.int "id"
                                            JsonEvent = readAsText read "event"
                                            Timestamp = read.dateTime "timestamp"
                                        }
                                    )
                                    |> Seq.tryHead
                        }
                        , evenStoreTimeout)
                with    
                | _ as ex ->
                    logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                    None     
                    
            member this.AddEvents eventId version name events =
                logger.Value.LogDebug (sprintf "AddEvents %s %s %A" version name events)
                let stream_name = version + name
                let command = sprintf "SELECT insert%s_event_and_return_id(@event);" stream_name
                let conn = new NpgsqlConnection(connection)

                conn.Open()
                let transaction = conn.BeginTransaction() 
                Async.RunSynchronously
                    (
                        async {
                            let lastEventId = (this :> IEventStore<string>).TryGetLastEventId version name
                            let result =
                                if (lastEventId.IsNone && eventId = 0) || (lastEventId.IsSome && lastEventId.Value = eventId) then
                                    try
                                        let ids =
                                            events
                                            |>>
                                                fun event -> 
                                                    let command' = new NpgsqlCommand(command, conn)
                                                    command'.Parameters.AddWithValue("event", event ) |> ignore
                                                    let result = command'.ExecuteScalar() 
                                                    result :?> int
                                        transaction.Commit()
                                        ids |> Ok
                                    with
                                        | _ as ex -> 
                                            transaction.Rollback()
                                            logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                                            ex.Message |> Error
                                else
                                    transaction.Rollback()
                                    Error "EventId is not the last one"
                            try
                                return result
                            finally
                                conn.Close()
                        }, evenStoreTimeout
                    )
                    
            member this.AddEventsMd eventId version name metadata events =
                logger.Value.LogDebug (sprintf "AddEventsMd %s %s %A %s" version name events metadata)
                let stream_name = version + name
                let command = sprintf "SELECT insert_md%s_event_and_return_id(@event,@md);" stream_name
                let conn = new NpgsqlConnection(connection)

                conn.Open()
                let transaction = conn.BeginTransaction() 
                Async.RunSynchronously
                    (
                        async {
                            let lastEventId = (this :> IEventStore<string>).TryGetLastEventId version name
                            let result =
                                if (lastEventId.IsNone && eventId = 0) || (lastEventId.IsSome && lastEventId.Value = eventId) then
                                    try
                                        let ids =
                                            events
                                            |>>
                                                fun event -> 
                                                    let command' = new NpgsqlCommand(command, conn)
                                                    command'.Parameters.AddWithValue("event", event ) |> ignore
                                                    command'.Parameters.AddWithValue("md", metadata ) |> ignore
                                                    let result = command'.ExecuteScalar() 
                                                    result :?> int
                                        transaction.Commit()
                                        ids |> Ok
                                    with
                                        | _ as ex -> 
                                            transaction.Rollback()
                                            logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                                            ex.Message |> Error
                                else
                                    transaction.Rollback()
                                    Error "EventId is not the last one"
                            try
                                return result
                            finally
                                conn.Close()
                        }, evenStoreTimeout
                    )

            member this.MultiAddEvents (arg: List<EventId * List<Json> * Version * Name>) =
                logger.Value.LogDebug (sprintf "MultiAddEvents %A" arg)
                let conn = new NpgsqlConnection(connection)
                conn.Open()
                let transaction = conn.BeginTransaction() 
                Async.RunSynchronously
                    (async {
                        let lastEventIdsPerContext =
                            arg |>> (fun (eventId, _, version, name) -> (eventId, (this :> IEventStore<string>).TryGetLastEventId version name))
                        let checkIds =
                            lastEventIdsPerContext
                            |> List.forall (fun (eventId, lastEventId) -> lastEventId.IsNone && eventId = 0 || lastEventId.Value = eventId)
                        let result =
                            if checkIds then
                                try
                                    let cmdList = 
                                        arg 
                                        |>>
                                            // take this opportunity to evaluate if eventId cold be managed by the db function to do the check
                                            fun (eventId, events, version,  name) -> 
                                                let stream_name = version + name
                                                let command = new NpgsqlCommand(sprintf "SELECT insert%s_event_and_return_id(@event);" stream_name, conn)

                                                events
                                                |>> 
                                                    fun event ->
                                                        command.Parameters.AddWithValue("event", event ) |> ignore
                                                        let result = command.ExecuteScalar() 
                                                        result :?> int
                                        
                                    transaction.Commit()    
                                    cmdList |> Ok
                                with
                                    | _ as ex ->
                                        logger.Value.LogDebug (sprintf "an error occurred: %A" ex.Message)
                                        transaction.Rollback()
                                        ex.Message |> Error
                            else
                                transaction.Rollback()
                                Error "EventId is not the last one"
                        try
                            return result
                        finally
                            conn.Close()
                    }, evenStoreTimeout)
                    
            member this.MultiAddEventsMd md (arg: List<EventId * List<Json> * Version * Name>) =
                logger.Value.LogDebug (sprintf "MultiAddEventsMd %A %s" arg md)
                let conn = new NpgsqlConnection(connection)
                conn.Open()
                let transaction = conn.BeginTransaction() 
                Async.RunSynchronously
                    (async {
                        let lastEventIdsPerContext =
                            arg |>> (fun (eventId, _, version, name) -> (eventId, (this :> IEventStore<string>).TryGetLastEventId version name))
                        let checkIds =
                            lastEventIdsPerContext
                            |> List.forall (fun (eventId, lastEventId) -> lastEventId.IsNone && eventId = 0 || lastEventId.Value = eventId)
                        let result =
                            if checkIds then
                                try
                                    let cmdList = 
                                        arg 
                                        |>>
                                            // take this opportunity to evaluate if eventId could be managed by the db function to do the check
                                            fun (eventId, events, version,  name) -> 
                                                let stream_name = version + name
                                                let command = new NpgsqlCommand(sprintf "SELECT insert_md%s_event_and_return_id(@event, @md);" stream_name, conn)

                                                events
                                                |>> 
                                                    fun event ->
                                                        command.Parameters.AddWithValue("event", event ) |> ignore
                                                        command.Parameters.AddWithValue("md", md ) |> ignore
                                                        let result = command.ExecuteScalar() 
                                                        result :?> int
                                        
                                    transaction.Commit()    
                                    cmdList |> Ok
                                with
                                    | _ as ex ->
                                        logger.Value.LogDebug (sprintf "an error occurred: %A" ex.Message)
                                        transaction.Rollback()
                                        ex.Message |> Error
                            else
                                transaction.Rollback()
                                Error "EventId is not the last one"
                        try
                            return result
                        finally
                            conn.Close()
                    }, evenStoreTimeout)
                
            member this.GetEventsAfterId version id name =
                logger.Value.LogDebug (sprintf "GetEventsAfterId %s %s %d" version name id)
                let query = sprintf "SELECT id, event FROM events%s%s WHERE id > @id ORDER BY id"  version name
                Async.RunSynchronously
                    (async {
                        return
                            try
                                connection
                                |> Sql.connect
                                |> Sql.query query
                                |> Sql.parameters ["id", Sql.int id]
                                |> Sql.execute ( fun read ->
                                    (
                                        read.int "id",
                                        readAsText read "event"
                                    )
                                )
                                |> Seq.toList
                                |> Ok
                            with
                            | _ as ex ->
                                logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                                ex.Message |> Error
                    }, evenStoreTimeout)

            member this.SetSnapshot version (id: int, snapshot: Json) name =
                logger.Value.LogDebug (sprintf "SetSnapshot %s %A %s" version id name)
                let command = sprintf "INSERT INTO snapshots%s%s (event_id, snapshot, timestamp) VALUES (@event_id, @snapshot, @timestamp)" version name
                let tryEvent = ((this :> IEventStore<string>).TryGetEvent version id name)
                match tryEvent with
                | None -> Error (sprintf "event %d not found" id)
                | Some event ->
                    try
                        Async.RunSynchronously
                            (async {
                                return
                                    connection
                                    |> Sql.connect
                                    |> Sql.executeTransaction
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
                            }, evenStoreTimeout)
                        |> ignore
                        |> Ok
                    with
                    | _ as ex ->
                        logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                        ex.Message |> Error

            member this.SetInitialAggregateState aggregateId version name json =
                logger.Value.LogDebug (sprintf "SetInitialAggregateState %A %s %s" aggregateId version name)
                let insertSnapshot = sprintf "INSERT INTO snapshots%s%s (aggregate_id, snapshot, timestamp) VALUES (@aggregate_id, @snapshot, @timestamp)" version name
                let firstEmptyAggregateEvent = sprintf "INSERT INTO aggregate_events%s%s (aggregate_id) VALUES (@aggregate_id)" version name
                Async.RunSynchronously
                    (async {
                        return
                            try
                                let _ =
                                    connection
                                    |> Sql.connect
                                    |> Sql.executeTransaction
                                        [
                                            insertSnapshot,
                                                [
                                                    [
                                                        ("@aggregate_id", Sql.uuid aggregateId);
                                                        ("snapshot",  sqlJson json);
                                                        ("timestamp", Sql.timestamptz System.DateTime.UtcNow)
                                                    ]
                                                ]
                                            firstEmptyAggregateEvent,
                                                [
                                                    [
                                                        ("@aggregate_id", Sql.uuid aggregateId)
                                                    ]
                                                ]
                                        ]
                                () |> Ok
                            with
                            | _ as ex ->
                                logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                                ex.Message |> Error
                    }, evenStoreTimeout)

            member this.SetInitialAggregateStateAndAddEvents eventId aggregateId aggregateVersion aggregatename json contextVersion contextName events =
                logger.Value.LogDebug "entered in setInitialAggregateStateAndAddEvents"

                let insertSnapshot = sprintf "INSERT INTO snapshots%s%s (aggregate_id,  snapshot, timestamp) VALUES (@aggregate_id,  @snapshot, @timestamp)" aggregateVersion aggregatename
                let insertFirstEmptyAggregateEvent = sprintf "INSERT INTO aggregate_events%s%s (aggregate_id) VALUES (@aggregate_id)" aggregateVersion aggregatename

                let insertEvents = sprintf "SELECT insert%s_event_and_return_id(@event);" (contextVersion + contextName)
                let conn = new NpgsqlConnection(connection)

                conn.Open()
                let transaction = conn.BeginTransaction()
                Async.RunSynchronously
                    (async {
                        let lastEventId = (this :> IEventStore<string>).TryGetLastEventId contextVersion contextName
                        let result =
                            if (lastEventId.IsNone && eventId = 0) || (lastEventId.IsSome && lastEventId.Value = eventId) then
                                try
                                    let ids =
                                        events
                                        |>>
                                            fun event -> 
                                                let command' = new NpgsqlCommand(insertEvents, conn)
                                                command'.Parameters.AddWithValue("event", event ) |> ignore
                                                let result = command'.ExecuteScalar() 
                                                result :?> int
                                    let _ =
                                        connection
                                        |> Sql.connect
                                        |> Sql.executeTransaction
                                            [
                                                insertSnapshot,
                                                    [
                                                        [
                                                            ("@aggregate_id", Sql.uuid aggregateId);
                                                            ("snapshot",  sqlJson json);
                                                            ("timestamp", Sql.timestamptz System.DateTime.UtcNow)
                                                        ]
                                                    ]
                                                insertFirstEmptyAggregateEvent,
                                                    [
                                                        [
                                                            ("@aggregate_id", Sql.uuid aggregateId)
                                                        ]
                                                    ]
                                            ]
                                    transaction.Commit()
                                    ids |> Ok
                                with
                                    | _ as ex ->
                                        logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                                        transaction.Rollback()
                                        ex.Message |> Error
                            else
                                transaction.Rollback()
                                Error "EventId is not the last one"
                        try
                            return result
                        finally
                            conn.Close()
                    }, evenStoreTimeout)

            member this.SetInitialAggregateStateAndAddEventsMd eventId aggregateId aggregateVersion aggregatename initInstance contextVersion contextName md events =
                logger.Value.LogDebug "entered in setInitialAggregateStateAndAddEvents"

                let insertSnapshot = sprintf "INSERT INTO snapshots%s%s (aggregate_id,  snapshot, timestamp) VALUES (@aggregate_id,  @snapshot, @timestamp)" aggregateVersion aggregatename
                let insertFirstEmptyAggregateEvent = sprintf "INSERT INTO aggregate_events%s%s (aggregate_id) VALUES (@aggregate_id)" aggregateVersion aggregatename

                let insertEvents = sprintf "SELECT insert_md%s_event_and_return_id(@event, @md);" (contextVersion + contextName)
                let conn = new NpgsqlConnection(connection)

                conn.Open()
                let transaction = conn.BeginTransaction()
                Async.RunSynchronously
                    (async {
                        let lastEventId = (this :> IEventStore<string>).TryGetLastEventId contextVersion contextName
                        let result =
                            if (lastEventId.IsNone && eventId = 0) || (lastEventId.IsSome && lastEventId.Value = eventId) then
                                try
                                    let ids =
                                        events
                                        |>>
                                            fun event -> 
                                                let command' = new NpgsqlCommand(insertEvents, conn)
                                                command'.Parameters.AddWithValue("event", event ) |> ignore
                                                command'.Parameters.AddWithValue("md", md ) |> ignore
                                                let result = command'.ExecuteScalar() 
                                                result :?> int
                                    let _ =
                                        connection
                                        |> Sql.connect
                                        |> Sql.executeTransaction
                                            [
                                                insertSnapshot,
                                                    [
                                                        [
                                                            ("@aggregate_id", Sql.uuid aggregateId);
                                                            ("snapshot",  sqlJson initInstance);
                                                            ("timestamp", Sql.timestamptz System.DateTime.UtcNow)
                                                        ]
                                                    ]
                                                insertFirstEmptyAggregateEvent,
                                                    [
                                                        [
                                                            ("@aggregate_id", Sql.uuid aggregateId)
                                                        ]
                                                    ]
                                            ]
                                    transaction.Commit()
                                    ids |> Ok
                                with
                                    | _ as ex ->
                                        logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                                        transaction.Rollback()
                                        ex.Message |> Error
                            else
                                transaction.Rollback()
                                Error "EventId is not the last one"
                        try
                            return result
                        finally
                            conn.Close()
                    }, evenStoreTimeout)
              
             
            member this.SetInitialAggregateStateAndAddAggregateEvents eventId aggregateId aggregateVersion aggregatename secondAggregateId json contextVersion contextName events =
                logger.Value.LogDebug "entered in SetInitialAggregateStateAndAddAggregateEvents"

                let insertSnapshot = sprintf "INSERT INTO snapshots%s%s (aggregate_id,  snapshot, timestamp) VALUES (@aggregate_id,  @snapshot, @timestamp)" aggregateVersion aggregatename
                let insertFirstEmptyAggregateEvent = sprintf "INSERT INTO aggregate_events%s%s (aggregate_id) VALUES (@aggregate_id)" aggregateVersion aggregatename

                let insertEvents = sprintf "SELECT insert%s_aggregate_event_and_return_id(@event, @aggregate_id);" (contextVersion + contextName)
                let conn = new NpgsqlConnection(connection)
                conn.Open()
                let transaction = conn.BeginTransaction()
                let lastEventId =
                    (this :> IEventStore<string>).TryGetLastAggregateEventId contextVersion contextName secondAggregateId
                
                Async.RunSynchronously
                    (async {
                        let result =
                            if (lastEventId.IsNone && eventId = 0) || (lastEventId.IsSome && lastEventId.Value = eventId) then
                                try
                                    let ids =
                                        events
                                        |>> 
                                            (
                                                fun x ->
                                                    let command' = new NpgsqlCommand(insertEvents, conn)
                                                    command'.Parameters.AddWithValue("event", x ) |> ignore
                                                    command'.Parameters.AddWithValue("@aggregate_id", secondAggregateId ) |> ignore
                                                    let result = command'.ExecuteScalar() 
                                                    result :?> int
                                            )
                                    let _ =
                                        connection
                                        |> Sql.connect
                                        |> Sql.executeTransaction
                                            [
                                                insertSnapshot,
                                                    [
                                                        [
                                                            ("@aggregate_id", Sql.uuid aggregateId);
                                                            ("snapshot",  sqlJson json);
                                                            ("timestamp", Sql.timestamptz System.DateTime.UtcNow)
                                                        ]
                                                    ]
                                                insertFirstEmptyAggregateEvent,
                                                    [
                                                        [
                                                            ("@aggregate_id", Sql.uuid aggregateId)
                                                        ]
                                                    ]
                                            ]
                                    transaction.Commit()
                                    ids |> Ok
                                with
                                    | _ as ex ->
                                        logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                                        transaction.Rollback()
                                        ex.Message |> Error
                            else
                                transaction.Rollback()
                                Error "EventId is not the last one"
                        try
                            return result
                        finally
                            conn.Close()
                    }, evenStoreTimeout)
                    
             
            member this.SetInitialAggregateStateAndAddAggregateEventsMd eventId aggregateId aggregateVersion aggregatename secondAggregateId json contextVersion contextName md events =
                logger.Value.LogDebug "entered in SetInitialAggregateStateAndAddAggregateEvents"

                let insertSnapshot = sprintf "INSERT INTO snapshots%s%s (aggregate_id,  snapshot, timestamp) VALUES (@aggregate_id,  @snapshot, @timestamp)" aggregateVersion aggregatename
                let insertFirstEmptyAggregateEvent = sprintf "INSERT INTO aggregate_events%s%s (aggregate_id) VALUES (@aggregate_id)" aggregateVersion aggregatename

                let insertEvents = sprintf "SELECT insert_md%s_aggregate_event_and_return_id(@event, @aggregate_id, @md);" (contextVersion + contextName)
                let conn = new NpgsqlConnection(connection)
                conn.Open()
                let transaction = conn.BeginTransaction()
                let lastEventId =
                    (this :> IEventStore<string>).TryGetLastAggregateEventId contextVersion contextName secondAggregateId
                
                Async.RunSynchronously
                    (async {
                        let result =
                            if (lastEventId.IsNone && eventId = 0) || (lastEventId.IsSome && lastEventId.Value = eventId) then
                                try
                                    let ids =
                                        events
                                        |>> 
                                            (
                                                fun x ->
                                                    let command' = new NpgsqlCommand(insertEvents, conn)
                                                    command'.Parameters.AddWithValue("event", x ) |> ignore
                                                    command'.Parameters.AddWithValue("@aggregate_id", secondAggregateId ) |> ignore
                                                    command'.Parameters.AddWithValue("md", md ) |> ignore
                                                    let result = command'.ExecuteScalar() 
                                                    result :?> int
                                            )
                                    let _ =
                                        connection
                                        |> Sql.connect
                                        |> Sql.executeTransaction
                                            [
                                                insertSnapshot,
                                                    [
                                                        [
                                                            ("@aggregate_id", Sql.uuid aggregateId);
                                                            ("snapshot",  sqlJson json);
                                                            ("timestamp", Sql.timestamptz System.DateTime.UtcNow)
                                                        ]
                                                    ]
                                                insertFirstEmptyAggregateEvent,
                                                    [
                                                        [
                                                            ("@aggregate_id", Sql.uuid aggregateId)
                                                        ]
                                                    ]
                                            ]
                                    transaction.Commit()
                                    ids |> Ok
                                with
                                    | _ as ex ->
                                        logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                                        transaction.Rollback()
                                        ex.Message |> Error
                            else
                                transaction.Rollback()
                                Error "EventId is not the last one"
                        try
                            return result
                        finally
                            conn.Close()
                    }, evenStoreTimeout)
            member this.SetInitialAggregateStateAndMultiAddAggregateEvents aggregateId version name jsonSnapshot events =
                logger.Value.LogDebug "entered in SetInitialAggregateStateAndMultiAddAggregateEvents"
                let insertSnapshot = sprintf "INSERT INTO snapshots%s%s (aggregate_id,  snapshot, timestamp) VALUES (@aggregate_id,  @snapshot, @timestamp)" version name
                let insertFirstEmptyAggregateEvent = sprintf "INSERT INTO aggregate_events%s%s (aggregate_id) VALUES (@aggregate_id)" version name
                
                let conn = new NpgsqlConnection(connection)
                
                conn.Open()
                
                let transaction = conn.BeginTransaction()
                
                let lastEventIds =
                    events 
                    |>> 
                    fun (_, _, version, name, aggrId) -> 
                        ((this :> IEventStore<string>).TryGetLastAggregateEventId version name aggrId) // |>> (fun x -> x)
                
                let eventIds =
                    events
                    |>> fun (eventId, _, _, _, _) -> eventId
                
                let checks =
                    List.zip lastEventIds eventIds
                    |> List.forall (fun (lastEventId, eventId) -> lastEventId.IsNone && eventId = 0 || lastEventId.Value = eventId)
                    
                Async.RunSynchronously
                    (async {
                        let result =
                            if checks then
                                try
                                    let ids =
                                        events
                                        |>> 
                                            (
                                                fun (_, events, version, name, aggId) ->
                                                    let stream_name = version + name
                                                    let command' = new NpgsqlCommand(sprintf "SELECT insert%s_aggregate_event_and_return_id(@event, @aggregate_id);" stream_name, conn)
                                                    events
                                                    |>> 
                                                        (
                                                            fun x ->
                                                                command'.Parameters.AddWithValue("event", x ) |> ignore
                                                                command'.Parameters.AddWithValue("@aggregate_id", aggId ) |> ignore
                                                                let result = command'.ExecuteScalar()
                                                                result  :?> int
                                                        )
                                            )
                                    let _ =
                                        connection
                                        |> Sql.connect
                                        |> Sql.executeTransaction
                                            [
                                                insertSnapshot,
                                                    [
                                                        [
                                                            ("@aggregate_id", Sql.uuid aggregateId);
                                                            ("snapshot",  sqlJson jsonSnapshot);
                                                            ("timestamp", Sql.timestamptz System.DateTime.UtcNow)
                                                        ]
                                                    ]
                                                insertFirstEmptyAggregateEvent,
                                                    [
                                                        [
                                                            ("@aggregate_id", Sql.uuid aggregateId)
                                                        ]
                                                    ]
                                            ]
                                    transaction.Commit()
                                    ids |> Ok
                                with
                                    | _ as ex ->
                                        logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                                        transaction.Rollback()
                                        ex.Message |> Error
                            else
                                transaction.Rollback()
                                Error "EventId is not the last one"
                        try 
                            return result
                        finally
                            conn.Close()
                    }, evenStoreTimeout)
                            
            member this.SetInitialAggregateStateAndMultiAddAggregateEventsMd  aggregateId version name jsonSnapshot md events =
                logger.Value.LogDebug "entered in SetInitialAggregateStateAndMultiAddAggregateEvents"
                let insertSnapshot = sprintf "INSERT INTO snapshots%s%s (aggregate_id,  snapshot, timestamp) VALUES (@aggregate_id,  @snapshot, @timestamp)" version name
                let insertFirstEmptyAggregateEvent = sprintf "INSERT INTO aggregate_events%s%s (aggregate_id) VALUES (@aggregate_id)" version name
                
                let conn = new NpgsqlConnection(connection)
                
                conn.Open()
                
                let transaction = conn.BeginTransaction()
                
                let lastEventIds =
                    events 
                    |>> 
                    fun (_, _, version, name, aggrId) -> 
                        ((this :> IEventStore<string>).TryGetLastAggregateEventId version name aggrId) // |>> (fun x -> x)
                
                let eventIds =
                    events
                    |>> fun (eventId, _, _, _, _) -> eventId
                
                let checks =
                    List.zip lastEventIds eventIds
                    |> List.forall (fun (lastEventId, eventId) -> lastEventId.IsNone && eventId = 0 || lastEventId.Value = eventId)
                    
                Async.RunSynchronously
                    (async {
                        let result =
                            if checks then
                                try
                                    let ids =
                                        events
                                        |>> 
                                            (
                                                fun (_, events, version, name, aggId) ->
                                                    let stream_name = version + name
                                                    let command' = new NpgsqlCommand(sprintf "SELECT insert_md%s_aggregate_event_and_return_id(@event, @aggregate_id, @md);" stream_name, conn)
                                                    events
                                                    |>> 
                                                        (
                                                            fun x ->
                                                                command'.Parameters.AddWithValue("event", x ) |> ignore
                                                                command'.Parameters.AddWithValue("@aggregate_id", aggId ) |> ignore
                                                                command'.Parameters.AddWithValue("md", md ) |> ignore
                                                                let result = command'.ExecuteScalar()
                                                                result  :?> int
                                                        )
                                            )
                                    let _ =
                                        connection
                                        |> Sql.connect
                                        |> Sql.executeTransaction
                                            [
                                                insertSnapshot,
                                                    [
                                                        [
                                                            ("@aggregate_id", Sql.uuid aggregateId);
                                                            ("snapshot",  sqlJson jsonSnapshot);
                                                            ("timestamp", Sql.timestamptz System.DateTime.UtcNow)
                                                        ]
                                                    ]
                                                insertFirstEmptyAggregateEvent,
                                                    [
                                                        [
                                                            ("@aggregate_id", Sql.uuid aggregateId)
                                                        ]
                                                    ]
                                            ]
                                    transaction.Commit()
                                    ids |> Ok
                                with
                                    | _ as ex ->
                                        logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                                        transaction.Rollback()
                                        ex.Message |> Error
                            else
                                transaction.Rollback()
                                Error "EventId is not the last one"
                        try 
                            return result
                        finally
                            conn.Close()
                    }, evenStoreTimeout)
                    
            member this.SetAggregateSnapshot version (aggregateId: AggregateId, eventId: int, snapshot: Json) name =
                logger.Value.LogDebug "entered in setAggregateSnapshot"
                let command = sprintf "INSERT INTO snapshots%s%s (aggregate_id, event_id, snapshot, timestamp) VALUES (@aggregate_id, @event_id, @snapshot, @timestamp)" version name
                let tryEvent = ((this :> IEventStore<string>).TryGetEvent version eventId name)
                match (tryEvent, eventId) with
                | (None, x) when x <> 0 -> Error (sprintf "event %d not found" eventId)
                | (Some event, _) -> 
                    try
                        Async.RunSynchronously (
                            async {
                                return
                                    connection
                                    |> Sql.connect
                                    |> Sql.executeTransaction
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
                            }, evenStoreTimeout)
                        |> ignore
                        |> Ok
                    with
                    | _ as ex ->
                        logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                        ex.Message |> Error
                | None, 0  ->
                    let command = sprintf "INSERT INTO snapshots%s%s (aggregate_id, event_id, snapshot, timestamp) VALUES (@aggregate_id, null, @snapshot, @timestamp)" version name
                    try
                        Async.RunSynchronously (
                            async {
                                return
                                    connection
                                    |> Sql.connect
                                    |> Sql.executeTransaction
                                        [
                                            command,
                                                [
                                                    [
                                                        ("@aggregate_id", Sql.uuid aggregateId);
                                                        ("snapshot",  sqlJson snapshot);
                                                        ("timestamp", Sql.timestamptz System.DateTime.UtcNow)
                                                    ]
                                                ]
                                        ]
                            }, evenStoreTimeout)
                        |> ignore
                        |> Ok
                    with    
                    | _ as ex ->
                        logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                        ex.Message |> Error
            
            member this.GetEventsInATimeInterval version name dateFrom dateTo =
                logger.Value.LogDebug (sprintf "GetEventsInATimeInterval %s %s %A %A" version name dateFrom dateTo)
                let query = sprintf "SELECT id, event FROM events%s%s WHERE timestamp >= @dateFrom AND timestamp <= @dateTo ORDER BY id" version name
                try
                    Async.RunSynchronously
                        (async {
                            return
                                connection
                                |> Sql.connect
                                |> Sql.query query
                                |> Sql.parameters ["dateFrom", Sql.timestamp dateFrom; "dateTo", Sql.timestamp dateTo]
                                |> Sql.execute ( fun read ->
                                    (
                                        read.int "id",
                                        readAsText read "event"
                                    )
                                )
                                |> Seq.toList
                            }, evenStoreTimeout)
                        |> Ok
                with
                | _ as ex ->
                    log.Error (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message
            
            member this.GetAggregateEventsInATimeInterval version name aggregateId dateFrom dateTo =
                logger.Value.LogDebug (sprintf "GetAggregateEventsInATimeInterval %s %s %A %A" version name dateFrom dateTo)
                let query = sprintf "SELECT id, event FROM events%s%s WHERE aggregate_id = @aggregateId AND timestamp >= @dateFrom AND timestamp <= @dateTo ORDER BY id" version name
                try
                    Async.RunSynchronously
                        (async {
                            return
                                connection
                                |> Sql.connect
                                |> Sql.query query
                                |> Sql.parameters ["aggregateId", Sql.uuid aggregateId; "dateFrom", Sql.timestamp dateFrom; "dateTo", Sql.timestamp dateTo]
                                |> Sql.execute ( fun read ->
                                    (
                                        read.int "id",
                                        readAsText read "event"
                                    )
                                )
                                |> Seq.toList
                            }, evenStoreTimeout)
                    |> Ok     
                with
                | _ as ex ->
                    logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message
           
            member this.GetMultipleAggregateEventsInATimeInterval version name aggregateIds dateFrom dateTo =
                let aggregateIdsArray = aggregateIds |> Array.ofList
                logger.Value.LogDebug (sprintf "GetMultipleAggregateEventsInATimeInterval %s %s %A %A" version name dateFrom dateTo)
                let query = sprintf "SELECT id, aggregate_id, event FROM events%s%s WHERE aggregate_id = ANY(@aggregateIds) AND timestamp >= @dateFrom AND timestamp <= @dateTo ORDER BY id" version name
                try
                    Async.RunSynchronously
                        (async {
                            return
                                connection
                                |> Sql.connect
                                |> Sql.query query
                                |> Sql.parameters ["aggregateIds", Sql.uuidArray aggregateIdsArray; "dateFrom", Sql.timestamp dateFrom; "dateTo", Sql.timestamp dateTo]
                                |> Sql.execute ( fun read ->
                                    (
                                        read.int "id",
                                        read.uuid "aggregate_id",
                                        readAsText read "event"
                                    )
                                )
                                |> Seq.toList
                            }, evenStoreTimeout)
                    |> Ok    
                with
                | _ as ex ->
                    logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message
             
            member this.GetAggregateSnapshotsInATimeInterval version name dateFrom dateTo =
                logger.Value.LogDebug (sprintf "GetAggregateSnapshotsInATimeInterval %s %s %A %A" version name dateFrom dateTo)
                let query = sprintf "SELECT id, aggregate_id, timestamp, snapshot FROM snapshots%s%s where timestamp >= @dateFrom AND timestamp <= @dateTo ORDER BY id" version name
                try
                    Async.RunSynchronously
                        (async {
                            return
                                connection
                                |> Sql.connect
                                |> Sql.query query
                                |> Sql.parameters ["dateFrom", Sql.timestamp dateFrom; "dateTo", Sql.timestamp dateTo]
                                |> Sql.execute ( fun read ->
                                    (
                                        read.int "id",
                                        read.uuid "aggregate_id",
                                        read.dateTime "timestamp",
                                        readAsText read "snapshot"
                                    )
                                )
                                |> Seq.toList
                            }, evenStoreTimeout)
                    |> Ok     
                with
                | _ as ex ->
                    logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                    ex.Message |> Error

            member this.GetAllAggregateEventsInATimeInterval version name dateFrom dateTo =
                logger.Value.LogDebug (sprintf "GetAllAggregateEventsInATimeInterval %s %s %A %A" version name dateFrom dateTo)
                let query = sprintf "SELECT id, event FROM events%s%s WHERE timestamp >= @dateFrom AND timestamp <= @dateTo ORDER BY id" version name
                try
                    let result =
                        Async.RunSynchronously
                            (async {
                                return
                                    connection
                                    |> Sql.connect
                                    |> Sql.query query
                                    |> Sql.parameters ["dateFrom", Sql.timestamp dateFrom; "dateTo", Sql.timestamp dateTo]
                                    |> Sql.execute ( fun read ->
                                        (
                                            read.int "id",
                                            readAsText read "event"
                                        )
                                    )
                                    |> Seq.toList
                                }, evenStoreTimeout)
                    result |> Ok         
                with
                | _ as ex ->
                    logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error (ex.Message)
            member this.TryGetLastSnapshotId version name =
                logger.Value.LogDebug (sprintf "TryGetLastSnapshotId %s %s" version name)
                let query = sprintf "SELECT event_id, id FROM snapshots%s%s ORDER BY id DESC LIMIT 1" version name
                try
                    Async.RunSynchronously
                        (async {
                            return
                                connection
                                |> Sql.connect
                                |> Sql.query query
                                |> Sql.execute (fun read ->
                                    (
                                        read.int "event_id",
                                        read.int "id"
                                    )
                                )
                                |> Seq.tryHead
                        }, evenStoreTimeout)
                with
                | _ as ex ->
                    logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                    None
          
            member this.TryGetFirstSnapshot version name aggregateId =
                logger.Value.LogDebug (sprintf "TryGetFirstSnapshot %s %s %A" version name aggregateId)
                let query = sprintf "SELECT id, snapshot FROM snapshots%s%s WHERE aggregate_id = @aggregateId ORDER BY id ASC LIMIT 1" version name
                
                try
                    Async.RunSynchronously
                        (async {
                            return
                                connection
                                |> Sql.connect
                                |> Sql.query query
                                |> Sql.parameters ["aggregateId", Sql.uuid aggregateId]
                                |> Sql.execute (fun read ->
                                    (
                                        read.int "id",
                                        readAsText read "snapshot"
                                    )
                                )
                                |> Seq.tryHead
                                |> Result.ofOption "snapshot not found"
                        }, evenStoreTimeout)
                with
                | _ as ex ->
                    logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error (sprintf "error occurred %A" ex.Message)
            
            member this.TryGetSnapshotById version name id =
                logger.Value.LogDebug (sprintf "TryGetSnapshotById %s %s %d" version name id)
                let query = sprintf "SELECT event_id, snapshot FROM snapshots%s%s WHERE id = @id" version name
                try
                    Async.RunSynchronously
                        (async {
                            return
                                connection
                                |> Sql.connect
                                |> Sql.query query
                                |> Sql.parameters ["id", Sql.int id]
                                |> Sql.execute (fun read ->
                                    (
                                        read.int "event_id",
                                        readAsText read "snapshot"
                                    )
                                )
                                |> Seq.tryHead
                        }, evenStoreTimeout)
                with
                | _ as ex ->
                    logger.Value.LogError (sprintf "TryGetSnapshotById an error occurred: %A" ex.Message)
                    None

            member this.TryGetAggregateSnapshotById version name aggregateId id =
                logger.Value.LogDebug (sprintf "TryGetSnapshotById %s %s %d" version name id)
                let query = sprintf "SELECT event_id, snapshot FROM snapshots%s%s WHERE id = @id" version name
                try
                    Async.RunSynchronously
                        (async {
                            return
                                connection
                                |> Sql.connect
                                |> Sql.query query
                                |> Sql.parameters ["id", Sql.int id]
                                |> Sql.execute (fun read ->
                                    (
                                        read.intOrNone "event_id",
                                        readAsText read "snapshot"
                                    )
                                )
                                |> Seq.tryHead
                            }, evenStoreTimeout)
                with
                | _ as ex ->
                    logger.Value.LogError (sprintf "TryGetSnapshotById an error occurred: %A" ex.Message)
                    None
                
            member this.TryGetLastAggregateSnapshotEventId version name aggregateId =
                logger.Value.LogDebug (sprintf "TryGetLastAggregateSnapshotEventId %s %s" version name)
                let query = sprintf "SELECT event_id FROM snapshots%s%s WHERE aggregate_id = @aggregateId ORDER BY id DESC LIMIT 1" version name
                try
                    Async.RunSynchronously
                        (async {
                            return
                                connection
                                |> Sql.connect
                                |> Sql.query query
                                |> Sql.parameters ["aggregateId", Sql.uuid aggregateId]
                                |> Sql.execute (fun read -> read.int "event_id")
                                |> Seq.tryHead
                        }, evenStoreTimeout)
                with        
                | _ as ex ->
                    logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                    None         

            member this.TryGetLastAggregateEventId version name aggregateId =
                logger.Value.LogDebug (sprintf "TryGetLastEventId %s %s" version name)
                let query = sprintf "SELECT id FROM events%s%s where aggregate_id = @aggregateId ORDER BY id DESC LIMIT 1" version name
                try
                    Async.RunSynchronously
                        (async {
                            return
                                connection
                                |> Sql.connect
                                |> Sql.query query 
                                |> Sql.parameters ["aggregateId", Sql.uuid aggregateId]
                                |> Sql.execute  (fun read ->
                                    (
                                        read.int "id"
                                    )
                                )
                                |> Seq.tryHead
                            }, evenStoreTimeout)
                with
                | _ as ex ->
                    logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                    None
                    
            member this.AddAggregateEvents (eventId: EventId) (version: Version) (name: Name) (aggregateId: System.Guid) (events: List<Json>): Result<List<int>,string> =
                logger.Value.LogDebug (sprintf "AddAggregateEvents %s %s %A %A" version name aggregateId events)
                let stream_name = version + name
                let command = sprintf "SELECT insert%s_aggregate_event_and_return_id(@event, @aggregate_id);" stream_name
                let conn = new NpgsqlConnection(connection)

                conn.Open()
                let transaction = conn.BeginTransaction() 
                let lastEventId = 
                    (this :> IEventStore<string>).TryGetLastAggregateEventId version name aggregateId
                Async.RunSynchronously
                    (async {
                        let result =
                            if (lastEventId.IsNone &&  eventId = 0) || (lastEventId.IsSome && lastEventId.Value = eventId) then
                                try
                                    let ids =
                                        events 
                                        |>> 
                                            (
                                                fun x ->
                                                    let command' = new NpgsqlCommand(command, conn)
                                                    command'.Parameters.AddWithValue("event", x ) |> ignore
                                                    command'.Parameters.AddWithValue("@aggregate_id", aggregateId ) |> ignore
                                                    let result = command'.ExecuteScalar() 
                                                    result :?> int
                                            )
                                    transaction.Commit()
                                    ids |> Ok
                                with
                                    | _ as ex ->
                                        logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                                        transaction.Rollback()
                                        ex.Message |> Error
                            else
                                transaction.Rollback()
                                Error "EventId is not the last one"
                        try
                            return result
                        finally
                            conn.Close()
                    }, evenStoreTimeout)
                    
            member this.AddAggregateEventsMd (eventId: EventId) (version: Version) (name: Name) (aggregateId: System.Guid) (md: Metadata) (events: List<Json>): Result<List<int>,string> =
                logger.Value.LogDebug (sprintf "AddAggregateEventsMd %s %s %A %A %s" version name aggregateId events md)
                let stream_name = version + name
                let command = sprintf "SELECT insert_md%s_aggregate_event_and_return_id(@event, @aggregate_id, @md);" stream_name
                let conn = new NpgsqlConnection(connection)

                conn.Open()
                let transaction = conn.BeginTransaction() 
                let lastEventId = 
                    (this :> IEventStore<string>).TryGetLastAggregateEventId version name aggregateId
                Async.RunSynchronously
                    (async {
                        let result =
                            if (lastEventId.IsNone &&  eventId = 0) || (lastEventId.IsSome && lastEventId.Value = eventId) then
                                try
                                    let ids =
                                        events 
                                        |>> 
                                            (
                                                fun x ->
                                                    let command' = new NpgsqlCommand(command, conn)
                                                    command'.Parameters.AddWithValue("event", x ) |> ignore
                                                    command'.Parameters.AddWithValue("@aggregate_id", aggregateId ) |> ignore
                                                    command'.Parameters.AddWithValue("md", md ) |> ignore
                                                    let result = command'.ExecuteScalar() 
                                                    result :?> int
                                            )
                                    transaction.Commit()
                                    ids |> Ok
                                with
                                    | _ as ex ->
                                        logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                                        transaction.Rollback()
                                        ex.Message |> Error
                            else
                                transaction.Rollback()
                                Error "EventId is not the last one"
                        try
                            return result
                        finally
                            conn.Close()
                    }, evenStoreTimeout)

            member this.MultiAddAggregateEvents (arg: List<EventId * List<Json> * Version * Name * System.Guid>) =
                logger.Value.LogDebug (sprintf "MultiAddAggregateEvents %A" arg)
                let conn = new NpgsqlConnection(connection)
                conn.Open()
                let transaction = conn.BeginTransaction() 

                let lastEventIds =
                    arg 
                    |>> 
                    fun (_, _, version, name, aggregateId) -> 
                        ((this :> IEventStore<string>).TryGetLastAggregateEventId version name aggregateId) // |>> (fun x -> x)

                let eventIds = 
                    arg
                    |>> fun (eventId, _, _, _, _) -> eventId

                let checks = 
                    List.zip lastEventIds eventIds
                    |> List.forall (fun (lastEventId, eventId) -> lastEventId.IsNone && eventId = 0 || lastEventId.Value = eventId)

                Async.RunSynchronously
                    (async {
                        let result =
                            if checks then
                                try
                                    let cmdList = 
                                        arg 
                                        |>>
                                            fun (_, events, version,  name, aggregateId) ->
                                                let stream_name = version + name
                                                events
                                                |>> 
                                                    fun event ->
                                                        let command = new NpgsqlCommand(sprintf "SELECT insert%s_aggregate_event_and_return_id(@event, @aggregate_id);" stream_name, conn)
                                                        (
                                                            command.Parameters.AddWithValue("event", event ) |> ignore
                                                            command.Parameters.AddWithValue("@aggregate_id", aggregateId ) |> ignore
                                                            let result = command.ExecuteScalar() 
                                                            result :?> int
                                                        )
                                        
                                    transaction.Commit()    
                                    cmdList |> Ok
                                with
                                    | _ as ex ->
                                        logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                                        transaction.Rollback()
                                        ex.Message |> Error
                            else
                                transaction.Rollback()
                                Error "EventId is not the last one"
                        try
                            return result
                        finally
                            conn.Close()
                    }, evenStoreTimeout)
                    
            member this.MultiAddAggregateEventsMd md (arg: List<EventId * List<Json> * Version * Name * System.Guid>) =
                logger.Value.LogDebug (sprintf "MultiAddAggregateEventsMd %A %s" arg md)
                let conn = new NpgsqlConnection(connection)
                conn.Open()
                let transaction = conn.BeginTransaction() 

                let lastEventIds =
                    arg 
                    |>> 
                    fun (_, _, version, name, aggregateId) -> 
                        ((this :> IEventStore<string>).TryGetLastAggregateEventId version name aggregateId)

                let eventIds = 
                    arg
                    |>> fun (eventId, _, _, _, _) -> eventId

                let checks = 
                    List.zip lastEventIds eventIds
                    |> List.forall (fun (lastEventId, eventId) -> lastEventId.IsNone && eventId = 0 || lastEventId.Value = eventId)

                Async.RunSynchronously
                    (async {
                        let result =
                            if checks then
                                try
                                    let cmdList = 
                                        arg 
                                        |>>
                                            fun (_, events, version,  name, aggregateId) ->
                                                let stream_name = version + name
                                                events
                                                |>> 
                                                    fun event ->
                                                        let command = new NpgsqlCommand(sprintf "SELECT insert_md%s_aggregate_event_and_return_id(@event, @aggregate_id, @md);" stream_name, conn)
                                                        (
                                                            command.Parameters.AddWithValue("event", event ) |> ignore
                                                            command.Parameters.AddWithValue("@aggregate_id", aggregateId ) |> ignore
                                                            command.Parameters.AddWithValue("md", md ) |> ignore
                                                            let result = command.ExecuteScalar() 
                                                            result :?> int
                                                        )
                                        
                                    transaction.Commit()    
                                    cmdList |> Ok
                                with
                                    | _ as ex ->
                                        logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                                        transaction.Rollback()
                                        ex.Message |> Error
                            else
                                transaction.Rollback()
                                Error "EventId is not the last one"
                        try
                            return result
                        finally
                            conn.Close()
                    }, evenStoreTimeout)

            member this.GetAggregateEventsAfterId version name aggregateId id: Result<List<EventId * Json>,string> =
                logger.Value.LogDebug (sprintf "GetAggregateEventsAfterId %s %s %A %d" version name aggregateId id)
                let query = sprintf "SELECT id, event FROM events%s%s WHERE id > @id and aggregate_id = @aggregateId ORDER BY id"  version name
                Async.RunSynchronously
                    (async {
                        return
                            try 
                                connection
                                |> Sql.connect
                                |> Sql.query query
                                |> Sql.parameters ["id", Sql.int id; "aggregateId", Sql.uuid aggregateId]
                                |> Sql.execute ( fun read ->
                                    (
                                        read.int "id",
                                        readAsText read "event"
                                    )
                                )
                                |> Seq.toList
                                |> Ok
                            with
                            | _ as ex ->
                                logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                                ex.Message |> Error
                        }, evenStoreTimeout)
                    
            member this.GetAggregateEvents version name aggregateId: Result<List<EventId * Json>,string> =
                logger.Value.LogDebug (sprintf "GetAggregateEvents %s %s %A" version name aggregateId)
                let query = sprintf "SELECT id, event FROM events%s%s WHERE aggregate_id = @aggregateId ORDER BY id"  version name
                Async.RunSynchronously
                    (async {
                        return
                            try 
                                connection
                                |> Sql.connect
                                |> Sql.query query
                                |> Sql.parameters ["aggregateId", Sql.uuid aggregateId]
                                |> Sql.execute ( fun read ->
                                    (
                                        read.int "id",
                                        readAsText read "event"
                                    )
                                )
                                |> Seq.toList
                                |> Ok
                            with
                            | _ as ex ->
                                logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                                ex.Message |> Error
                    }, evenStoreTimeout)
            
            member this.GDPRReplaceSnapshotsAndEventsOfAnAggregate version name aggregateId snapshot event =
                logger.Value.LogDebug (sprintf "GDPRReplaceSnapshotsAndEventsOfAnAggregate %s %s %A %A %A" version name aggregateId snapshot event)
                let conn = new NpgsqlConnection(connection)
                let sqlReplaceAllAggregates = (sprintf "UPDATE snapshots%s%s SET snapshot = @snapshot WHERE aggregate_id = @aggregateId" version name)
                let sqlReplaceAllEvents = (sprintf "UPDATE events%s%s SET event = @event WHERE aggregate_id = @aggregateId" version name)
                
                conn.Open()
                let transaction = conn.BeginTransaction()
                Async.RunSynchronously
                    (async {
                        let result =
                            try
                                let _ =
                                    connection
                                    |> Sql.connect
                                    |> Sql.executeTransaction
                                        [
                                            sqlReplaceAllAggregates,
                                                [
                                                    [
                                                        ("@snapshot", sqlJson snapshot);
                                                        ("@aggregateId", Sql.uuid aggregateId)
                                                    ]
                                                ]
                                            sqlReplaceAllEvents,
                                                [
                                                    [
                                                        ("@event", sqlJson event);
                                                        ("@aggregateId", Sql.uuid aggregateId)
                                                    ]
                                                ]
                                        ]
                                transaction.Commit()
                                Ok ()
                            with
                            | _ as ex ->
                                logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                                transaction.Rollback()
                                ex.Message |> Error
                        try
                            return result
                        finally
                            conn.Close()
                    }, evenStoreTimeout)
                
                

                