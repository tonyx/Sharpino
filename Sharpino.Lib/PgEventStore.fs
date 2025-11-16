
namespace Sharpino

open Npgsql.FSharp
open Npgsql
open FSharpPlus
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions
open System.Runtime.CompilerServices
open Sharpino
open Sharpino.Storage
open Sharpino.Definitions

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

    // todo: should set the logger from outside or, better (next release), use the dependency injection infrastructure
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
                    let res2 =
                        connection
                        |> Sql.connect
                        |> Sql.query (sprintf "DELETE from snapshots%s%s" version name)
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
                let query = sprintf "SELECT event_id, id, is_deleted FROM snapshots%s%s WHERE aggregate_id = @aggregate_id ORDER BY id DESC LIMIT 1" version name
                
                let result = 
                    Async.RunSynchronously
                        (async {
                            return 
                                connection
                                |> Sql.connect
                                |> Sql.query query
                                |> Sql.parameters ["aggregate_id", Sql.uuid aggregateId ]
                                |> Sql.execute (fun read ->
                                    (
                                        read.intOrNone "event_id",
                                        read.int "id",
                                        read.bool "is_deleted"
                                    )
                                )
                                |> Seq.tryHead
                        }, evenStoreTimeout)
                match result with
                | None -> None
                | Some (eventId, id, false) -> Some (eventId, id)
                | _ -> None

            member this.TryGetLastHistorySnapshotIdByAggregateId version name aggregateId =
                logger.Value.LogDebug (sprintf "TryGetLastSnapshotIdByAggregateId %s %s %A" version name aggregateId)
                let query = sprintf "SELECT event_id, id FROM snapshots%s%s WHERE aggregate_id = @aggregate_id ORDER BY id DESC LIMIT 1" version name
                
                let result =
                    fun () ->     
                        Async.RunSynchronously
                            (async {
                                return 
                                    connection
                                    |> Sql.connect
                                    |> Sql.query query
                                    |> Sql.parameters ["aggregate_id", Sql.uuid aggregateId ]
                                    |> Sql.execute (fun read ->
                                        (
                                            read.intOrNone "event_id",
                                            read.int "id"
                                        )
                                    )
                                    |> Seq.tryHead
                            }, evenStoreTimeout)
                try              
                    result ()
                with
                | _ as ex ->
                    logger.Value.LogError (ex.Message)
                    None
            // todo: that's not good approach
            
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
                (this :> IEventStore<string>).AddEventsMd eventId version name "" events
                    
            member this.AddEventsMd eventId version name metadata events =
                logger.Value.LogDebug (sprintf "AddEventsMd %s %s %A %s" version name events metadata)
                let stream_name = version + name
                let command = sprintf "SELECT insert_md%s_event_and_return_id(@event,@md);" stream_name
                let conn = new NpgsqlConnection(connection)

                let result =
                    fun _ ->
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
                                            Error $"EventId match conrtrol failed: passed eventId {eventId}. Latest eventId: {lastEventId}"
                                    try
                                        return result
                                    finally
                                        conn.Close()
                                }, evenStoreTimeout
                            )
                try
                    result ()
                with
                | _ as ex ->
                    logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message
                    
            member this.MultiAddEvents (arg: List<EventId * List<Json> * Version * Name>) =
                (this :> IEventStore<string>).MultiAddEventsMd "" arg
                    
            member this.MultiAddEventsMd md (arg: List<EventId * List<Json> * Version * Name>) =
                logger.Value.LogDebug (sprintf "MultiAddEventsMd %A %s" arg md)
                
                let result =
                    fun _ -> 
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
                                
                                // todo: unify with checkIds to avoid redoundancy i.e. check and errors needs to be there at the same time
                                let checkIdsErrors =
                                    lastEventIdsPerContext
                                    |> List.filter (fun (eventId, lastEventId) -> lastEventId.IsNone && eventId <> 0 || (lastEventId.IsSome && lastEventId.Value <> eventId))
                                    |> List.map (fun (eventId, lastEventId) -> sprintf "EventId %d does not match lastEventId %A" eventId lastEventId)
                                    
                                let result =
                                    if checkIds then
                                        try
                                            let cmdList = 
                                                arg 
                                                |>>
                                                    // take this opportunity to evaluate if eventId could be managed by the db function to do the check
                                                    fun (eventId, events, version,  name) -> 
                                                        let stream_name = version + name

                                                        events
                                                        |>> 
                                                            fun event ->
                                                                let command = new NpgsqlCommand(sprintf "SELECT insert_md%s_event_and_return_id(@event, @md);" stream_name, conn)
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
                                        Error ("EventId is not the last one " + (checkIdsErrors |> String.concat ", "))
                                try
                                    return result
                                finally
                                    conn.Close()
                            }, evenStoreTimeout)
                try             
                    result ()
                with    
                | _  as ex ->
                    logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message 
                
            member this.GetEventsAfterId version id name =
                logger.Value.LogDebug (sprintf "GetEventsAfterId %s %s %d" version name id)
                let query = sprintf "SELECT id, event FROM events%s%s WHERE id > @id ORDER BY id"  version name
               
                let result =
                    fun _ ->
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
                try
                    result ()
                with
                | _ as ex ->
                    logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message

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
               
                let result =
                    fun _ ->
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
                try
                    result ()
                with
                | _ as ex ->
                    logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message
           
            member this.SetInitialAggregateStates version name idsAndSnapshots =
                logger.Value.LogDebug (sprintf "SetInitialAggregateStates %s %s"  version name)
                let insertSnapshot = sprintf "INSERT INTO snapshots%s%s (aggregate_id, snapshot, timestamp) VALUES (@aggregate_id, @snapshot, @timestamp)" version name
                let firstEmptyAggregateEvent = sprintf "INSERT INTO aggregate_events%s%s (aggregate_id) VALUES (@aggregate_id)" version name
              
                try  
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
                                                        for (aggregateId, json) in idsAndSnapshots do
                                                        [
                                                            ("@aggregate_id", Sql.uuid aggregateId);
                                                            ("snapshot",  sqlJson json);
                                                            ("timestamp", Sql.timestamptz System.DateTime.UtcNow)
                                                        ]
                                                    ]
                                                firstEmptyAggregateEvent,
                                                    [
                                                        for (aggregateId, _) in idsAndSnapshots do
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
                with
                | _ as ex ->
                    logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message
                 
            
            member this.SetInitialAggregateStateAndAddEvents eventId aggregateId aggregateVersion aggregatename json contextVersion contextName events =
                (this:> IEventStore<string>).SetInitialAggregateStateAndAddEventsMd eventId aggregateId aggregateVersion aggregatename json contextVersion contextName "" events

            member this.SetInitialAggregateStateAndAddEventsMd eventId aggregateId aggregateVersion aggregatename initInstance contextVersion contextName md events =
                logger.Value.LogDebug "entered in setInitialAggregateStateAndAddEvents"
                let insertSnapshot = sprintf "INSERT INTO snapshots%s%s (aggregate_id,  snapshot, timestamp) VALUES (@aggregate_id,  @snapshot, @timestamp)" aggregateVersion aggregatename
                let insertFirstEmptyAggregateEvent = sprintf "INSERT INTO aggregate_events%s%s (aggregate_id) VALUES (@aggregate_id)" aggregateVersion aggregatename
                let insertEvents = sprintf "SELECT insert_md%s_event_and_return_id(@event, @md);" (contextVersion + contextName)
                
                let result =
                    fun _ ->
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
                                        Error $"EventId check failed. EventId passed {eventId}. Latest eventId: {lastEventId}"
                                try
                                    return result
                                finally
                                    conn.Close()
                            }, evenStoreTimeout)
                    
                try
                    result ()
                with
                | _ as ex ->
                    logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message
             
            member this.SetInitialAggregateStateAndAddAggregateEvents eventId aggregateId aggregateVersion aggregatename secondAggregateId json contextVersion contextName events =
                (this :> IEventStore<string>).SetInitialAggregateStateAndAddAggregateEventsMd eventId aggregateId aggregateVersion aggregatename secondAggregateId json contextVersion contextName "" events
              
            member this.SetInitialAggregateStateAndAddAggregateEventsMd eventId aggregateId aggregateVersion aggregatename secondAggregateId json contextVersion contextName md events =
                logger.Value.LogDebug "entered in SetInitialAggregateStateAndAddAggregateEvents"

                let insertSnapshot = sprintf "INSERT INTO snapshots%s%s (aggregate_id,  snapshot, timestamp) VALUES (@aggregate_id,  @snapshot, @timestamp)" aggregateVersion aggregatename
                let insertFirstEmptyAggregateEvent = sprintf "INSERT INTO aggregate_events%s%s (aggregate_id) VALUES (@aggregate_id)" aggregateVersion aggregatename

                let insertEvents = sprintf "SELECT insert_md%s_aggregate_event_and_return_id(@event, @aggregate_id, @md);" (contextVersion + contextName)
               
                let result =
                    fun _ ->
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
                                        Error $"EventId check failed. eventId passed {eventId}. Latest eventId: {lastEventId}"
                                try
                                    return result
                                finally
                                    conn.Close()
                            }, evenStoreTimeout)
                try
                    result ()
                with
                | _ as ex ->
                    logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message
                    
            member this.SetInitialAggregateStateAndMultiAddAggregateEvents aggregateId version name jsonSnapshot events =
                (this :> IEventStore<string>).SetInitialAggregateStateAndMultiAddAggregateEventsMd aggregateId version name jsonSnapshot "" events
                            
            member this.SetInitialAggregateStateAndMultiAddAggregateEventsMd  aggregateId version name jsonSnapshot md events =
                logger.Value.LogDebug "entered in SetInitialAggregateStateAndMultiAddAggregateEvents"
                let insertSnapshot = sprintf "INSERT INTO snapshots%s%s (aggregate_id,  snapshot, timestamp) VALUES (@aggregate_id,  @snapshot, @timestamp)" version name
                let insertFirstEmptyAggregateEvent = sprintf "INSERT INTO aggregate_events%s%s (aggregate_id) VALUES (@aggregate_id)" version name
               
                let result =
                    fun _ ->
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
                            |> List.forall (fun (lastEventId, eventId) -> lastEventId.IsNone && eventId = 0 || (lastEventId.IsSome && lastEventId.Value = eventId))
                        
                        let errors =
                            List.zip lastEventIds eventIds
                            |> List.filter (fun (lastEventId, eventId) -> lastEventId.IsNone && eventId <> 0 || (lastEventId.IsSome && lastEventId.Value <> eventId))
                            |> List.map (fun (lastEventId, eventId) -> sprintf "EventId check failed. eventId passed %d. Latest eventId: %A" eventId lastEventId)
                            
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
                                                            events
                                                            |>> 
                                                                (
                                                                    fun x ->
                                                                        let command' = new NpgsqlCommand(sprintf "SELECT insert_md%s_aggregate_event_and_return_id(@event, @aggregate_id, @md);" stream_name, conn)
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
                                        Error ("EventId is not the last one" + (errors |> String.concat ", "))
                                try 
                                    return result
                                finally
                                    conn.Close()
                            }, evenStoreTimeout)
                try
                    result ()
                with
                | _ as ex ->
                    logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message
                    
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
                    logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
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
                (this :> IEventStore<string>).AddAggregateEventsMd eventId version name aggregateId "" events
                    
            member this.AddAggregateEventsMd (eventId: EventId) (version: Version) (name: Name) (aggregateId: System.Guid) (md: Metadata) (events: List<Json>): Result<List<int>,string> =
                logger.Value.LogDebug (sprintf "AddAggregateEventsMd %s %s %A %A %s" version name aggregateId events md)
                let stream_name = version + name
                let command = sprintf "SELECT insert_md%s_aggregate_event_and_return_id(@event, @aggregate_id, @md);" stream_name
                let result =
                    fun _ ->
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
                                        Error $"in {stream_name} the EventId passed {eventId} does not match to the latest one {lastEventId} is not the last one"
                                try
                                    return result
                                finally
                                    conn.Close()
                            }, evenStoreTimeout)
                try 
                    result ()
                with
                | _ as ex ->
                    logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message

            member this.MultiAddAggregateEvents (arg: List<EventId * List<Json> * Version * Name * AggregateId>) =
                (this :> IEventStore<string>).MultiAddAggregateEventsMd "" arg
                    
            member this.MultiAddAggregateEventsMd md (arg: List<EventId * List<Json> * Version * Name *  AggregateId>) =
                logger.Value.LogDebug (sprintf "MultiAddAggregateEventsMd %A %s" arg md)
                
                let result =
                    fun _ ->
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
                        
                        let errors =
                            List.zip lastEventIds eventIds
                            |> List.filter (fun (lastEventId, eventId) -> lastEventId.IsNone && eventId <> 0 || (lastEventId.IsSome && lastEventId.Value <> eventId))
                            |> List.map (fun (lastEventId, eventId) -> sprintf "EventId check failed. eventId passed %d. Latest eventId: %A" eventId lastEventId)

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
                                        Error ("eventids check failed " + (errors |> String.concat ", "))
                                try
                                    return result
                                finally
                                    conn.Close()
                            }, evenStoreTimeout)
                try
                    result ()
                with
                | _  as ex ->
                    logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message

            member this.GetAggregateEventsAfterId version name aggregateId id: Result<List<EventId * Json>,string> =
                logger.Value.LogDebug (sprintf "GetAggregateEventsAfterId %s %s %A %d" version name aggregateId id)
                let query = sprintf "SELECT id, event FROM events%s%s WHERE id > @id and aggregate_id = @aggregateId ORDER BY id"  version name
                
                let result =
                    fun _ ->
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
                try
                    result ()
                with
                | _ as ex ->
                    logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message
                        
            member this.GetAggregateEvents version name aggregateId: Result<List<EventId * Json>,string> =
                logger.Value.LogDebug (sprintf "GetAggregateEvents %s %s %A" version name aggregateId)
                let query = sprintf "SELECT id, event FROM events%s%s WHERE aggregate_id = @aggregateId ORDER BY id"  version name
                let result =
                    fun _ ->
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
                try             
                    result ()
                with
                | _ as ex ->
                    logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message
            
            member this.GDPRReplaceSnapshotsAndEventsOfAnAggregate version name aggregateId snapshot event =
                logger.Value.LogDebug (sprintf "GDPRReplaceSnapshotsAndEventsOfAnAggregate %s %s %A %A %A" version name aggregateId snapshot event)
                
                let sqlReplaceAllAggregates = (sprintf "UPDATE snapshots%s%s SET snapshot = @snapshot WHERE aggregate_id = @aggregateId" version name)
                let sqlReplaceAllEvents = (sprintf "UPDATE events%s%s SET event = @event WHERE aggregate_id = @aggregateId" version name)
                
                let result =
                    fun _ ->
                        let conn = new NpgsqlConnection(connection)
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
                try
                    result ()
                with
                | _ as ex ->
                    logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message

            member this.GetAggregateIdsInATimeInterval version name dateFrom dateTo = 
                logger.Value.LogDebug (sprintf "GetAggregateIdsInATimeInterval %A %A %A %A" version name dateFrom dateTo)
                let query = sprintf "SELECT DISTINCT  aggregate_id FROM snapshots%s%s where timestamp >= @dateFrom AND timestamp <= @dateTo" version name
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
                                        read.uuid "aggregate_id"
                                    )
                                )
                                |> Seq.toList
                            }, evenStoreTimeout)
                    |> Ok
                with
                | _ as ex ->
                    logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message 

            member this.GetAggregateIds version name = 
                logger.Value.LogDebug (sprintf "GetAggregateIds %A %A" version name)
                let query = sprintf "SELECT DISTINCT  aggregate_id FROM snapshots%s%s" version name
                try
                    Async.RunSynchronously
                        (async {
                            return
                                connection
                                |> Sql.connect
                                |> Sql.query query
                                |> Sql.execute ( fun read ->
                                    (
                                        read.uuid "aggregate_id"
                                    )
                                )
                                |> Seq.toList
                            }, evenStoreTimeout)
                    |> Ok
                with
                | _ as ex ->
                    logger.Value.LogDebug (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message 
          
            member this.SnapshotAndMarkDeleted version name eventId aggregateId snapshot =
                logger.Value.LogDebug (sprintf "SnapshotAndMarkDeleted %s %s %A" version name aggregateId)
                let command = sprintf "INSERT INTO snapshots%s%s (aggregate_id, snapshot, timestamp, is_deleted) VALUES (@aggregate_id, @snapshot, @timestamp, true)" version name
                let lastEventId = (this :> IEventStore<string>).TryGetLastAggregateEventId version name aggregateId
               
                // still complicated, but better than before
                if (lastEventId.IsNone && eventId = 0) || (lastEventId.IsSome && lastEventId.Value = eventId) then
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
                                                        ("aggregate_id", Sql.uuid aggregateId)
                                                        ("snapshot",  sqlJson snapshot)
                                                        ("timestamp", Sql.timestamp System.DateTime.Now)
                                                        ("is_deleted", Sql.bool true)
                                                    ]
                                                ]
                                        ]
                            }, evenStoreTimeout)
                            |> ignore
                            |> Ok
                    with
                    | _ as ex ->
                        logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                        Error ex.Message
                else
                    Error $"error checking event alignments. eventId passed {eventId}. Latest event id: {lastEventId}"

            member this.SnapshotMarkDeletedAndAddAggregateEventsMd
                s1Version
                s1name
                s1EventId
                s1AggregateId
                s1Snapshot
                streamEventId
                streamAggregateVersion
                streamAggregateName
                streamAggregateId
                metaData
                events =
                    logger.Value.LogDebug (sprintf "SnapshotAndMarkDeleted %s %s %A" s1Version s1name s1AggregateId)
                    let snapCommand = sprintf "INSERT INTO snapshots%s%s (aggregate_id, snapshot, timestamp, is_deleted) VALUES (@aggregate_id, @snapshot, @timestamp, true)" s1Version s1name
                    let lastEventId = (this :> IEventStore<string>).TryGetLastAggregateEventId s1Version s1name s1AggregateId
                    
                    let lastStreamEventId =
                        (this :> IEventStore<string>).TryGetLastAggregateEventId streamAggregateVersion streamAggregateName streamAggregateId
                    
                    let stream_name = streamAggregateVersion + streamAggregateName
                    let command = sprintf "SELECT insert_md%s_aggregate_event_and_return_id(@event, @aggregate_id, @md);" stream_name
                    
                    if
                        ((lastEventId.IsNone && s1EventId = 0) || (lastEventId.IsSome && lastEventId.Value = s1EventId)) &&
                        ((lastStreamEventId.IsNone && streamEventId = 0) || (lastStreamEventId.IsSome && lastStreamEventId.Value = streamEventId)) then
                        try
                            Async.RunSynchronously (
                                async {
                                    let conn = new NpgsqlConnection(connection)
                                    conn.Open()
                                    let transaction = conn.BeginTransaction()
                                    try
                                        let ids =
                                            events 
                                            |>> 
                                                (
                                                    fun x ->
                                                        let command' = new NpgsqlCommand(command, conn)
                                                        command'.Parameters.AddWithValue("event", x ) |> ignore
                                                        command'.Parameters.AddWithValue("@aggregate_id", streamAggregateId ) |> ignore
                                                        command'.Parameters.AddWithValue("md", metaData ) |> ignore
                                                        let result = command'.ExecuteScalar() 
                                                        result :?> int
                                                )
                                        
                                        let _ =
                                            connection
                                            |> Sql.connect
                                            |> Sql.executeTransaction
                                                [
                                                    snapCommand,
                                                        [
                                                            [
                                                                ("aggregate_id", Sql.uuid s1AggregateId)
                                                                ("snapshot",  sqlJson s1Snapshot)
                                                                ("timestamp", Sql.timestamp System.DateTime.Now)
                                                                ("is_deleted", Sql.bool true)
                                                            ]
                                                        ]
                                                ]
                                        transaction.Commit()
                                        return (ids |> Ok)
                                        finally
                                            transaction.Dispose()
                                            conn.Dispose()
                            }, evenStoreTimeout)
                        with
                        | _ as ex ->
                            logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                            Error ex.Message
                    else
                        Error $"error checking event alignments. eventId passed {s1EventId}. Latest event id: {lastEventId}. LastStreamEventId: {lastStreamEventId}. StreamEventid: {streamEventId}"
                    
            member this.SnapshotMarkDeletedAndMultiAddAggregateEventsMd
                md
                s1Version
                s1name
                s1EventId
                s1AggregateId
                s1Snapshot
                (arg: List<EventId * List<Json> * Version * Name * AggregateId>) =
                    let snapCommand = sprintf "INSERT INTO snapshots%s%s (aggregate_id, snapshot, timestamp, is_deleted) VALUES (@aggregate_id, @snapshot, @timestamp, true)" s1Version s1name
                    let lastEventId = (this :> IEventStore<string>).TryGetLastAggregateEventId s1Version s1name s1AggregateId
                    
                    let lastEventIds =
                        arg 
                        |>> 
                        fun (_, _, version, name, aggregateId) -> 
                            ((this :> IEventStore<string>).TryGetLastAggregateEventId version name aggregateId)
                            
                    let eventIds = 
                        arg
                        |>> fun (eventId, _, _, _, _) -> eventId
                        
                    let eventIdsMatch = 
                        List.zip lastEventIds eventIds
                        |> List.forall (fun (lastEventId, eventId) -> lastEventId.IsNone && eventId = 0 || lastEventId.Value = eventId)
                    
                    let errors =
                        List.zip lastEventIds eventIds
                        |> List.filter (fun (lastEventId, eventId) -> lastEventId.IsNone && eventId <> 0 || (lastEventId.IsSome && lastEventId.Value <> eventId))
                        |> List.map (fun (lastEventId, eventId) -> sprintf "EventId check failed. eventId passed %d. Latest eventId: %A" eventId lastEventId)
                        
                    if ((lastEventId.IsNone && s1EventId = 0) || (lastEventId.IsSome && lastEventId.Value = s1EventId)) && eventIdsMatch then
                        
                        let conn = new NpgsqlConnection(connection)
                        conn.Open()
                        let transaction = conn.BeginTransaction()
                        
                        Async.RunSynchronously
                            (async {
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
                                    
                                    let _ =
                                        connection
                                        |> Sql.connect
                                        |> Sql.executeTransaction
                                            [
                                                snapCommand,
                                                    [
                                                        [
                                                            ("aggregate_id", Sql.uuid s1AggregateId)
                                                            ("snapshot",  sqlJson s1Snapshot)
                                                            ("timestamp", Sql.timestamp System.DateTime.Now)
                                                            ("is_deleted", Sql.bool true)
                                                        ]
                                                    ]
                                            ]  
                                    
                                    transaction.Commit()
                                    conn.Close()
                                    return (cmdList |> Ok)
                                with
                                    | _ as ex ->
                                        logger.Value.LogError (sprintf "an error occurred: %A" ex.Message)
                                        transaction.Rollback()
                                        conn.Close()
                                        return (ex.Message |> Error)
                        }, evenStoreTimeout) 
                    else Error ("optimistic lock failure: " + (errors |> String.concat ", ") + $"lastEventId: {lastEventId}, eventId: {s1EventId}")
                        
                