

namespace Sharpino

open System
open FsToolkit.ErrorHandling
open Npgsql.FSharp
open Npgsql
open FSharpPlus
open FSharpPlus.Operators
open Sharpino
open Sharpino.Storage
open Sharpino.PgStorage
open Sharpino.Definitions
open log4net
open log4net.Config

module PgBinaryStore =
    open Conf
    let log = LogManager.GetLogger (System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)

    type PgBinaryStore (connection: string, readAsBinary: RowReader -> (string -> byte[])) =

        new (connection: string) = PgBinaryStore (connection, readAsBinary)

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
                
        interface IEventStore<byte []> with
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
                                    readAsBinary read "snapshot"
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
                            let result: StoragePgEvent<byte[]> = 
                                {
                                    Id = read.int "id"
                                    JsonEvent = readAsBinary read "event"
                                    Timestamp = read.dateTime "timestamp"
                                }
                            result
                    )
                    |> Async.AwaitTask
                    |> Async.RunSynchronously
                    |> Seq.tryHead
                    
            member this.AddEvents eventId version name events =
                log.Debug (sprintf "AddEvents %s %s %A" version name events)
                let stream_name = version + name
                let command = sprintf "SELECT insert%s_event_and_return_id(@event);" stream_name
                let conn = new NpgsqlConnection(connection)

                conn.Open()
                let transaction = conn.BeginTransaction() 
                async {
                    let lastEventId = (this :> IEventStore<byte[]>).TryGetLastEventId version name
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
                                    log.Error (sprintf "an error occurred: %A" ex.Message)
                                    transaction.Rollback()
                                    ex.Message |> Error
                        else
                            transaction.Rollback()
                            Error "EventId is not the last one"
                    try 
                        return result
                    finally
                        conn.Close()
                }
                |> Async.RunSynchronously

            member this.MultiAddEvents (arg: List<EventId * List<byte array> * Version * Name>) =
                log.Debug (sprintf "MultiAddEvents %A" arg)
                let conn = new NpgsqlConnection(connection)
                conn.Open()
                let transaction = conn.BeginTransaction() 
                async {
                    let lastEventIdsPerContext =
                        arg |>> (fun (eventId, _, version, name) -> (eventId, (this :> IEventStore<byte[]>).TryGetLastEventId version name))
                    let checkIds =
                        lastEventIdsPerContext
                        |> List.forall (fun (eventId, lastEventId) -> lastEventId.IsNone && eventId = 0 || lastEventId.Value = eventId)

                    let result =
                        if checkIds then
                            try
                                let cmdList = 
                                    arg 
                                    |>>
                                        fun (_, events, version,  name) -> 
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
                                    log.Error (sprintf "an error occurred: %A" ex.Message)
                                    transaction.Rollback()
                                    ex.Message |> Error
                        else
                            transaction.Rollback()
                            Error "EventId is not the last one"
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
                            readAsBinary read "event"
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

            member this.SetSnapshot version (id: int, snapshot: byte array) name =
                log.Debug "entered in setSnapshot"
                let command = sprintf "INSERT INTO snapshots%s%s (event_id, snapshot, timestamp) VALUES (@event_id, @snapshot, @timestamp)" version name
                let tryEvent = ((this :> IEventStore<byte []>).TryGetEvent version id name)
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
                                            ("snapshot",  sqlBinary snapshot);
                                            ("timestamp", Sql.timestamp event.Timestamp)
                                        ]
                                    ]
                            ]
                        |> Async.AwaitTask
                        |> Async.RunSynchronously
                    () |> Ok

            member this.SetInitialAggregateState aggregateId version name json =
                log.Debug "entered in setSnapshot"
                let insertSnapshot = sprintf "INSERT INTO snapshots%s%s (aggregate_id,  snapshot, timestamp) VALUES (@aggregate_id,  @snapshot, @timestamp)" version name
                let makeFirstEmptyEvent = sprintf "INSERT INTO aggregate_events%s%s (aggregate_id) VALUES (@aggregate_id)" version name
                try
                    let _ =
                        connection
                        |> Sql.connect
                        |> Sql.executeTransactionAsync
                            [
                                insertSnapshot,
                                    [
                                        [
                                            ("@aggregate_id", Sql.uuid aggregateId);
                                            ("snapshot",  sqlBinary json);
                                            ("timestamp", Sql.timestamptz System.DateTime.UtcNow)
                                        ]
                                    ]
                                makeFirstEmptyEvent,
                                    [
                                        [
                                            ("@aggregate_id", Sql.uuid aggregateId)
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

            member this.SetInitialAggregateStateAndAddEvents eventId aggregateId aggregateVersion aggregatename initSnapshot contextVersion contextName events =
                log.Debug "entered in SetInitialAggregateStateAndAddEvents"
                let insertSnapshot = sprintf "INSERT INTO snapshots%s%s (aggregate_id, snapshot, timestamp) VALUES (@aggregate_id, @snapshot, @timestamp)" aggregateVersion aggregatename
                let insertAggregateEvents = sprintf "INSERT INTO aggregate_events%s%s (aggregate_id) VALUES (@aggregate_id)" aggregateVersion aggregatename
                let insertEvent = sprintf "SELECT insert%s_event_and_return_id(@event);" (contextVersion + contextName)
                let conn = new NpgsqlConnection(connection)
                conn.Open()
                let transaction = conn.BeginTransaction() 
                async {
                    let lastEventId = (this :> IEventStore<byte[]>).TryGetLastEventId contextVersion contextName
                    let result =
                        if (lastEventId.IsNone && eventId = 0) || (lastEventId.IsSome && lastEventId.Value = eventId) then
                            try
                                let ids =
                                    events
                                    |>>
                                        fun event -> 
                                            let command' = new NpgsqlCommand(insertEvent, conn)
                                            command'.Parameters.AddWithValue("event", event ) |> ignore
                                            let result = command'.ExecuteScalar() 
                                            result :?> int
                                let _ =
                                    connection
                                    |> Sql.connect
                                    |> Sql.executeTransactionAsync
                                        [
                                            insertSnapshot,
                                                [
                                                    [
                                                        ("@aggregate_id", Sql.uuid aggregateId);
                                                        ("snapshot",  sqlBinary initSnapshot);
                                                        ("timestamp", Sql.timestamptz System.DateTime.UtcNow)
                                                    ]
                                                ]
                                            insertAggregateEvents,
                                                [
                                                    [
                                                        ("@aggregate_id", Sql.uuid aggregateId)
                                                    ]
                                                ]
                                        ]
                                    |> Async.AwaitTask
                                    |> Async.RunSynchronously
                                transaction.Commit()
                                ids |> Ok
                            with
                                | _ as ex -> 
                                    log.Error (sprintf "an error occurred: %A" ex.Message)
                                    transaction.Rollback()
                                    ex.Message |> Error
                        else
                            transaction.Rollback()
                            Error "EventId is not the last one"
                    try
                        return 
                            result
                    finally
                        conn.Close()
                }
                |> Async.RunSynchronously

            member this.SetAggregateSnapshot version (aggregateId: AggregateId, eventId: int, snapshot: byte array) name =
                log.Debug "entered in setSnapshot"
                let command = sprintf "INSERT INTO snapshots%s%s (aggregate_id, event_id, snapshot, timestamp) VALUES (@aggregate_id, @event_id, @snapshot, @timestamp)" version name
                let tryEvent = ((this :> IEventStore<byte array>).TryGetEvent version eventId name)
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
                                                ("snapshot",  sqlBinary snapshot);
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
                |> Sql.parameters ["dateFrom", Sql.timestamptz dateFrom; "dateTo", Sql.timestamptz dateTo]
                |> Sql.executeAsync ( fun read ->
                    (
                        read.int "id",
                        readAsBinary read "event"
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
                        readAsBinary read "snapshot"
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
                        readAsBinary read "snapshot"
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

            member this.TryGetLastAggregateEventId version name aggregateId =
                log.Debug (sprintf "TryGetLastEventId %s %s" version name)

                let query = sprintf "SELECT id FROM events%s%s where aggregate_id = '%A' ORDER BY id DESC LIMIT 1" version name aggregateId
                connection
                |> Sql.connect
                |> Sql.query query 
                |> Sql.executeAsync  (fun read ->
                        (
                            read.int "id"
                        )     
                    )
                |> Async.AwaitTask
                |> Async.RunSynchronously
                |> Seq.tryHead
            
            member this.AddAggregateEvents (eventId: EventId) (version: Version) (name: Name) (aggregateId: System.Guid) (events: List<byte array>): Result<List<int>,string> =
                log.Debug (sprintf "AddAggregateEvents %s %s %A %A" version name aggregateId events)
                let stream_name = version + name
                let command = sprintf "SELECT insert%s_aggregate_event_and_return_id(@event, @aggregate_id);" stream_name
                let conn = new NpgsqlConnection(connection)

                conn.Open()
                let transaction = conn.BeginTransaction() 
                let lastEventId = 
                    (this :> IEventStore<byte[]>).TryGetLastAggregateEventId version name aggregateId
                    |>> (fun x -> x)
                async {
                    let result =
                        if (lastEventId.IsNone && eventId = 0) || (lastEventId.IsSome && lastEventId.Value = eventId) then
                            try
                                let ids =
                                    events
                                    |>> 
                                        (
                                            fun x  -> 
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
                                    transaction.Rollback()
                                    log.Error (sprintf "an error occurred: %A" ex.Message)
                                    ex.Message |> Error
                        else
                            transaction.Rollback()
                            Error "EventId is not the last one"
                    try
                        return result
                    finally
                        conn.Close()
                }
                |> Async.RunSynchronously

            member this.MultiAddAggregateEvents (arg: List<EventId * List<byte array> * Version * Name * System.Guid>) =
                log.Debug (sprintf "MultiAddAggregateEvents %A" arg)
                let conn = new NpgsqlConnection(connection)
                conn.Open()
                let transaction = conn.BeginTransaction() 

                // refactor this only after all the examples are ready for this release
                let lastEventIds =
                    arg 
                    |>> 
                    fun ( _, _, version, name, aggregateId) -> 
                        ((this :> IEventStore<byte[]>).TryGetLastAggregateEventId version name aggregateId) |>> (fun x -> x)

                let eventIds =
                    arg 
                    |>> fun (eventId, _, _, _, _) -> eventId
                
                let check =
                    List.zip lastEventIds eventIds
                    |> List.forall (fun (lastEventId, eventId) -> lastEventId.IsNone && eventId = 0 || lastEventId.Value = eventId)

                async {
                    let result =
                        if check then
                            try
                                let cmdList = 
                                    arg 
                                    |>>
                                        fun (_, events, version,  name, aggregateId) ->
                                            let stream_name = version + name
                                            let command = new NpgsqlCommand(sprintf "SELECT insert%s_event_and_return_id(@event, @aggregate_id);" stream_name, conn)
                                            events
                                            |>> 
                                                fun event ->
                                                    command.Parameters.AddWithValue("event", event ) |> ignore
                                                    command.Parameters.AddWithValue("@aggregate_id", aggregateId ) |> ignore
                                                    let result = command.ExecuteScalar() 
                                                    result :?> int
                                    
                                transaction.Commit()    
                                cmdList |> Ok
                            with
                                | _ as ex -> 
                                    log.Error (sprintf "an error occurred: %A" ex.Message)
                                    transaction.Rollback()
                                    ex.Message |> Error
                        else
                            transaction.Rollback()
                            Error "EventId is not the last one"
                    try
                        return result
                    finally
                        conn.Close()
                }
                |> Async.RunSynchronously

            member this.GetAggregateEventsAfterId version name aggregateId id: Result<List<EventId * byte array>,string> = 
                let query = sprintf "SELECT id, event FROM events%s%s WHERE id > @id and aggregate_id = @aggregateId ORDER BY id"  version name
                try 
                    connection
                    |> Sql.connect
                    |> Sql.query query
                    |> Sql.parameters ["id", Sql.int id; "aggregateId", Sql.uuid aggregateId]
                    |> Sql.executeAsync ( fun read ->
                        (
                            read.int "id",
                            readAsBinary read "event"
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
            member this.GetAggregateEvents version name aggregateId: Result<List<EventId * byte array>, string> = 
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
                            readAsBinary read "event"
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
