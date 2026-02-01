namespace Sharpino

open System
open System.Threading
open System.Threading.Tasks
open FsToolkit.ErrorHandling
open Npgsql.FSharp
open Npgsql
open FSharpPlus
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Hosting
open FSharpPlus.Operators
open Sharpino
open Sharpino.Storage
open Sharpino.PgStorage
open Sharpino.Definitions

module PgBinaryStore =
    let builder = Host.CreateApplicationBuilder()
    let config = builder.Configuration
    let cancellationTokenSourceExpiration = config.GetValue<int>("CancellationTokenSourceExpiration", 100000)
    let isTestEnv = config.GetValue<bool>("IsTestEnv", false)
    
    // todo: the logger needs to be compliant with appsettings as well
    let loggerFactory = LoggerFactory.Create(fun b ->
        if config.GetValue<bool>("Logging:Console", true) then
            b.AddConsole() |> ignore
        )
    let logger = loggerFactory.CreateLogger("Sharpino.CommandHandler")
    
    let setLogger (newLogger: ILogger) =
        failwith "setLogger is deprecated. Use config"
        
    type PgBinaryStore (connection: string, readAsBinary: RowReader -> (string -> byte[])) =

        new (connection: string) = PgBinaryStore (connection, readAsBinary)

        member this.Reset version name = 
            if isTestEnv then
                try
                    let res1 =
                        Async.RunSynchronously
                            (connection
                            |> Sql.connect
                            |> Sql.query (sprintf "DELETE from snapshots%s%s" version name)
                            |> Sql.executeNonQueryAsync
                            |> Async.AwaitTask
                            ,
                            eventStoreTimeout)
                    let res2 =
                        Async.RunSynchronously
                        (connection
                        |> Sql.connect
                        |> Sql.query (sprintf "DELETE from events%s%s" version name)
                        |> Sql.executeNonQueryAsync
                        |> Async.AwaitTask
                        ,
                        eventStoreTimeout)
                    ()
                with 
                    | _ as e -> failwith (e.ToString())
            else
                failwith "operation allowed only in test db"
        member this.ResetAggregateStream version name =
            if isTestEnv then
                try
                    
                    Async.RunSynchronously
                        (connection
                        |> Sql.connect
                        |> Sql.query (sprintf "DELETE from aggregate_events%s%s" version name)
                        |> Sql.executeNonQueryAsync
                        |> Async.AwaitTask
                        ,
                        eventStoreTimeout)
                        |> ignore
                    
                    Async.RunSynchronously
                        (connection
                        |> Sql.connect
                        |> Sql.query (sprintf "DELETE from snapshots%s%s" version name)
                        |> Sql.executeNonQueryAsync
                        |> Async.AwaitTask
                        ,
                        eventStoreTimeout)
                        |> ignore    
                    
                    ()    
                with
                    | _ as e -> failwith (e.ToString())
            else
                failwith "operation allowed only in test db"
        
        interface IEventStore<byte []> with
            member this.GetEventsInATimeIntervalAsync(version: Version, name: Name, dateFrom: DateTime, dateTo: DateTime, ?ct: CancellationToken) =
                logger.LogDebug (sprintf "GetEventsInATimeIntervalAsync %s %s %A %A" version name dateFrom dateTo)
                let query = sprintf "SELECT id, event FROM events%s%s WHERE timestamp >= @dateFrom AND timestamp <= @dateTo ORDER BY id"  version name
                task {
                    try
                        use cts = CancellationTokenSource.CreateLinkedTokenSource
                                      (defaultArg ct (new CancellationTokenSource(eventStoreTimeout)).Token)
                        cts.CancelAfter(cancellationTokenSourceExpiration)
                        use conn = new NpgsqlConnection(connection)
                        do! conn.OpenAsync(cts.Token).ConfigureAwait(false)
                        use command = new NpgsqlCommand(query, conn)
                        command.CommandTimeout <- max 1 (eventStoreTimeout / 1000)
                        command.Parameters.AddWithValue("dateFrom", dateFrom) |> ignore
                        command.Parameters.AddWithValue("dateTo", dateTo) |> ignore
                        use! reader = command.ExecuteReaderAsync(cts.Token).ConfigureAwait(false)
                        let results = ResizeArray<_>()
                        let rec loop () = task {
                            let! hasRow = reader.ReadAsync(cts.Token).ConfigureAwait(false)
                            if hasRow then
                                let eventId = reader.GetInt32(0)
                                let eventBytes = reader.GetFieldValue<byte[]>(1)
                                results.Add(eventId, eventBytes)
                                return! loop ()
                            else
                                return ()
                        }
                        do! loop ()
                        return results |> Seq.toList |> Ok
                    with ex ->
                        logger.LogError (sprintf "an error occurred: %A" ex.Message)
                        return Error ex.Message
                }
                
            member this.GetAggregateEventsInATimeIntervalAsync(version: Version, name: Name, aggregateId: AggregateId, dateFrom: DateTime, dateTo: DateTime, ?ct: CancellationToken) =
                logger.LogDebug (sprintf "GetEventsInATimeInterval %s %s %A %A %A" version name aggregateId dateFrom dateTo)
                let query = sprintf "SELECT id, event FROM events%s%s WHERE aggregate_id = @aggregateId and timestamp >= @dateFrom and timestamp <= @dateTo ORDER BY id"  version name
                task
                    {
                        try
                            use cts = CancellationTokenSource.CreateLinkedTokenSource
                                          (defaultArg ct (new CancellationTokenSource(eventStoreTimeout)).Token)
                            cts.CancelAfter(cancellationTokenSourceExpiration)
                            use conn = new NpgsqlConnection(connection)
                            do! conn.OpenAsync(cts.Token).ConfigureAwait(false)
                            use command = new NpgsqlCommand(query, conn)
                            command.CommandTimeout <- max 1 (eventStoreTimeout / 1000)
                            command.Parameters.AddWithValue("aggregateId", aggregateId) |> ignore
                            command.Parameters.AddWithValue("dateFrom", dateFrom) |> ignore
                            command.Parameters.AddWithValue("dateTo", dateTo) |> ignore
                            use! reader = command.ExecuteReaderAsync(cts.Token).ConfigureAwait(false)
                            let results = ResizeArray<_>()
                            let rec loop () = task {
                                let! hasRow = reader.ReadAsync(cts.Token).ConfigureAwait(false)
                                if hasRow then
                                    let eventId = reader.GetInt32(0)
                                    let event = reader.GetFieldValue<byte[]>(1)
                                    results.Add(eventId, event)
                                    return! loop ()
                                else
                                    return ()
                            }
                            do! loop ()
                            return results |> Seq.toList |> Ok
                        with ex ->
                            logger.LogError (sprintf "an error occurred: %A" ex.Message)
                            return Error ex.Message
                    }
            member this.GetAggregateEventsAfterIdAsync(version: Version, name: Name, aggregateId: AggregateId, id: EventId, ?ct: CancellationToken) =
                logger.LogDebug (sprintf "GetAggregateEventsAfterId %s %s %A %d" version name aggregateId id)
                let query = sprintf "SELECT id, event FROM events%s%s WHERE id > @id and aggregate_id = @aggregateId ORDER BY id"  version name
                task
                    {
                       try
                           use cts = CancellationTokenSource.CreateLinkedTokenSource
                                          (defaultArg ct (new CancellationTokenSource(eventStoreTimeout)).Token)
                           cts.CancelAfter(cancellationTokenSourceExpiration)
                           use conn = new NpgsqlConnection(connection)
                           do! conn.OpenAsync(cts.Token).ConfigureAwait(false)
                           use command = new NpgsqlCommand(query, conn)
                           command.CommandTimeout <- max 1 (eventStoreTimeout / 1000)
                           command.Parameters.AddWithValue("id", id) |> ignore
                           command.Parameters.AddWithValue("aggregateId", aggregateId) |> ignore
                           use! reader = command.ExecuteReaderAsync(cts.Token).ConfigureAwait(false)
                           let results = ResizeArray<_>()
                           let rec loop () = task {
                               let! hasRow = reader.ReadAsync(cts.Token).ConfigureAwait(false)
                               if hasRow then
                                   let eventId = reader.GetInt32(0)
                                   let eventJson = reader.GetFieldValue<byte[]>(1)
                                   results.Add(eventId, eventJson)
                                   return! loop ()
                               else
                                   return ()
                           }
                           do! loop ()
                           return results |> Seq.toList |> Ok
                       with ex ->
                           logger.LogError (sprintf "an error occurred: %A" ex.Message)
                           return Error ex.Message
                    }
            member this.SnapshotAndMarkDeletedAsync (version: Version, name: Name, eventId: EventId, aggregateId: System.Guid, napshot: byte[], ?ct: CancellationToken) =
                logger.LogDebug (sprintf "SnapshotAndMarkDeletedAsync %s %s %A" version name aggregateId)
                task {
                    use cts = CancellationTokenSource.CreateLinkedTokenSource
                                  (defaultArg ct (new CancellationTokenSource(eventStoreTimeout)).Token)
                    cts.CancelAfter(cancellationTokenSourceExpiration)
                    let command = sprintf "INSERT INTO snapshots%s%s (aggregate_id, snapshot, timestamp, is_deleted) VALUES (@aggregate_id, @snapshot, @timestamp, true)" version name
                    let lastEventId = (this :> IEventStore<byte[]>).TryGetLastAggregateEventId version name aggregateId
                    use conn = new NpgsqlConnection(connection)
                    do! conn.OpenAsync(cts.Token).ConfigureAwait(false)
                    if (lastEventId.IsNone && eventId = 0) || (lastEventId.IsSome && lastEventId.Value = eventId) then
                        try
                            use command' = new NpgsqlCommand(command, conn)
                            command'.CommandTimeout <- max 1 (eventStoreTimeout / 1000)
                            command'.Parameters.AddWithValue("aggregate_id", aggregateId) |> ignore
                            command'.Parameters.AddWithValue("snapshot", napshot) |> ignore
                            command'.Parameters.AddWithValue("timestamp", System.DateTime.Now) |> ignore
                            command'.Parameters.AddWithValue("is_deleted", true) |> ignore
                            let! result = command'.ExecuteScalarAsync(cts.Token).ConfigureAwait(false)
                            return Ok ()
                        with
                            | _ as e ->
                               return Error e.Message
                    else
                        return Error $"error checking event alignments. eventId passed: {eventId}. Latest event id: {lastEventId}"
                }
                
            member this.Reset(version: Version) (name: Name): unit =
                this.Reset version name
            member this.ResetAggregateStream(version: Version) (name: Name): unit =
                this.ResetAggregateStream version name    
            member this.AddAggregateEventsMdAsync (eventId: EventId, version: Version, name: Name, aggregateId: System.Guid, md: Metadata, events: List<byte[]>, ?ct: CancellationToken) =
                logger.LogDebug (sprintf "AddAggregateEventsMdAsync %s %s %A %A %s" version name aggregateId events md)
                let stream_name = version + name
                let commandText = sprintf "SELECT insert_md%s_aggregate_event_and_return_id(@event, @aggregate_id, @md);" stream_name
                task {
                    use conn = new NpgsqlConnection(connection)
                    use cts = CancellationTokenSource.CreateLinkedTokenSource
                                  (defaultArg ct (new CancellationTokenSource(eventStoreTimeout)).Token)
                    cts.CancelAfter(cancellationTokenSourceExpiration)
                    try
                        do! conn.OpenAsync(cts.Token).ConfigureAwait(false)
                        use transaction = conn.BeginTransaction()
                        let lastEventId = (this :> IEventStore<byte[]>).TryGetLastAggregateEventId version name aggregateId
                        if (lastEventId.IsNone && eventId = 0) || (lastEventId.IsSome && lastEventId.Value = eventId) then
                            try
                                let ids = ResizeArray<int>()
                                for x in events do
                                    use command' = new NpgsqlCommand(commandText, conn, transaction)
                                    command'.CommandTimeout <- max 1 (eventStoreTimeout / 1000)
                                    command'.Parameters.AddWithValue("event", x) |> ignore
                                    command'.Parameters.AddWithValue("@aggregate_id", aggregateId) |> ignore
                                    command'.Parameters.AddWithValue("md", md) |> ignore
                                    let! scalar = command'.ExecuteScalarAsync(cts.Token).ConfigureAwait(false)
                                    ids.Add(unbox<int> scalar)
                                do! transaction.CommitAsync(cts.Token).ConfigureAwait(false)
                                return Ok (List.ofSeq ids)
                            with ex ->
                                logger.LogError (sprintf "an error occurred: %A" ex.Message)
                                do! transaction.RollbackAsync(cts.Token).ConfigureAwait(false)
                                return Error ex.Message
                        else
                            do! transaction.RollbackAsync(cts.Token).ConfigureAwait(false)
                            return Error "EventId is not the last one"
                    with ex ->
                        logger.LogError (sprintf "an error occurred: %A" ex.Message)
                        return Error ex.Message
                }
                
            member this.MultiAddAggregateEventsMdAsync (arg: List<EventId * List<byte[]> * Version * Name * AggregateId>, md: Metadata, ?ct: CancellationToken) =
                logger.LogDebug (sprintf "MultiAddAggregateEventsMd %A" arg )
                task {
                    use conn = new NpgsqlConnection(connection)
                    use cts = CancellationTokenSource.CreateLinkedTokenSource
                                  (defaultArg ct (new CancellationTokenSource(eventStoreTimeout)).Token)
                    cts.CancelAfter(cancellationTokenSourceExpiration)
                    try
                        do! conn.OpenAsync(cts.Token).ConfigureAwait(false)
                        let transaction = conn.BeginTransaction() 
                        let lastEventIds =
                            arg 
                            |>> 
                            fun (_, _, version, name, aggregateId) -> 
                                ((this :> IEventStore<byte[]>).TryGetLastAggregateEventId version name aggregateId)

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

                        let result =
                            if checks then
                                try
                                    let idLists = ResizeArray<ResizeArray<int>>()
                                    let _ = 
                                        arg 
                                        |>>
                                            fun (_, events, version,  name, aggregateId) ->
                                                let stream_name = version + name
                                                let ids = ResizeArray<int>()
                                                for event in events do
                                                    let command = new NpgsqlCommand(sprintf "SELECT insert_md%s_aggregate_event_and_return_id(@event, @aggregate_id, @md);" stream_name, conn)
                                                    (
                                                        command.CommandTimeout <- max 1 (eventStoreTimeout / 1000)
                                                        command.Parameters.AddWithValue("event", event ) |> ignore
                                                        command.Parameters.AddWithValue("@aggregate_id", aggregateId ) |> ignore
                                                        command.Parameters.AddWithValue("md", md ) |> ignore
                                                        let scalar = command.ExecuteScalar()
                                                        ids.Add(unbox<int> scalar)
                                                    )
                                                idLists.Add(ids)
                                    transaction.Commit()
                                    let result =
                                        idLists
                                        |>> List.ofSeq
                                    result
                                    |> List.ofSeq |> Ok
                                with
                                    | _ as ex ->
                                        logger.LogError (sprintf "an error occurred: %A" ex.Message)
                                        transaction.Rollback()
                                        ex.Message |> Error
                            else
                                transaction.Rollback()
                                Error ("eventids check failed " + (errors |> String.concat ", "))
                        try
                            return result
                        finally
                            conn.Close()
                    with
                    | _  as ex ->
                        logger.LogError (sprintf "an error occurred: %A" ex.Message)
                        return (Error ex.Message)
                }
            member this.TryGetLastSnapshot version name =
                logger.LogDebug("TryGetLastSnapshot")
                let query = sprintf "SELECT id, event_id, snapshot FROM snapshots%s%s ORDER BY id DESC LIMIT 1" version name
                try
                    Async.RunSynchronously 
                        (async {
                            return
                                connection
                                |> Sql.connect
                                |> Sql.query query
                                |> Sql.execute (fun read ->
                                    (
                                        read.int "id",
                                        read.int "event_id",
                                        readAsBinary read "snapshot"
                                    )
                                )
                                |> Seq.tryHead
                        }, eventStoreTimeout)
                with
                | _ as ex ->
                    logger.LogInformation (sprintf "an error occurred in retrieving snapshot: %A" ex.Message)
                    None

            member this.TryGetLastEventId version name =
                logger.LogDebug(sprintf "TryGetLastEventId %s %s" version name)
                let query = sprintf "SELECT id FROM events%s%s ORDER BY id DESC LIMIT 1" version name
                Async.RunSynchronously
                    (async {
                        return 
                            connection
                            |> Sql.connect
                            |> Sql.query query 
                            |> Sql.execute  (fun read -> read.int "id")
                            |> Seq.tryHead
                        }, eventStoreTimeout)

            member this.TryGetLastSnapshotEventId version name =
                logger.LogDebug (sprintf "TryGetLastSnapshotEventId %s %s" version name)
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
                        }, eventStoreTimeout)
                with
                | _ as ex ->
                    logger.LogError (sprintf "TryGetLastSnapshotEventId: an error occurred: %A" ex.Message)
                    None

            member this.TryGetLastSnapshotIdByAggregateId version name aggregateId =
                logger.LogDebug (sprintf "TryGetLastSnapshotIdByAggregateId %s %s %A" version name aggregateId)
                let query = sprintf "SELECT event_id, id, is_deleted FROM snapshots%s%s WHERE aggregate_id = @aggregate_id ORDER BY id DESC LIMIT 1" version name
                let result =
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
                                        read.int "id",
                                        read.bool "is_deleted"
                                    )
                                )
                                |> Seq.tryHead
                        }, eventStoreTimeout)
                match result with        
                | None -> None
                | Some (eventId, id, false) -> Some (eventId, id)
                | _ -> None
                
            member this.TryGetLastHistorySnapshotIdByAggregateId version name aggregateId =
                logger.LogDebug (sprintf "TryGetLastSnapshotIdByAggregateId %s %s %A" version name aggregateId)
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
                            }, eventStoreTimeout)
                try              
                    result ()
                with
                | _ as ex ->
                    logger.LogError (ex.Message)
                    None
            // todo: that's not good approach. revise possibly using result
            
            member this.TryGetEvent version id name =
                logger.LogDebug (sprintf "TryGetEvent %s %s" version name)
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
                                            let result: StoragePgEvent<byte[]> = 
                                                {
                                                    Id = read.int "id"
                                                    JsonEvent = readAsBinary read "event"
                                                    Timestamp = read.dateTime "timestamp"
                                                }
                                            result
                                    )
                                    |> Seq.tryHead
                        }, eventStoreTimeout)
                with
                | _ as ex ->
                    logger.LogError (sprintf "an error occurred: %A" ex.Message)
                    None

            member this.AddEventsMd eventId version name metadata events  =
                logger.LogDebug (sprintf "AddEventsMd %s %s %A %s" version name events metadata)
                let stream_name = version + name
                let command = sprintf "SELECT insert_md%s_event_and_return_id(@event, @md);" stream_name
                
                let result =
                    fun _ ->
                        let conn = new NpgsqlConnection(connection)
                        conn.Open()
                        let transaction = conn.BeginTransaction() 
                        Async.RunSynchronously
                            (
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
                                                            command'.Parameters.AddWithValue("md", metadata ) |> ignore
                                                            let result = command'.ExecuteScalar() 
                                                            result :?> int
                                                transaction.Commit()
                                                ids |> Ok
                                            with
                                                | _ as ex -> 
                                                    transaction.Rollback()
                                                    logger.LogError (sprintf "an error occurred: %A" ex.Message)
                                                    ex.Message |> Error
                                        else
                                            transaction.Rollback()
                                            Error $"EventId match conrtrol failed: passed eventId {eventId}. Latest eventId: {lastEventId}"
                                    try
                                        return result
                                    finally
                                        conn.Close()
                                }, eventStoreTimeout
                            )
                try
                    result ()
                with
                | _ as ex ->
                    logger.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message
                
            member this.MultiAddEventsMd md (arg: List<EventId * List<byte[]> * Version * Name>) =
                logger.LogDebug (sprintf "MultiAddEventsMd %A %s" arg md)
                let result =
                    fun _ ->
                        let conn = new NpgsqlConnection(connection)
                        conn.Open()
                        let transaction = conn.BeginTransaction() 
                        Async.RunSynchronously
                            (async {
                                let lastEventIdsPerContext =
                                    arg |>> (fun (eventId, _, version, name) -> (eventId, (this :> IEventStore<byte[]>).TryGetLastEventId version name))
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
                                                    // take this opportunity to evaluate if eventId cold be managed by the db function to do the check
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
                                                logger.LogDebug (sprintf "an error occurred: %A" ex.Message)
                                                transaction.Rollback()
                                                ex.Message |> Error
                                    else
                                        transaction.Rollback()
                                        Error ("EventId is not the last one " + (checkIdsErrors |> String.concat ", "))
                                try
                                    return result
                                finally
                                    conn.Close()
                            }, eventStoreTimeout)
                try
                    result ()
                with
                | _ as ex ->
                    logger.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message
                    
            member this.GetEventsAfterId version id name =
                logger.LogDebug (sprintf "GetEventsAfterId %s %s %d" version name id)
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
                                                readAsBinary read "event"
                                            )
                                        )
                                        |> Seq.toList
                                        |> Ok
                                    with
                                    | _ as ex ->
                                        logger.LogError (sprintf "an error occurred: %A" ex.Message)
                                        ex.Message |> Error
                            }, eventStoreTimeout)
                try
                    result ()
                with    
                | _ as ex  ->  
                    logger.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message

            member this.SetSnapshot version (id: int, snapshot: byte array) name =
                logger.LogDebug (sprintf "SetSnapshot %s %A %s" version id name)
                let command = sprintf "INSERT INTO snapshots%s%s (event_id, snapshot, timestamp) VALUES (@event_id, @snapshot, @timestamp)" version name
                let tryEvent = ((this :> IEventStore<byte []>).TryGetEvent version id name)
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
                                                        ("snapshot",  sqlBinary snapshot);
                                                        ("timestamp", Sql.timestamp event.Timestamp)
                                                    ]
                                                ]
                                        ]
                                }, eventStoreTimeout)
                        |> ignore
                        |> Ok
                    with
                    | _ as ex -> 
                        logger.LogError (sprintf "an error occurred: %A" ex.Message)
                        ex.Message |> Error

            member this.SetInitialAggregateState aggregateId version name json =
                logger.LogDebug (sprintf "SetInitialAggregateState %A %s %s" aggregateId version name)
                let insertSnapshot = sprintf "INSERT INTO snapshots%s%s (aggregate_id,  snapshot, timestamp) VALUES (@aggregate_id,  @snapshot, @timestamp)" version name
                let makeFirstEmptyEvent = sprintf "INSERT INTO aggregate_events%s%s (aggregate_id) VALUES (@aggregate_id)" version name
                
                let result =
                    fun _ ->
                        Async.RunSynchronously
                            (async {
                                return 
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
                                            // tests in sharpinoSample7 binary show that the following two statements are needed
                                            // on the other hand the equivalent in jsonEventStore are not needed (to be investigated)
                                            |> Async.AwaitTask
                                            |> Async.RunSynchronously
                                        () |> Ok
                                    with
                                    | _ as ex -> 
                                        logger.LogError (sprintf "an error occurred: %A" ex.Message)
                                        ex.Message |> Error
                            }, eventStoreTimeout)
                try
                    result ()
                with
                | _ as ex ->
                    logger.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message

            member this.SetInitialAggregateStatesAsync(version: Version, name: Name, idsAndSnapshots: (AggregateId * byte[])[], ?ct: CancellationToken) =
                logger.LogDebug (sprintf "SetInitialAggregateStatesAsync %s %s" version name)
                let insertSnapshotCmd = sprintf "INSERT INTO snapshots%s%s (aggregate_id, snapshot, timestamp) VALUES (@aggregate_id, @snapshot, @timestamp)" version name
                let firstEmptyEventCmd = sprintf "INSERT INTO aggregate_events%s%s (aggregate_id) VALUES (@aggregate_id)" version name

                task {
                    use conn = new NpgsqlConnection(connection)
                    use cts = CancellationTokenSource.CreateLinkedTokenSource
                                  (defaultArg ct (new CancellationTokenSource(eventStoreTimeout)).Token)
                    cts.CancelAfter(cancellationTokenSourceExpiration)
                    do! conn.OpenAsync(cts.Token).ConfigureAwait(false)
                    let! transaction = conn.BeginTransactionAsync(cts.Token)
                    try
                        for (aggregateId, json) in idsAndSnapshots do
                            use insertSnapshot' = new NpgsqlCommand(insertSnapshotCmd, conn)
                            insertSnapshot'.CommandTimeout <- max 1 (eventStoreTimeout / 1000)
                            insertSnapshot'.Parameters.AddWithValue("aggregate_id", aggregateId) |> ignore
                            insertSnapshot'.Parameters.AddWithValue("snapshot", json) |> ignore
                            insertSnapshot'.Parameters.AddWithValue("timestamp", System.DateTime.Now) |> ignore
                            do! insertSnapshot'.ExecuteNonQueryAsync(cts.Token) |> Async.AwaitTask |> Async.Ignore

                            use firstEmptyEvent' = new NpgsqlCommand(firstEmptyEventCmd, conn)
                            firstEmptyEvent'.CommandTimeout <- max 1 (eventStoreTimeout / 1000)
                            firstEmptyEvent'.Parameters.AddWithValue("aggregate_id", aggregateId) |> ignore
                            do! firstEmptyEvent'.ExecuteNonQueryAsync(cts.Token) |> Async.AwaitTask |> Async.Ignore

                        do! transaction.CommitAsync(cts.Token)
                        return Ok ()
                    with e ->
                        do! transaction.RollbackAsync(cts.Token)
                        return Error e.Message
                }
                   
            member this.SetInitialAggregateStateAsync (aggregateId, version, name, json, ?ct: CancellationToken) =
                logger.LogDebug (sprintf "SetInitialAggregateStateAsync %A %s %s" aggregateId version name)
                let insertSnapshot = sprintf "INSERT INTO snapshots%s%s (aggregate_id, snapshot, timestamp) VALUES (@aggregate_id, @snapshot, @timestamp)" version name
                let firstEmptyAggregateEvent = sprintf "INSERT INTO aggregate_events%s%s (aggregate_id) VALUES (@aggregate_id)" version name
                task
                    {
                        use conn= new NpgsqlConnection(connection)
                        use cts = CancellationTokenSource.CreateLinkedTokenSource
                                      (defaultArg ct (new CancellationTokenSource(eventStoreTimeout)).Token)
                        cts.CancelAfter(cancellationTokenSourceExpiration)
                        do! conn.OpenAsync(cts.Token).ConfigureAwait(false)
                        let! transaction = conn.BeginTransactionAsync (cts.Token)
                        try
                            use insertSnapshot' = new NpgsqlCommand(insertSnapshot, conn)
                            insertSnapshot'.CommandTimeout <- max 1 (eventStoreTimeout / 1000)
                            insertSnapshot'.Parameters.AddWithValue("aggregate_id", aggregateId) |> ignore
                            insertSnapshot'.Parameters.AddWithValue("snapshot", json) |> ignore
                            insertSnapshot'.Parameters.AddWithValue("timestamp", System.DateTime.Now) |> ignore
                            
                            use firstEmptyAggregateEvent' = new NpgsqlCommand(firstEmptyAggregateEvent, conn)
                            firstEmptyAggregateEvent'.CommandTimeout <- max 1 (eventStoreTimeout / 1000)
                            firstEmptyAggregateEvent'.Parameters.AddWithValue("aggregate_id", aggregateId) |> ignore
                            
                            let! _ = insertSnapshot'.ExecuteScalarAsync(cts.Token).ConfigureAwait(false)
                            let! _ = firstEmptyAggregateEvent'.ExecuteScalarAsync(cts.Token).ConfigureAwait(false)
                            let! _ = transaction.CommitAsync(cts.Token)
                            return Ok ()
                        with
                            | _ as e ->
                                let! _ = transaction.RollbackAsync (cts.Token)
                                return Error e.Message
                    }
            member this.SetInitialAggregateStates version name idsAndSnapshots =
                logger.LogDebug (sprintf "SetInitialAggregateStates %s %s"  version name)
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
                                                            ("snapshot",  sqlBinary json);
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
                                    logger.LogError (sprintf "an error occurred: %A" ex.Message)
                                    ex.Message |> Error
                        }, eventStoreTimeout)
                with
                | _ as ex ->
                    logger.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message

            member this.SetInitialAggregateStateAndAddEventsMd eventId aggregateId aggregateVersion aggregatename initInstance contextVersion contextName md events =
                logger.LogDebug "entered in setInitialAggregateStateAndAddEvents"

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
                                let lastEventId = (this :> IEventStore<byte[]>).TryGetLastEventId contextVersion contextName
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
                                                                    ("snapshot",  sqlBinary initInstance);
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
                                                logger.LogError (sprintf "an error occurred: %A" ex.Message)
                                                transaction.Rollback()
                                                ex.Message |> Error
                                    else
                                        transaction.Rollback()
                                        Error $"EventId check failed. EventId passed {eventId}. Latest eventId: {lastEventId}"
                                try
                                    return result
                                finally
                                    conn.Close()
                            }, eventStoreTimeout)
                try
                    result ()
                with
                | _ as ex ->
                    logger.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message
                
            member this.SetInitialAggregateStateAndAddAggregateEventsMd eventId aggregateId aggregateVersion aggregatename secondAggregateId json contextVersion contextName md events =
                logger.LogDebug "entered in SetInitialAggregateStateAndAddAggregateEvents"

                let insertSnapshot = sprintf "INSERT INTO snapshots%s%s (aggregate_id,  snapshot, timestamp) VALUES (@aggregate_id,  @snapshot, @timestamp)" aggregateVersion aggregatename
                let insertFirstEmptyAggregateEvent = sprintf "INSERT INTO aggregate_events%s%s (aggregate_id) VALUES (@aggregate_id)" aggregateVersion aggregatename
                let insertEvents = sprintf "SELECT insert_md%s_aggregate_event_and_return_id(@event, @aggregate_id, @md);" (contextVersion + contextName)
                
                let result =
                    fun _ ->
                        let conn = new NpgsqlConnection(connection)
                        conn.Open()
                        let transaction = conn.BeginTransaction()
                        let lastEventId =
                            (this :> IEventStore<byte[]>).TryGetLastAggregateEventId contextVersion contextName secondAggregateId
                            
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
                                                                    ("snapshot",  sqlBinary json);
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
                                                logger.LogError (sprintf "an error occurred: %A" ex.Message)
                                                transaction.Rollback()
                                                ex.Message |> Error
                                    else
                                        transaction.Rollback()
                                        Error $"EventId check failed. eventId passed {eventId}. Latest eventId: {lastEventId}"
                                try
                                    return result
                                finally
                                    conn.Close()
                            }, eventStoreTimeout)
                try
                    result ()
                with
                | _ as ex ->
                    logger.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message
                
            member this.SetInitialAggregateStateAndMultiAddAggregateEventsMd  aggregateId version name jsonSnapshot md events =
                logger.LogDebug "entered in SetInitialAggregateStateAndMultiAddAggregateEvents"
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
                                ((this :> IEventStore<byte[]>).TryGetLastAggregateEventId version name aggrId)
                        
                        let eventIds =
                            events
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
                                                                    ("snapshot",  sqlBinary jsonSnapshot);
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
                                                logger.LogError (sprintf "an error occurred: %A" ex.Message)
                                                transaction.Rollback()
                                                ex.Message |> Error
                                    else
                                        transaction.Rollback()
                                        Error ("EventId is not the last one" + (errors |> String.concat ", "))
                                try 
                                    return result
                                finally
                                    conn.Close()
                            }, eventStoreTimeout)
                try
                    result ()
                with
                | _ as ex ->
                    logger.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message
                    
            member this.SetAggregateSnapshot version (aggregateId: AggregateId, eventId: int, snapshot: byte array) name =
                logger.LogDebug "entered in setAggregateSnapshot"
                let command = sprintf "INSERT INTO snapshots%s%s (aggregate_id, event_id, snapshot, timestamp) VALUES (@aggregate_id, @event_id, @snapshot, @timestamp)" version name
                let tryEvent = ((this :> IEventStore<byte array>).TryGetEvent version eventId name)
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
                                                        ("snapshot",  sqlBinary snapshot);
                                                        ("timestamp", Sql.timestamp event.Timestamp)
                                                    ]
                                                ]
                                        ]
                                }, eventStoreTimeout)
                                |> ignore
                                |> Ok
                    with
                    | _ as ex -> 
                        logger.LogError (sprintf "an error occurred: %A" ex.Message)
                        ex.Message |> Error
                | None, 0 ->
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
                                                        ("snapshot",  sqlBinary snapshot);
                                                        ("timestamp", Sql.timestamptz System.DateTime.UtcNow)
                                                    ]
                                                ]
                                        ]
                            }, eventStoreTimeout)
                        |> ignore
                        |> Ok
                    with    
                    | _ as ex ->
                        logger.LogError (sprintf "an error occurred: %A" ex.Message)
                        ex.Message |> Error
            
            member this.GetEventsInATimeInterval version name dateFrom dateTo =
                logger.LogDebug (sprintf "GetEventsInATimeInterval %s %s %A %A" version name dateFrom dateTo)
                let query = sprintf "SELECT id, event FROM events%s%s WHERE timestamp >= @dateFrom AND timestamp <= @dateTo ORDER BY id" version name
                try
                    Async.RunSynchronously
                        (async {
                            return
                                connection
                                |> Sql.connect
                                |> Sql.query query
                                |> Sql.parameters ["dateFrom", Sql.timestamptz dateFrom; "dateTo", Sql.timestamptz dateTo]
                                |> Sql.execute ( fun read ->
                                    (
                                        read.int "id",
                                        readAsBinary read "event"
                                    )
                                )
                                |> Seq.toList
                            }, eventStoreTimeout)
                        |> Ok
                        
                with
                | _ as ex ->
                    logger.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message
           
            // todo: verify the "async" version and wrap to it if working fine
            member this.GetAggregateEventsInATimeInterval version name aggregateId dateFrom dateTo =
                logger.LogDebug (sprintf "TryGetLastSnapshotId %s %s" version name)
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
                                        readAsBinary read "event"
                                    )
                                )
                                |> Seq.toList
                            }, eventStoreTimeout)
                        |> Ok    
                    with
                    | _ as ex ->
                        logger.LogError (sprintf "an error occurred: %A" ex.Message)
                        Error ex.Message
                      
            member this.GetAllAggregateEventsInATimeInterval version name dateFrom dateTo =
                logger.LogDebug (sprintf "GetAllAggregateEventsInATimeInterval %s %s %A %A" version name dateFrom dateTo)
                let query = sprintf "SELECT id, event FROM events%s%s WHERE timestamp >= @dateFrom AND timestamp <= @dateTo ORDER BY id" version name
                try
                    Async.RunSynchronously
                        (async {
                            return
                                connection
                                |> Sql.connect
                                |> Sql.query query
                                |> Sql.parameters ["dateFrom", Sql.timestamptz dateFrom; "dateTo", Sql.timestamptz dateTo]
                                |> Sql.execute ( fun read ->
                                    (
                                        read.int "id",
                                        readAsBinary read "event"
                                    )
                                )
                                |> Seq.toList
                            }, eventStoreTimeout)
                    |> Ok
                with
                | _ as ex ->
                    logger.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message 
           
            member this.GetAllAggregateEventsInATimeIntervalAsync (version, name, dateFrom, dateTo, ?ct:CancellationToken) =
                logger.LogDebug (sprintf "GetAllAggregateEventsInATimeIntervalAsync %s %s %A %A" version name dateFrom dateTo)
                let query = sprintf "SELECT id, aggregate_id, event FROM events%s%s WHERE timestamp >= @dateFrom AND timestamp <= @dateTo ORDER BY id" version name
                task
                    {
                        use cts = CancellationTokenSource.CreateLinkedTokenSource
                                      (defaultArg ct (new CancellationTokenSource(eventStoreTimeout)).Token)
                        cts.CancelAfter(cancellationTokenSourceExpiration)
                        
                        use conn = new NpgsqlConnection(connection)
                        do! conn.OpenAsync(cts.Token).ConfigureAwait(false)
                        use command = new NpgsqlCommand(query, conn)
                        command.CommandTimeout <- max 1 (eventStoreTimeout / 100)
                        command.Parameters.AddWithValue("dateFrom", dateFrom) |> ignore
                        command.Parameters.AddWithValue("dateTo", dateTo) |> ignore
                        use! reader = command.ExecuteReaderAsync(cts.Token).ConfigureAwait(false)
                        let results = ResizeArray<_>()
                        let rec loop () = task {
                            let! hasRow = reader.ReadAsync(cts.Token).ConfigureAwait(false)
                            if hasRow then
                                let eventId = reader.GetInt32(0)
                                let aggregateId = reader.GetGuid(1)
                                let eventJson = reader.GetFieldValue<byte[]>(2)
                                results.Add(eventId, aggregateId, eventJson)
                                return! loop ()
                            else
                                return ()
                        }
                        do! loop ()
                        return results |> Ok
                    }
                    
            member this.GetMultipleAggregateEventsInATimeInterval version name aggregateIds dateFrom dateTo =
                let aggregateIdsArray = aggregateIds |> Array.ofList
                logger.LogDebug (sprintf "GetMultipleAggregateEventsInATimeInterval %s %s %A %A" version name dateFrom dateTo)
                let query = sprintf "SELECT id, aggregate_id, event FROM events%s%s WHERE aggregate_id = ANY(@aggregateIds) AND timestamp >= @dateFrom AND timestamp <= @dateTo ORDER BY id" version name
                try
                    Async.RunSynchronously
                        (async {
                            return
                                connection
                                |> Sql.connect
                                |> Sql.query query
                                |> Sql.parameters ["aggregateIds", Sql.uuidArray aggregateIdsArray; "dateFrom", Sql.timestamptz dateFrom; "dateTo", Sql.timestamptz dateTo]
                                |> Sql.execute ( fun read ->
                                    (
                                        read.int "id",
                                        read.uuid "aggregate_id",
                                        readAsBinary read "event"
                                    )
                                )
                                |> Seq.toList
                            }, eventStoreTimeout)
                    |> Ok
                with
                | _ as ex ->
                    logger.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message
                     
                    
            member this.GetMultipleAggregateEventsInATimeIntervalAsync (version, name, aggregateIds, dateFrom, dateTo, ?ct) =
                logger.LogDebug (sprintf "GetMultipleAggregateEventsInATimeIntervalAsync %s %s %A %A" version name dateFrom dateTo)
                
                let aggregateIdsArray = aggregateIds |> Array.ofList
                let query = sprintf "SELECT id, aggregate_id, event FROM events%s%s WHERE aggregate_id = ANY(@aggregateIds) AND timestamp >= @dateFrom AND timestamp <= @dateTo ORDER BY id" version name
                task
                    {
                        try
                            use cts = CancellationTokenSource.CreateLinkedTokenSource
                                          (defaultArg ct (new CancellationTokenSource(eventStoreTimeout)).Token)
                            cts.CancelAfter(cancellationTokenSourceExpiration)
                            use conn = new NpgsqlConnection(connection)
                            do! conn.OpenAsync(cts.Token).ConfigureAwait(false)
                            use command = new NpgsqlCommand(query, conn)
                            command.CommandTimeout <- max 1 (eventStoreTimeout / 100)
                            command.Parameters.AddWithValue("dateFrom", dateFrom) |> ignore
                            command.Parameters.AddWithValue("dateTo", dateTo) |> ignore
                            command.Parameters.AddWithValue("aggregateIds", aggregateIdsArray) |> ignore
                            use! reader = command.ExecuteReaderAsync(cts.Token).ConfigureAwait(false)
                            let results = ResizeArray<_>()
                            let rec loop () = task {
                                let! hasRow = reader.ReadAsync(cts.Token).ConfigureAwait(false)
                                if hasRow then
                                    let eventId = reader.GetInt32(0)
                                    let aggregateId = reader.GetGuid(1)
                                    let eventJson = reader.GetFieldValue<byte[]>(2)
                                    results.Add(eventId, aggregateId, eventJson)
                                    return! loop ()
                                else
                                    return ()
                            }
                            do! loop ()
                            return results |> Seq.toList |> Ok
                        with
                        | _ as ex ->
                            logger.LogError (sprintf "an error occurred: %A" ex.Message)
                            return Error ex.Message    
                    }
                    
            member this.GetAggregateSnapshotsInATimeInterval version name dateFrom dateTo =
                logger.LogDebug (sprintf "GetAggregateSnapshotsInATimeInterval %s %s %A %A" version name dateFrom dateTo)
                let query = sprintf "SELECT id, aggregate_id, timestamp FROM snapshots%s%s where timestamp >= @dateFrom AND timestamp <= @dateTo ORDER BY id" version name
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
                                            read.uuid "aggregate_id",
                                            read.dateTime "timestamp",
                                            readAsBinary read "snapshot"
                                        )
                                    )
                                    |> Seq.toList
                                }, eventStoreTimeout)
                    result |> Ok      
                with
                | _ as ex ->
                    logger.LogError (sprintf "an error occurred: %A" ex.Message)
                    ex.Message |> Error
                    
            member this.GetAggregateIdsInATimeInterval version name dateFrom dateTo =
                logger.LogDebug (sprintf "GetAggregateIdsInATimeInterval %s %s %A %A" version name dateFrom dateTo)
                let query = sprintf "SELECT DISTINCT aggregate_id FROM snapshots%s%s where timestamp >= @dateFrom AND timestamp <= @dateTo" version name
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
                            }, eventStoreTimeout)
                    |> Ok
                with
                | _ as ex ->
                    logger.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message
            
            member this.GetAggregateIds version name =
                logger.LogDebug (sprintf "GetAggregateIds %s %s" version name)
                let query = sprintf "SELECT DISTINCT aggregate_id FROM snapshots%s%s" version name
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
                            }, eventStoreTimeout)
                    |> Ok
                with
                | _ as ex ->
                    logger.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message        
            member this.GetAggregateIdsAsync (version, name, ?ct) =
                logger.LogDebug (sprintf "GetAggregateIdsAsync %s %s" version name)
                let query = sprintf "SELECT DISTINCT  aggregate_id FROM snapshots%s%s" version name
                taskResult
                    {
                        try
                            use cts = CancellationTokenSource.CreateLinkedTokenSource
                                          (defaultArg ct (new CancellationTokenSource(eventStoreTimeout)).Token)
                            cts.CancelAfter(cancellationTokenSourceExpiration)
                            use conn = new NpgsqlConnection(connection)
                            do! conn.OpenAsync(cts.Token).ConfigureAwait(false)
                            use command = new NpgsqlCommand(query, conn)
                            use! reader = command.ExecuteReaderAsync(cts.Token).ConfigureAwait(false)
                            let results = ResizeArray<_>()
                            let rec loop () = task {
                                let! hasRow = reader.ReadAsync(cts.Token).ConfigureAwait(false)
                                if hasRow then
                                    let aggregateId = reader.GetGuid(0)
                                    results.Add(aggregateId)
                                    return! loop ()
                                else
                                    return ()
                            }
                            do! loop ()
                            return results |> Seq.toList
                        with
                        | _ as ex ->
                            logger.LogError (sprintf "an error occurred: %A" ex.Message)
                            return! Error ex.Message
                    }
             
            member this.GetAggregateIdsInATimeIntervalAsync (version, name, dateFrom, dateTo, ?ct) =
                logger.LogDebug (sprintf "GetAggregateIdsInATimeIntervalAsync %A %A %A %A" version name dateFrom dateTo)
                let query = sprintf "SELECT DISTINCT  aggregate_id FROM snapshots%s%s where timestamp >= @dateFrom AND timestamp <= @dateTo" version name
                taskResult
                    {
                        try
                            use cts = CancellationTokenSource.CreateLinkedTokenSource
                                          (defaultArg ct (new CancellationTokenSource(eventStoreTimeout)).Token)
                            cts.CancelAfter(cancellationTokenSourceExpiration)
                            use conn = new NpgsqlConnection(connection)
                            do! conn.OpenAsync(cts.Token).ConfigureAwait(false)
                            use command = new NpgsqlCommand(query, conn)
                            command.Parameters.AddWithValue("dateFrom", dateFrom) |> ignore
                            command.Parameters.AddWithValue("dateTo", dateTo) |> ignore
                            use! reader = command.ExecuteReaderAsync(cts.Token).ConfigureAwait(false)
                            let results = ResizeArray<_>()
                            let rec loop () = task {
                                let! hasRow = reader.ReadAsync(cts.Token).ConfigureAwait(false)
                                if hasRow then
                                    let aggregateId = reader.GetGuid(0)
                                    results.Add(aggregateId)
                                    return! loop ()
                                else
                                    return ()
                            }
                            do! loop ()
                            return results |> Seq.toList
                        with
                        | _ as ex ->
                            logger.LogError (sprintf "an error occurred: %A" ex.Message)
                            return! Error ex.Message
                    }
                    
            member this.TryGetLastSnapshotId version name =
                logger.LogDebug (sprintf "TryGetLastSnapshotId %s %s" version name)
                // log.Debug (sprintf "TryGetLastSnapshotId %s %s" version name)
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
                        }, eventStoreTimeout)
                with
                | _ as ex ->
                    logger.LogError (sprintf "an error occurred: %A" ex.Message)
                    None

            member this.TryGetFirstSnapshot version name aggregateId =
                logger.LogDebug (sprintf "TryGetSnapshotById %s %s %A" version name aggregateId)
                let query = sprintf "SELECT id, snapshot FROM snapshots%s%s WHERE aggregate_id = @aggregateId ORDER BY id LIMIT 1" version name
                
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
                                        readAsBinary read "snapshot"
                                    )
                                )
                                |> Seq.tryHead
                                |> Result.ofOption "Snapshot not found"
                        }, eventStoreTimeout)
                with
                | _ as ex ->
                    logger.LogError (sprintf "TryGetSnapshotById an error occurred: %A" ex.Message)
                    Error (sprintf "error occurred %A" ex.Message)
            
            member this.TryGetSnapshotById version name id =
                logger.LogDebug (sprintf "TryGetSnapshotById %s %s %d" version name id)
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
                                        readAsBinary read "snapshot"
                                    )
                                )
                                |> Seq.tryHead
                        }, eventStoreTimeout)
                with
                | _ as ex ->
                    logger.LogError (sprintf "TryGetSnapshotById an error occurred: %A" ex.Message)
                    None
                    
            member this.TryGetLastAggregateSnapshot version name aggregateId =
                logger.LogDebug (sprintf "TryGetLastAggregateSnapshot %s %s %A" version name aggregateId)
                
                let query = sprintf "SELECT event_id, is_deleted, snapshot FROM snapshots%s%s WHERE aggregate_id = @aggregateId ORDER BY id DESC LIMIT 1" version name
                try
                    Async.RunSynchronously
                        (async {
                            let result =
                                connection
                                |> Sql.connect
                                |> Sql.query query
                                |> Sql.parameters ["aggregateId", Sql.uuid aggregateId]
                                |> Sql.execute (fun read ->
                                    (
                                        read.intOrNone "event_id",
                                        readAsBinary read "snapshot",
                                        read.bool "is_deleted"
                                    )
                                )
                                |> Seq.tryHead
                            match result with
                            | Some (eventId, snapshot, isDeleted) ->
                                if isDeleted then
                                    return Error $"object {aggregateId} type {version}{name} previously deleted"
                                else
                                    return Ok (eventId, snapshot)
                            | None -> return Error $"object {aggregateId} type {version}{name} not existing"    
                        }, eventStoreTimeout)
                with
                | _ as ex ->
                    logger.LogError (sprintf "TryGetLastAggregateSnapshot an error occurred: %A" ex.Message)
                    Error (sprintf "error occurred %A" ex.Message)
                    
            member this.TryGetLastAggregateSnapshotAsync (version, name, aggregateId, ?ct) =
                logger.LogDebug (sprintf "TryGetLastAggregateSnapshotAsync %s %s %A" version name aggregateId)
                let query = sprintf "SELECT event_id, is_deleted, snapshot FROM snapshots%s%s WHERE aggregate_id = @aggregateId ORDER BY id DESC LIMIT 1" version name
                task
                    {
                        try
                            use cts = CancellationTokenSource.CreateLinkedTokenSource
                                          (defaultArg ct (new CancellationTokenSource(eventStoreTimeout)).Token)
                            cts.CancelAfter(cancellationTokenSourceExpiration)
                            use conn = new NpgsqlConnection(connection)
                            do! conn.OpenAsync(cts.Token).ConfigureAwait(false)
                            use command = new NpgsqlCommand(query, conn)
                            command.CommandTimeout <- max 1 (eventStoreTimeout / 1000)
                            command.Parameters.AddWithValue("aggregateId", aggregateId) |> ignore
                            use! reader = command.ExecuteReaderAsync(cts.Token).ConfigureAwait(false)
                            let! hasRow = reader.ReadAsync(cts.Token).ConfigureAwait(false)
                            if hasRow then
                                let eventIdOpt = if reader.IsDBNull(0) then None else Some (reader.GetInt32(0))
                                let isDeleted = reader.GetBoolean(1)
                                let snapshotBin = reader.GetFieldValue<byte[]>(2)
                                if isDeleted then
                                    return Error (sprintf "object %A type %s%s is deleted" aggregateId version name)
                                else
                                    return Ok (eventIdOpt, snapshotBin)
                            else
                                return Error (sprintf "object %A type %s%s not existing" aggregateId version name)
                        with ex ->
                            logger.LogError (sprintf "TryGetLastAggregateSnapshotAsync: an error occurred: %A" ex.Message)
                            return Error ex.Message
                    }
                    
            member this.TryGetAggregateSnapshotById version name aggregateId id =
                logger.LogDebug (sprintf "TryGetSnapshotById %s %s %d" version name id)
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
                                        readAsBinary read "snapshot"
                                    )
                                )
                                |> Seq.tryHead
                            }, eventStoreTimeout)
                with
                | _ as ex ->
                    logger.LogError (sprintf "TryGetSnapshotById an error occurred: %A" ex.Message)
                    None
            
            member this.TryGetLastAggregateSnapshotEventId version name aggregateId =
                logger.LogDebug (sprintf "TryGetLastAggregateSnapshotEventId %s %s" version name)
                let query = sprintf "SELECT event_id FROM snapshots%s%s WHERE aggregate_id = @aggregateId ORDER BY id DESC LIMIT 1" version name
                try
                    Async.RunSynchronously
                        (async  {
                            return
                                connection
                                |> Sql.connect
                                |> Sql.query query
                                |> Sql.parameters ["aggregateId", Sql.uuid aggregateId]
                                |> Sql.execute (fun read -> read.int "event_id")
                                |> Seq.tryHead
                        }, eventStoreTimeout)
                with
                    | _ as ex ->
                        // not an error anymore
                        // logger.LogError (sprintf "an error occurred: %A" ex.Message)
                        None

            member this.TryGetLastAggregateEventId version name aggregateId =
                logger.LogDebug (sprintf "TryGetLastEventId %s %s" version name)
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
                        }, eventStoreTimeout)
                with
                | _ as ex ->
                    logger.LogError (sprintf "an error occurred: %A" ex.Message)
                    None

            member this.AddAggregateEventsMd (eventId: EventId) (version: Version) (name: Name) (aggregateId: System.Guid) (md: Metadata) (events: List<byte[]>) : Result<List<int>,string> =
                (this :> IEventStore<byte[]>).AddAggregateEventsMdAsync(eventId, version, name, aggregateId, md, events).GetAwaiter().GetResult()
                    
            member this.MultiAddAggregateEventsMd md (arg: List<EventId * List<byte array> * Version * Name * System.Guid>) =
                logger.LogDebug (sprintf "MultiAddAggregateEvents %A %s" arg md)
                
               
                let result =
                    fun _ ->
                        let conn = new NpgsqlConnection(connection)
                        conn.Open()
                        let transaction = conn.BeginTransaction() 

                        let lastEventIds =
                            arg 
                            |>> 
                            fun ( _, _, version, name, aggregateId) -> 
                                ((this :> IEventStore<byte[]>).TryGetLastAggregateEventId version name aggregateId) // |>> (fun x -> x)

                        let eventIds =
                            arg 
                            |>> fun (eventId, _, _, _, _) -> eventId
                        
                        let check =
                            List.zip lastEventIds eventIds
                            |> List.forall (fun (lastEventId, eventId) -> lastEventId.IsNone && eventId = 0 || lastEventId.Value = eventId)
                            
                        let errors =
                            List.zip lastEventIds eventIds
                            |> List.filter (fun (lastEventId, eventId) -> lastEventId.IsNone && eventId <> 0 || (lastEventId.IsSome && lastEventId.Value <> eventId))
                            |> List.map (fun (lastEventId, eventId) -> sprintf "EventId check failed. eventId passed %d. Latest eventId: %A" eventId lastEventId)

                        Async.RunSynchronously
                            (async {
                                let result =
                                    if check then
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
                                                                command.Parameters.AddWithValue("event", event ) |> ignore
                                                                command.Parameters.AddWithValue("@aggregate_id", aggregateId ) |> ignore
                                                                command.Parameters.AddWithValue("md", md ) |> ignore
                                                                let result = command.ExecuteScalar() 
                                                                result :?> int
                                                
                                            transaction.Commit()    
                                            cmdList |> Ok
                                        with
                                            | _ as ex ->
                                                logger.LogError (sprintf "an error occurred: %A" ex.Message)
                                                transaction.Rollback()
                                                ex.Message |> Error
                                    else
                                        transaction.Rollback()
                                        Error ("eventids check failed " + (errors |> String.concat ", "))
                                try
                                    return result
                                finally
                                    conn.Close()
                            }, eventStoreTimeout)
                try
                    result ()
                with
                | _ as ex ->
                    logger.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message

            member this.GetAggregateEventsAfterId version name aggregateId id: Result<List<EventId * byte array>,string> = 
                logger.LogDebug (sprintf "GetAggregateEventsAfterId %s %s %A %d" version name aggregateId id)
                taskResult
                    {
                        return! (this :> IEventStore<byte[]>).GetAggregateEventsAfterIdAsync(version, name, aggregateId, id)
                    }
                |> Async.AwaitTask
                |> Async.RunSynchronously
                                
            member this.GetAggregateEvents version name aggregateId: Result<List<EventId * byte array>, string> = 
                logger.LogDebug (sprintf "GetAggregateEvents %s %s %A" version name aggregateId)
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
                                                readAsBinary read "event"
                                            )
                                        )
                                        |> Seq.toList
                                        |> Ok
                                    with
                                    | _ as ex ->
                                        logger.LogError (sprintf "an error occurred: %A" ex.Message)
                                        ex.Message |> Error
                            }, eventStoreTimeout)
                try
                    result ()
                with
                | _ as ex ->
                    logger.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message
            
            member this.GetAggregateEventsAsync (version: Version, name: Name, aggregateId: AggregateId, ?ct: CancellationToken): Task<Result<List<EventId * byte array>, string>> =
                logger.LogDebug (sprintf "GetAggregateEvents %s %s %A" version name aggregateId)
                let query = sprintf "SELECT id, event FROM events%s%s WHERE aggregate_id = @aggregateId ORDER BY id"  version name
                task {
                    try
                        use cts = CancellationTokenSource.CreateLinkedTokenSource
                                      (defaultArg ct (new CancellationTokenSource(eventStoreTimeout)).Token)
                        cts.CancelAfter(cancellationTokenSourceExpiration)
                        use conn = new NpgsqlConnection(connection)
                        do! conn.OpenAsync(cts.Token).ConfigureAwait(false)
                        use command = new NpgsqlCommand(query, conn)
                        command.CommandTimeout <- max 1 (eventStoreTimeout / 1000)
                        command.Parameters.AddWithValue("aggregateId", aggregateId) |> ignore
                        use! reader = command.ExecuteReaderAsync(cts.Token).ConfigureAwait(false)
                        let results = ResizeArray<_>()
                        let rec loop () = task {
                            let! hasRow = reader.ReadAsync(cts.Token).ConfigureAwait(false)
                            if hasRow then
                                let eventId = reader.GetInt32(0)
                                let eventBytes = reader.GetFieldValue<byte[]>(1)
                                results.Add(eventId, eventBytes)
                                return! loop ()
                            else
                                return ()
                        }
                        do! loop ()
                        return results |> Seq.toList |> Ok
                    with ex ->
                        logger.LogError (sprintf "an error occurred: %A" ex.Message)
                        return Error ex.Message
                }
                    
            member this.GDPRReplaceSnapshotsAndEventsOfAnAggregate version name aggregateId snapshot event =
                logger.LogDebug (sprintf "GDPRReplaceSnapshotsAndEventsOfAnAggregate %s %s %A %A %A" version name aggregateId snapshot event)
                let conn = new NpgsqlConnection(connection)
                let sqlReplaceAllAggregates = (sprintf "UPDATE snapshots%s%s SET snapshot = @snapshot WHERE aggregate_id = @aggregateId" version name)
                let sqlReplaceAllEvents = (sprintf "UPDATE events%s%s SET event = @event WHERE aggregate_id = @aggregateId" version name)
               
                let result =
                    fun _ ->
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
                                                                ("@snapshot", sqlBinary snapshot);
                                                                ("@aggregateId", Sql.uuid aggregateId)
                                                            ]
                                                        ]
                                                    sqlReplaceAllEvents,
                                                        [
                                                            [
                                                                ("@event", sqlBinary event);
                                                                ("@aggregateId", Sql.uuid aggregateId)
                                                            ]
                                                        ]
                                                ]
                                        transaction.Commit()
                                        Ok ()
                                    with
                                    | _ as ex -> 
                                        logger.LogError (sprintf "an error occurred: %A" ex.Message)
                                        transaction.Rollback()
                                        ex.Message |> Error
                                try
                                    return result
                                finally
                                    conn.Close()
                            }, eventStoreTimeout)
                try
                    result ()
                with
                | _ as ex ->
                    logger.LogError (sprintf "an error occurred: %A" ex.Message)
                    Error ex.Message
                
            member this.SnapshotAndMarkDeleted version name eventId aggregateId napshot =
                logger.LogDebug (sprintf "SnapshotAndMarkDeleted %s %s %A" version name aggregateId)
                let command = sprintf "INSERT INTO snapshots%s%s (aggregate_id, snapshot, timestamp, is_deleted) VALUES (@aggregate_id, @snapshot, @timestamp, true)" version name
                let lastEventId = (this :> IEventStore<byte[]>).TryGetLastAggregateEventId version name aggregateId
                
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
                                                        ("snapshot",  sqlBinary napshot)
                                                        ("timestamp", Sql.timestamp System.DateTime.Now)
                                                        ("is_deleted", Sql.bool true)
                                                    ]
                                                ]
                                        ]
                            }, eventStoreTimeout)
                            |> ignore
                            |> Ok
                    with
                    | _ as ex ->
                        logger.LogError (sprintf "an error occurred: %A" ex.Message)
                        Error ex.Message
                else
                    Error $"error checking event alignments. eventId passed: {eventId}. Latest event id: {lastEventId}"
                
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
                    logger.LogDebug (sprintf "SnapshotAndMarkDeleted %s %s %A" s1Version s1name s1AggregateId)
                    let snapCommand = sprintf "INSERT INTO snapshots%s%s (aggregate_id, snapshot, timestamp, is_deleted) VALUES (@aggregate_id, @snapshot, @timestamp, true)" s1Version s1name
                    let lastEventId = (this :> IEventStore<byte[]>).TryGetLastAggregateEventId s1Version s1name s1AggregateId
                    
                    let lastStreamEventId =
                        (this :> IEventStore<byte[]>).TryGetLastAggregateEventId streamAggregateVersion streamAggregateName streamAggregateId
                    
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
                                                                ("snapshot",  sqlBinary s1Snapshot)
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
                            }, eventStoreTimeout)
                        with
                        | _ as ex ->
                            logger.LogError (sprintf "an error occurred: %A" ex.Message)
                            Error ex.Message
                    else
                        Error $"error checking event alignments. eventId passed {s1EventId}. Latest event id: {lastEventId}"
            member this.SnapshotMarkDeletedAndMultiAddAggregateEventsMd
                md
                s1Version
                s1name
                s1EventId
                s1AggregateId
                s1Snapshot
                (arg: List<EventId * List<byte[]> * Version * Name * AggregateId>) =
                    let snapCommand = sprintf "INSERT INTO snapshots%s%s (aggregate_id, snapshot, timestamp, is_deleted) VALUES (@aggregate_id, @snapshot, @timestamp, true)" s1Version s1name
                    let lastEventId = (this :> IEventStore<byte[]>).TryGetLastAggregateEventId s1Version s1name s1AggregateId
                    
                    let lastEventIds =
                        arg 
                        |>> 
                        fun (_, _, version, name, aggregateId) -> 
                            ((this :> IEventStore<byte[]>).TryGetLastAggregateEventId version name aggregateId)
                            
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
                        
                    if ((lastEventId.IsNone && s1EventId = 0) || (lastEventId.IsSome && lastEventId.Value = s1EventId)) && checks then
                        
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
                                                            ("snapshot",  sqlBinary s1Snapshot)
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
                                        logger.LogError (sprintf "an error occurred: %A" ex.Message)
                                        transaction.Rollback()
                                        conn.Close()
                                        return (ex.Message |> Error)
                        }, eventStoreTimeout) 
                    else Error ("optimistic lock failure: " + (errors |> String.concat ", ") + $"lastEventId: {lastEventId}, eventId: {s1EventId}")
                        
