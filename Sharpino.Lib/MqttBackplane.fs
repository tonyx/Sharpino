namespace Sharpino

open System
open System.Text
open System.Threading
open System.Threading.Tasks
open System.Buffers
open MQTTnet
open ZiggyCreatures.Caching.Fusion.Backplane
open ZiggyCreatures.Caching.Fusion


// TODO: the MQTT backplane is replaced by Service Bus backplane. This code is kept just for reference.
type MqttBackplane(mqttOptions: MQTTnet.MqttClientOptions, topicPrefix: string) =
    let factory = MQTTnet.MqttClientFactory()
    let mqttClient = factory.CreateMqttClient()
    let mutable subscriptionOptions: BackplaneSubscriptionOptions = null
    let mutable channelName = ""

    let handleIncomingMessage (e: MqttApplicationMessageReceivedEventArgs) =
        task {
            if not (isNull subscriptionOptions) then
                let applicationMessage = e.ApplicationMessage
                // Use the ReadOnlySequence extension method ToArray()
                let payloadBytes = applicationMessage.Payload.ToArray()
                let payload = Encoding.UTF8.GetString(payloadBytes)
                let parts: string array = payload.Split(':', 3)
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
        } :> Task

    do
        mqttClient.add_ApplicationMessageReceivedAsync(Func<MqttApplicationMessageReceivedEventArgs, Task>(handleIncomingMessage))

    interface IFusionCacheBackplane with
        member this.Subscribe(options: BackplaneSubscriptionOptions) =
            (this :> IFusionCacheBackplane).SubscribeAsync(options).AsTask().Wait()

        member this.SubscribeAsync(options: BackplaneSubscriptionOptions) =
            task {
                subscriptionOptions <- options
                let safeChannelName = options.ChannelName.Replace(":", "_").Replace("/", "_")
                channelName <- sprintf "%s/%s" topicPrefix safeChannelName

                if not mqttClient.IsConnected then
                    let! _ = mqttClient.ConnectAsync(mqttOptions, CancellationToken.None)
                    ()
                
                let mqttSubscribeOptions = 
                    factory.CreateSubscribeOptionsBuilder()
                        .WithTopicFilter(fun f -> f.WithTopic(channelName) |> ignore)
                        .Build()
                let! _ = mqttClient.SubscribeAsync(mqttSubscribeOptions, CancellationToken.None)

                // Notify FusionCache
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
                if mqttClient.IsConnected then
                    let! _ = mqttClient.DisconnectAsync()
                    ()
                subscriptionOptions <- null
            } |> ValueTask

        member this.Publish(message: BackplaneMessage, options: FusionCacheEntryOptions, token: CancellationToken) =
            (this :> IFusionCacheBackplane).PublishAsync(message, options, token).AsTask().Wait()

        member this.PublishAsync(message: BackplaneMessage, options: FusionCacheEntryOptions, token: CancellationToken) =
            task {
                if mqttClient.IsConnected then
                    // Message format: Action:SourceId:CacheKey
                    let actionStr = 
                        match message.Action with
                        | BackplaneMessageAction.EntrySet -> "EntrySet"
                        | BackplaneMessageAction.EntryRemove -> "EntryRemove"
                        | _ -> "Unknown"
                    
                    let payload = sprintf "%s:%s:%s" actionStr subscriptionOptions.CacheInstanceId message.CacheKey
                    let mqttMsg = 
                        MqttApplicationMessageBuilder()
                            .WithTopic(channelName)
                            .WithPayload(payload)
                            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce)
                            .Build()
                    let! _ = mqttClient.PublishAsync(mqttMsg, token)
                    ()
            } |> ValueTask
