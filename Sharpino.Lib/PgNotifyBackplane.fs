namespace Sharpino

open System
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks
open Npgsql
open ZiggyCreatures.Caching.Fusion
open ZiggyCreatures.Caching.Fusion.Backplane
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection

[<AutoOpen>]
module PgNotifyBackplaneConfig =
    let builder = Host.CreateApplicationBuilder()

type PgNotifyBackplane(connectionString: string, channelName: string) =
    let logger = builder.Services.BuildServiceProvider().GetRequiredService<ILogger<PgNotifyBackplane>>()
    let handlers = new ConcurrentDictionary<string, BackplaneSubscriptionOptions>()
    let mutable listenCts: CancellationTokenSource = null
    let mutable listenTask: Task = null

    let sanitizeIdentifier (name: string) =
        // Simple sanitization to prevent SQL injection in channel name
        let safe = name |> Seq.filter (fun c -> Char.IsLetterOrDigit(c) || c = '_') |> Seq.toArray |> String
        if String.IsNullOrEmpty(safe) then "sharpino_cache_eviction" else safe

    let safeChannelName = sanitizeIdentifier channelName

    let handleIncomingPayload (payload: string) =
        task {
            let parts = payload.Split(':', 3)
            if parts.Length >= 3 then
                let actionStr = parts.[0]
                let sourceId  = parts.[1]
                let cacheKey  = parts.[2]
                logger.LogDebug ($"[PgNotifyBackplane] Received {actionStr} | key: {cacheKey} | source: {sourceId}")

                for KeyValue(instanceId, opts) in handlers do
                    if sourceId <> instanceId then
                        let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        let msg =
                            match actionStr with
                            | "EntrySet"    -> BackplaneMessage.CreateForEntrySet(sourceId, cacheKey, timestamp)
                            | "EntryRemove" -> BackplaneMessage.CreateForEntryRemove(sourceId, cacheKey, timestamp)
                            | _ -> null
                        if not (isNull msg) then
                            if not (isNull opts.IncomingMessageHandlerAsync) then
                                let! _ = opts.IncomingMessageHandlerAsync.Invoke(msg)
                                ()
                            elif not (isNull opts.IncomingMessageHandler) then
                                opts.IncomingMessageHandler.Invoke(msg)
        } :> Task

    let startListeningLoop (ct: CancellationToken) =
        Task.Run(fun () ->
            task {
                while not ct.IsCancellationRequested do
                    try
                        logger.LogDebug ($"[PgNotifyBackplane] Starting LISTEN connection for channel: {safeChannelName}")
                        use conn = new NpgsqlConnection(connectionString)
                        do! conn.OpenAsync(ct)

                        conn.Notification.Add(fun args ->
                            if args.Channel.Equals(safeChannelName, StringComparison.OrdinalIgnoreCase) then
                                Task.Run(fun () -> handleIncomingPayload args.Payload) |> ignore
                        )

                        use cmd = conn.CreateCommand()
                        cmd.CommandText <- sprintf "LISTEN %s" safeChannelName
                        let! _ = cmd.ExecuteNonQueryAsync(ct)
                        logger.LogDebug ($"[PgNotifyBackplane] LISTEN command executed successfully on channel: {safeChannelName}")

                        while not ct.IsCancellationRequested && conn.State = System.Data.ConnectionState.Open do
                            do! conn.WaitAsync(ct)
                    with
                    | :? OperationCanceledException ->
                        logger.LogDebug "[PgNotifyBackplane] Listening loop cancelled."
                    | ex ->
                        logger.LogError ($"[PgNotifyBackplane] Connection error in listening loop: {ex.Message}. Reconnecting in 3 seconds...")
                        try do! Task.Delay(3000, ct) with _ -> ()
            } :> Task
        )

    interface IFusionCacheBackplane with

        member this.Subscribe(options) =
            logger.LogDebug ($"[PgNotifyBackplane] Subscribe (SYNC) called for instance: {options.CacheInstanceId}")
            (this :> IFusionCacheBackplane).SubscribeAsync(options).AsTask().Wait()

        member this.SubscribeAsync(options: BackplaneSubscriptionOptions) =
            task {
                handlers.[options.CacheInstanceId] <- options
                logger.LogDebug ($"[PgNotifyBackplane] Registered local cache {options.CacheInstanceId} (total: {handlers.Count})")

                if isNull listenTask then
                    listenCts <- new CancellationTokenSource()
                    listenTask <- startListeningLoop listenCts.Token
                    logger.LogDebug ("[PgNotifyBackplane] Listening loop started.")

                if not (isNull options.ConnectHandlerAsync) then
                    let! _ = options.ConnectHandlerAsync.Invoke(BackplaneConnectionInfo(false))
                    ()
                elif not (isNull options.ConnectHandler) then
                    options.ConnectHandler.Invoke(BackplaneConnectionInfo(false))
            } |> ValueTask

        member this.Unsubscribe() =
            (this :> IFusionCacheBackplane).UnsubscribeAsync().AsTask().Wait()

        member this.UnsubscribeAsync() =
            task {
                if not (isNull listenCts) then
                    listenCts.Cancel()
                    try do! listenTask with _ -> ()
                    listenCts.Dispose()
                    listenCts <- null
                    listenTask <- null

                handlers.Clear()
                logger.LogDebug ("[PgNotifyBackplane] Unsubscribed and cleared all handlers.")
            } |> ValueTask

        member this.Publish(message, options, token) =
            logger.LogDebug ($"[PgNotifyBackplane] Publish (SYNC) called for key: {message.CacheKey}")
            (this :> IFusionCacheBackplane).PublishAsync(message, options, token).AsTask().Wait()

        member this.PublishAsync(message: BackplaneMessage, _options: FusionCacheEntryOptions, token: CancellationToken) =
            task {
                let actionStr =
                    match message.Action with
                    | BackplaneMessageAction.EntrySet    -> "EntrySet"
                    | BackplaneMessageAction.EntryRemove -> "EntryRemove"
                    | _                                  -> "Unknown"

                let payload = sprintf "%s:%s:%s" actionStr message.SourceId message.CacheKey
                logger.LogDebug ($"[PgNotifyBackplane] Publishing notification to channel {safeChannelName}: {payload}")
                
                use conn = new NpgsqlConnection(connectionString)
                do! conn.OpenAsync(token)
                use cmd = conn.CreateCommand()
                cmd.CommandText <- "SELECT pg_notify($1, $2)"
                cmd.Parameters.AddWithValue(safeChannelName) |> ignore
                cmd.Parameters.AddWithValue(payload) |> ignore
                let! _ = cmd.ExecuteNonQueryAsync(token)
                ()
            } |> ValueTask
