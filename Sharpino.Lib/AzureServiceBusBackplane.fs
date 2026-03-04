namespace Sharpino

open System
open System.Text
open System.Threading
open System.Threading.Tasks
open Azure.Messaging.ServiceBus
open ZiggyCreatures.Caching.Fusion
open ZiggyCreatures.Caching.Fusion.Backplane

type AzureServiceBusBackplane(connectionString: string, topicName: string, subscriptionName: string) =
    let client = new ServiceBusClient(connectionString)
    let sender = client.CreateSender(topicName)
    let mutable processor: ServiceBusProcessor = null
    let mutable subscriptionOptions: BackplaneSubscriptionOptions = null

    let handleMessage (args: ProcessMessageEventArgs) =
        task {
            if not (isNull subscriptionOptions) then
                let body = args.Message.Body.ToString()
                let parts = body.Split(':', 3)
                if parts.Length >= 3 then
                    let actionStr = parts.[0]
                    let sourceId = parts.[1]
                    let cacheKey = parts.[2]

                    if sourceId <> subscriptionOptions.CacheInstanceId then
                        let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        let msg = 
                            match actionStr with
                            | "EntrySet" -> BackplaneMessage.CreateForEntrySet(sourceId, cacheKey, timestamp)
                            | "EntryRemove" -> BackplaneMessage.CreateForEntryRemove(sourceId, cacheKey, timestamp)
                            | _ -> null

                        if not (isNull msg) then
                            if not (isNull subscriptionOptions.IncomingMessageHandlerAsync) then
                                let! _ = subscriptionOptions.IncomingMessageHandlerAsync.Invoke(msg)
                                ()
                            elif not (isNull subscriptionOptions.IncomingMessageHandler) then
                                subscriptionOptions.IncomingMessageHandler.Invoke(msg)
            
            do! args.CompleteMessageAsync(args.Message)
        } :> Task

    let handleError (args: ProcessErrorEventArgs) =
        task {
            // Log error if possible
            ()
        } :> Task

    interface IFusionCacheBackplane with
        member this.Subscribe(options: BackplaneSubscriptionOptions) =
            (this :> IFusionCacheBackplane).SubscribeAsync(options).AsTask().Wait()

        member this.SubscribeAsync(options: BackplaneSubscriptionOptions) =
            task {
                subscriptionOptions <- options
                processor <- client.CreateProcessor(topicName, subscriptionName)
                processor.add_ProcessMessageAsync(Func<ProcessMessageEventArgs, Task>(handleMessage))
                processor.add_ProcessErrorAsync(Func<ProcessErrorEventArgs, Task>(handleError))
                
                do! processor.StartProcessingAsync()

                if not (isNull options.ConnectHandlerAsync) then
                    let! _ = options.ConnectHandlerAsync.Invoke(new BackplaneConnectionInfo(false))
                    ()
                elif not (isNull options.ConnectHandler) then
                    options.ConnectHandler.Invoke(new BackplaneConnectionInfo(false))
            } |> ValueTask

        member this.Unsubscribe() =
            (this :> IFusionCacheBackplane).UnsubscribeAsync().AsTask().Wait()

        member this.UnsubscribeAsync() =
            task {
                if not (isNull processor) then
                    do! processor.StopProcessingAsync()
                    do! processor.DisposeAsync()
                    processor <- null
                subscriptionOptions <- null
            } |> ValueTask

        member this.Publish(message: BackplaneMessage, options: FusionCacheEntryOptions, token: CancellationToken) =
            (this :> IFusionCacheBackplane).PublishAsync(message, options, token).AsTask().Wait()

        member this.PublishAsync(message: BackplaneMessage, options: FusionCacheEntryOptions, token: CancellationToken) =
            task {
                let actionStr = 
                    match message.Action with
                    | BackplaneMessageAction.EntrySet -> "EntrySet"
                    | BackplaneMessageAction.EntryRemove -> "EntryRemove"
                    | _ -> "Unknown"
                
                let payload = sprintf "%s:%s:%s" actionStr subscriptionOptions.CacheInstanceId message.CacheKey
                let sbMessage = new ServiceBusMessage(payload)
                do! sender.SendMessageAsync(sbMessage, token)
            } |> ValueTask
