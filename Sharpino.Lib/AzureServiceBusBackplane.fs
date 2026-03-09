namespace Sharpino

open System
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks
open Azure.Messaging.ServiceBus
open ZiggyCreatures.Caching.Fusion
open ZiggyCreatures.Caching.Fusion.Backplane
open Azure.Messaging.ServiceBus.Administration

/// Azure Service Bus backplane for FusionCache.
/// One instance of this type is shared among ALL FusionCache instances in the process
/// (statesDetails, objectDetails, statePerAggregate). It multiplexes incoming messages
/// to every registered local FusionCache, and uses message.SourceId (set by FusionCache)
/// as the authoritative source identity in published messages.
type AzureServiceBusBackplane(connectionString: string, topicName: string, subscriptionName: string, ?managementConnectionString: string) =
    let client = new ServiceBusClient(connectionString)
    let adminClientOpt =
        match managementConnectionString with
        | Some cs when not (String.IsNullOrWhiteSpace cs) -> Some (new ServiceBusAdministrationClient(cs))
        | _ -> None
    let sender = client.CreateSender(topicName)
    // Key = CacheInstanceId assigned by FusionCache at Subscribe time
    let handlers = new ConcurrentDictionary<string, BackplaneSubscriptionOptions>()
    let mutable processor: ServiceBusProcessor = null
    let mutable activeSubscriptionName: string = null

    // Called by the Service Bus processor for every incoming message.
    // We fan out to every registered local FusionCache handler EXCEPT the one
    // whose instance id matches the sourceId encoded in the message.
    let handleMessage (args: ProcessMessageEventArgs) =
        task {
            let body = args.Message.Body.ToString()
            let parts = body.Split(':', 3)
            if parts.Length >= 3 then
                let actionStr = parts.[0]
                let sourceId  = parts.[1]
                let cacheKey  = parts.[2]
                printfn "[Backplane] Received %s | key: %s | source: %s" actionStr cacheKey sourceId

                for KeyValue(instanceId, opts) in handlers do
                    // Don't echo back to the originating cache instance
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

            do! args.CompleteMessageAsync(args.Message)
        } :> Task

    let handleError (args: ProcessErrorEventArgs) =
        task {
            printfn "[Backplane] ServiceBus processor error: %s" args.Exception.Message
        } :> Task

    interface IFusionCacheBackplane with

        member this.Subscribe(options) =
            printfn "[Backplane] Subscribe (SYNC) called for instance: %s" options.CacheInstanceId
            (this :> IFusionCacheBackplane).SubscribeAsync(options).AsTask().Wait()

        member this.SubscribeAsync(options: BackplaneSubscriptionOptions) =
            task {
                handlers.[options.CacheInstanceId] <- options
                printfn "[Backplane] Registered local cache %s (total: %d)" options.CacheInstanceId handlers.Count

                // Start the processor only once for the whole process,
                // regardless of how many FusionCache instances subscribe.
                if isNull processor then
                    let! resolvedSubName =
                        task {
                            match adminClientOpt with
                            | Some adminClient ->
                                // Unique per-process subscription via admin API
                                let uniqueName = sprintf "%s-%s" subscriptionName (options.CacheInstanceId.Substring(0, 8))
                                let! exists = adminClient.SubscriptionExistsAsync(topicName, uniqueName)
                                if not exists.Value then
                                    let subOpts = CreateSubscriptionOptions(topicName, uniqueName)
                                    subOpts.AutoDeleteOnIdle <- TimeSpan.FromMinutes(5.0)
                                    let! _ = adminClient.CreateSubscriptionAsync(subOpts)
                                    ()
                                activeSubscriptionName <- uniqueName
                                return uniqueName
                            | None ->
                                // Emulator: use the fixed subscription name from config
                                activeSubscriptionName <- subscriptionName
                                return subscriptionName
                        }

                    printfn "[Backplane] Starting processor on subscription: %s" resolvedSubName
                    processor <- client.CreateProcessor(topicName, resolvedSubName)
                    processor.add_ProcessMessageAsync(Func<ProcessMessageEventArgs, Task>(handleMessage))
                    processor.add_ProcessErrorAsync(Func<ProcessErrorEventArgs, Task>(handleError))
                    do! processor.StartProcessingAsync()
                    printfn "[Backplane] Processor started."

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
                if not (isNull processor) then
                    do! processor.StopProcessingAsync()
                    do! processor.DisposeAsync()
                    processor <- null

                match adminClientOpt with
                | Some adminClient when not (isNull activeSubscriptionName) ->
                    try
                        let! _ = adminClient.DeleteSubscriptionAsync(topicName, activeSubscriptionName)
                        ()
                    with _ -> ()
                | _ -> ()
                activeSubscriptionName <- null
                handlers.Clear()
            } |> ValueTask

        member this.Publish(message, options, token) =
            printfn "[Backplane] Publish (SYNC) called for key: %s" message.CacheKey
            (this :> IFusionCacheBackplane).PublishAsync(message, options, token).AsTask().Wait()

        /// FusionCache calls this when a cache entry is updated or removed.
        /// message.SourceId is authoritative — FusionCache sets it to the calling
        /// cache's CacheInstanceId before invoking this method.
        member this.PublishAsync(message: BackplaneMessage, _options: FusionCacheEntryOptions, token: CancellationToken) =
            task {
                let actionStr =
                    match message.Action with
                    | BackplaneMessageAction.EntrySet    -> "EntrySet"
                    | BackplaneMessageAction.EntryRemove -> "EntryRemove"
                    | _                                  -> "Unknown"

                // Use message.SourceId — this is the CacheInstanceId set by FusionCache
                let payload = sprintf "%s:%s:%s" actionStr message.SourceId message.CacheKey
                printfn "[Backplane] Publishing %s | key: %s | source: %s" actionStr message.CacheKey message.SourceId
                let sbMsg = ServiceBusMessage(payload)
                do! sender.SendMessageAsync(sbMsg, token)
            } |> ValueTask
