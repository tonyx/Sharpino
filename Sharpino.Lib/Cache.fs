namespace Sharpino

open System.Collections.Concurrent
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Caching.Memory
open ZiggyCreatures.Caching.Fusion
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Hosting
open Sharpino
open Sharpino.Core
open Sharpino.Definitions
open System.Runtime.CompilerServices
open Microsoft.Extensions.Logging.Abstractions
open System.Collections
open FSharp.Core
open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Caching.Distributed
open ZiggyCreatures.Caching.Fusion.Backplane
open ZiggyCreatures.Caching.Fusion.Serialization

open Microsoft.Extensions.Caching.SqlServer
open Microsoft.Extensions.Options
open ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson
open System.Text.Json
open System.Text.Json.Serialization
open MQTTnet

module Cache =
    let builder = Host.CreateApplicationBuilder()
    let env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
    
    builder.Configuration
        .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
        .AddJsonFile("appSettings.json", optional=false, reloadOnChange=true) |> ignore
    
    if not (String.IsNullOrWhiteSpace env) then
        builder.Configuration.AddJsonFile($"appSettings.{env}.json", optional=true) |> ignore
        
    builder.Configuration.AddEnvironmentVariables() |> ignore
    let config = builder.Configuration
        
    let numProcs = Environment.ProcessorCount
    let concurrencyLevel = numProcs * 2

    let loggerFactory = LoggerFactory.Create(fun b ->
        if config.GetValue<bool>("Logging:Console", true) then
            b.AddConsole() |> ignore
        )
    let logger = loggerFactory.CreateLogger("Sharpino.Cache")

    let jsonOptions = JsonFSharpOptions.Default().ToJsonSerializerOptions()
    let serializer = new FusionCacheSystemTextJsonSerializer(jsonOptions)
    
    let setLogger (newLogger: Microsoft.Extensions.Logging.ILogger) =
        logger.LogError ("setting logger is not supported")
   
    type Refreshable<'A> =
        abstract member Refresh: unit -> Result<'A, string>
   
    type DetailsCacheKey =
        | DetailsCacheKey of string * Guid  // string = type name (not System.Type, for JSON-serializability)
        with
            member this.Value =
                match this with
                | DetailsCacheKey (typeName, id) -> sprintf "%s:%A" typeName id
            static member OfType (t: Type) (id: Guid) =
                DetailsCacheKey (t.Name, id)
        
    type DetailsCache private () =
        let ignoreIncomingBackplane = config.GetValue<bool>("Cache:IgnoreIncomingBackplaneNotifications", false)
        let detailsOptions = FusionCacheOptions(
            CacheName = "statesDetails",
            CacheKeyPrefix = "statesDetails:",
            IgnoreIncomingBackplaneNotifications = ignoreIncomingBackplane
        )
        let statesDetails = new FusionCache(detailsOptions)
            
        let detailsCacheExpirationConfigInSeconds = config.GetValue<float>("DetailsCacheExpiration", 300)
        let detailsCacheDependenciesExpirationConfigInSeconds = config.GetValue<float>("DetailsCacheDependenciesExpiration", 301)
        let l2CacheExpirationConfigInSeconds = config.GetValue<float>("Cache:L2CacheExpirationSeconds", 120)
        
        let detailsEntryOptions =
            FusionCacheEntryOptions().
                SetDuration(TimeSpan.FromSeconds(detailsCacheExpirationConfigInSeconds))
        
        let detailsDependenciesEntryOptions =
            // L2 TTL is set separately and shorter than L1 to avoid stale entries polluting L1 on restarts
            let opts = FusionCacheEntryOptions().
                           SetDuration(TimeSpan.FromSeconds(detailsCacheDependenciesExpirationConfigInSeconds))
            opts.DistributedCacheDuration <- System.Nullable(TimeSpan.FromSeconds(l2CacheExpirationConfigInSeconds))
            opts

        let assocOptions = FusionCacheOptions(
            CacheName = "objectDetails",
            CacheKeyPrefix = "objectDetails:",
            IgnoreIncomingBackplaneNotifications = ignoreIncomingBackplane
        )
        let objectDetailsAssociationsCache = new FusionCache(assocOptions)
        
        let mutable _backplane: IFusionCacheBackplane option = None
        
        static let instance = DetailsCache ()
        static member Instance = instance
        
        member this.SetupL2AndBackplane(dc: IDistributedCache option, ser: IFusionCacheSerializer option, bp: IFusionCacheBackplane option) =
            if dc.IsSome && ser.IsSome then
                // NOTE: statesDetails intentionally does NOT use L2/SQL cache.
                // It stores Refreshable<'T> wrappers which contain live closures and
                // are fundamentally non-serializable (they carry System.Type references).
                // If we wire statesDetails to L2, System.Text.Json will throw
                // "Serialization of System.Type is not supported" on the first write.
                // Only objectDetailsAssociationsCache (which stores plain List<DetailsCacheKey>)
                // is safe to persist in L2.
                (objectDetailsAssociationsCache :> IFusionCache).SetupDistributedCache(dc.Value, ser.Value) |> ignore
            if bp.IsSome then
                let backplane = bp.Value
                _backplane <- Some backplane
                (statesDetails :> IFusionCache).SetupBackplane(backplane) |> ignore
                (objectDetailsAssociationsCache :> IFusionCache).SetupBackplane(backplane) |> ignore
                
                // FusionCache only auto-subscribes when managed by DI/IHostedService.
                // Since we instantiate it directly, we manually trigger the internal Subscribe() using reflection.
                let activateBackplane (fc: IFusionCache) =
                    let bpaProp = fc.GetType().GetProperty("BackplaneAccessor", System.Reflection.BindingFlags.Instance ||| System.Reflection.BindingFlags.NonPublic)
                    if not (isNull bpaProp) then
                        let bpa = bpaProp.GetValue(fc)
                        if not (isNull bpa) then
                            let subMethod = bpa.GetType().GetMethod("Subscribe", System.Reflection.BindingFlags.Instance ||| System.Reflection.BindingFlags.Public ||| System.Reflection.BindingFlags.NonPublic)
                            if not (isNull subMethod) then
                                subMethod.Invoke(bpa, [||]) |> ignore
                
                activateBackplane (statesDetails :> IFusionCache)
                activateBackplane (objectDetailsAssociationsCache :> IFusionCache)
                
                // Manually invalidate L1 cache when receiving backplane messages
                let receiverOptions = ZiggyCreatures.Caching.Fusion.FusionCacheEntryOptions().SetSkipBackplaneNotifications(true)
                
                statesDetails.Events.Backplane.add_MessageReceived(System.EventHandler<ZiggyCreatures.Caching.Fusion.Events.FusionCacheBackplaneMessageEventArgs>(fun sender e ->
                    if e.Message.SourceId <> statesDetails.InstanceId then
                        if e.Message.Action = ZiggyCreatures.Caching.Fusion.Backplane.BackplaneMessageAction.EntryRemove || e.Message.Action = ZiggyCreatures.Caching.Fusion.Backplane.BackplaneMessageAction.EntrySet then
                            let prefix = "statesDetails:"
                            if e.Message.CacheKey.StartsWith(prefix) then
                                let key = e.Message.CacheKey.Substring(prefix.Length)
                                statesDetails.Remove(key, receiverOptions)
                                printfn "[Cache Event] DetailsCache manually removed L1 entry for %s" key
                ))
                
                objectDetailsAssociationsCache.Events.Backplane.add_MessageReceived(System.EventHandler<ZiggyCreatures.Caching.Fusion.Events.FusionCacheBackplaneMessageEventArgs>(fun sender e ->
                    if e.Message.SourceId <> objectDetailsAssociationsCache.InstanceId then
                        if e.Message.Action = ZiggyCreatures.Caching.Fusion.Backplane.BackplaneMessageAction.EntryRemove || e.Message.Action = ZiggyCreatures.Caching.Fusion.Backplane.BackplaneMessageAction.EntrySet then
                            let prefix = "objectDetails:"
                            if e.Message.CacheKey.StartsWith(prefix) then
                                let key = e.Message.CacheKey.Substring(prefix.Length)
                                objectDetailsAssociationsCache.Remove(key, receiverOptions)
                                printfn "[Cache Event] DetailsCache(Associations) manually removed L1 entry for %s" key
                ))
            ()
            
        member this.UpdateMultipleAggregateIdAssociation (aggregateIds: AggregateId[]) (key: DetailsCacheKey) =
            for aggregateId in aggregateIds do
                let existingKeys = objectDetailsAssociationsCache.GetOrDefault<List<DetailsCacheKey>>(aggregateId.ToString(), Unchecked.defaultof<List<DetailsCacheKey>>)
                let updatedKeys = 
                    if isNull (box existingKeys) then
                        [key]
                    elif not (List.contains key existingKeys) then
                        key :: existingKeys
                    else
                        existingKeys
                objectDetailsAssociationsCache.Set(aggregateId.ToString(), updatedKeys, detailsDependenciesEntryOptions)
            ()
        
        member this.Refresh (key: DetailsCacheKey) =
            let v = statesDetails.GetOrDefault<obj>(key.Value, null)
            if (obj.ReferenceEquals(v, null)) then
                Error "not found"
            else 
                let interfaces = v.GetType().GetInterfaces()
                let refreshableInterface = interfaces |> Array.tryFind (fun i -> i.IsGenericType && i.GetGenericTypeDefinition() = typedefof<Refreshable<_>>)
                
                if not (obj.ReferenceEquals(v, null)) then
                    match refreshableInterface with
                    | Some _ ->
                        let refreshMethod = v.GetType().GetMethod("Refresh")
                        let refreshed = refreshMethod.Invoke(v, [||])
                        let resultType = refreshableInterface.Value.GetGenericArguments().[0]
                        let result = 
                            try
                                // Create a Result<obj, string> from the refreshed object
                                let resultType = typedefof<Result<_, _>>.MakeGenericType([| resultType; typeof<string> |])
                                let okValue = resultType.GetProperty("IsOk").GetValue(refreshed)
                                if unbox<bool> okValue then
                                    let value = resultType.GetProperty("ResultValue").GetValue(refreshed)
                                    Ok value
                                else
                                    // I assume that if an element is not refreshable anymore it means that it should be evicted
                                    this.Evict key
                                    let error = resultType.GetProperty("ErrorValue").GetValue(refreshed) :?> string
                                    Error error
                            with ex ->
                                Error (sprintf "Error processing refresh result: %s" ex.Message)
                        
                        match result with
                        | Ok resultObj ->
                            // Update the cache directly without going through TryCache
                            try
                                let keyStr = key.Value
                                statesDetails.Set<obj>(keyStr, resultObj, detailsEntryOptions)
                                if _backplane.IsSome then
                                    let fullKey = "statesDetails:" + keyStr
                                    let msg = ZiggyCreatures.Caching.Fusion.Backplane.BackplaneMessage.CreateForEntrySet(
                                        statesDetails.InstanceId, 
                                        fullKey, 
                                        System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                                    )
                                    _backplane.Value.PublishAsync(msg, detailsEntryOptions, System.Threading.CancellationToken.None).AsTask() |> ignore
                                Ok resultObj
                            with e ->
                                logger.LogError (sprintf "error: cache update failed. %A\n" e)
                                statesDetails.Clear()
                                Error "Failed to update cache"
                        | Error e -> Error e
                    | _ -> Error "Object does not implement Refreshable interface"
                else
                    Error "not found"
                
        
        member this.Evict (key: DetailsCacheKey)  =
            let keyStr = key.Value
            statesDetails.Remove keyStr
            if _backplane.IsSome then
                let fullKey = "statesDetails:" + keyStr
                let msg = ZiggyCreatures.Caching.Fusion.Backplane.BackplaneMessage.CreateForEntryRemove(
                    statesDetails.InstanceId, 
                    fullKey, 
                    System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                )
                _backplane.Value.PublishAsync(msg, detailsEntryOptions, System.Threading.CancellationToken.None).AsTask() |> ignore
        
        member this.RefreshDependentDetails (aggregateId: AggregateId) =
            let keys = objectDetailsAssociationsCache.GetOrDefault<List<DetailsCacheKey>>(aggregateId.ToString(), Unchecked.defaultof<List<DetailsCacheKey>>)
            if not (obj.ReferenceEquals(keys, null)) then
                for key in keys do
                    let refreshed =
                        this.Refresh key
                    ()    
            ()
        
        member this.evictDependentDetails (aggregateId: AggregateId) =
            let keys = objectDetailsAssociationsCache.GetOrDefault<List<DetailsCacheKey>>(aggregateId.ToString(), Unchecked.defaultof<List<DetailsCacheKey>>)
            if not (obj.ReferenceEquals(keys, null)) then
                for key in keys do
                    this.Evict key
                ()
        
        member private this.TryCache (key: string, value: Refreshable<_>) =
            try
                statesDetails.Set<obj>(key, value, detailsEntryOptions)
                if _backplane.IsSome then
                    let fullKey = "statesDetails:" + key
                    let msg = ZiggyCreatures.Caching.Fusion.Backplane.BackplaneMessage.CreateForEntrySet(
                        statesDetails.InstanceId, 
                        fullKey, 
                        System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    )
                    _backplane.Value.PublishAsync(msg, detailsEntryOptions, System.Threading.CancellationToken.None).AsTask() |> ignore
            with e ->
                logger.LogError (sprintf "error: cache is doing something wrong. Resetting. %A\n" e)
                statesDetails.Clear()
                ()
                    
        member this.Memoize (f: unit -> Result<Refreshable<_>*List<AggregateId>, string>) (key: DetailsCacheKey) =
            let v = statesDetails.GetOrDefault<obj>(key.Value, null)
            if not (obj.ReferenceEquals(v, null)) then
                v |> Ok
            else
                let res = f()
                match res with
                | Ok (result, dependendIds) ->
                    this.TryCache (key.Value, result)
                    this.UpdateMultipleAggregateIdAssociation (dependendIds |> List.toArray) key 
                    Ok (result |> unbox)
                | Error e ->
                    Error e
        
        member this.Clear () =
            statesDetails.Clear()
            objectDetailsAssociationsCache.Clear()

        member this.ClearL1 () =
            statesDetails.Clear(true)
            objectDetailsAssociationsCache.Clear(true)

        member this.ClearL2 () =
            statesDetails.Clear(false)
            objectDetailsAssociationsCache.Clear(false)
    
    type AggregateCache3 private () =
        let ignoreIncomingBackplane = config.GetValue<bool>("Cache:IgnoreIncomingBackplaneNotifications", false)
        let aggregateOptions = FusionCacheOptions(
            CacheName = "statePerAggregate",
            CacheKeyPrefix = "statePerAggregate:",
            IgnoreIncomingBackplaneNotifications = ignoreIncomingBackplane
        )
        let statePerAggregate = new FusionCache(aggregateOptions)
        let cacheExpirationConfigInSeconds = config.GetValue<float>("AggregateCacheExpiration", 600)
        let l2CacheExpirationConfigInSeconds = config.GetValue<float>("Cache:L2CacheExpirationSeconds", 120)
        let entryOptions =
            // L2 TTL is shorter than L1 to prevent stale aggregate states from polluting L1 on node restarts
            let opts = FusionCacheEntryOptions().
                           SetDuration(TimeSpan.FromSeconds(cacheExpirationConfigInSeconds))
            opts.DistributedCacheDuration <- System.Nullable(TimeSpan.FromSeconds(l2CacheExpirationConfigInSeconds))
            opts
        
        let mutable _backplane: IFusionCacheBackplane option = None

        static let instance = AggregateCache3()
        static member Instance = instance
        
        member this.SetupL2AndBackplane(dc: IDistributedCache option, ser: IFusionCacheSerializer option, bp: IFusionCacheBackplane option) =
            if dc.IsSome && ser.IsSome then
                (statePerAggregate :> IFusionCache).SetupDistributedCache(dc.Value, ser.Value) |> ignore
            if bp.IsSome then
                let backplane = bp.Value
                _backplane <- Some backplane
                (statePerAggregate :> IFusionCache).SetupBackplane(backplane) |> ignore
                
                // Activate backplane manually via reflection
                let bpaProp = statePerAggregate.GetType().GetProperty("BackplaneAccessor", System.Reflection.BindingFlags.Instance ||| System.Reflection.BindingFlags.NonPublic)
                if not (isNull bpaProp) then
                    let bpa = bpaProp.GetValue(statePerAggregate)
                    if not (isNull bpa) then
                        let subMethod = bpa.GetType().GetMethod("Subscribe", System.Reflection.BindingFlags.Instance ||| System.Reflection.BindingFlags.Public ||| System.Reflection.BindingFlags.NonPublic)
                        if not (isNull subMethod) then
                            subMethod.Invoke(bpa, [||]) |> ignore
                            logger.LogInformation (sprintf "[Cache] AggregateCache3: HasBackplane = %A" statePerAggregate.HasBackplane)
                            
                            let usableMethod = bpa.GetType().GetMethod("IsCurrentlyUsable", System.Reflection.BindingFlags.Instance ||| System.Reflection.BindingFlags.Public ||| System.Reflection.BindingFlags.NonPublic)
                            if not (isNull usableMethod) then
                                logger.LogInformation (sprintf "[Cache] AggregateCache3: IsCurrentlyUsable = %A" (usableMethod.Invoke(bpa, [| null; null |])))
                            logger.LogInformation (sprintf "[Cache] AggregateCache3: SkipBackplane = %A" entryOptions.SkipBackplaneNotifications)
                
                // Add event listeners
                let receiverOptions = ZiggyCreatures.Caching.Fusion.FusionCacheEntryOptions().SetSkipBackplaneNotifications(true)
                statePerAggregate.Events.Backplane.add_MessagePublished(System.EventHandler<ZiggyCreatures.Caching.Fusion.Events.FusionCacheBackplaneMessageEventArgs>(fun sender e ->
                    logger.LogDebug (sprintf "[Cache Event] MessagePublished: Action=%A, Key=%s, SourceId=%s" e.Message.Action e.Message.CacheKey e.Message.SourceId)
                ))
                statePerAggregate.Events.Backplane.add_MessageReceived(System.EventHandler<ZiggyCreatures.Caching.Fusion.Events.FusionCacheBackplaneMessageEventArgs>(fun sender e ->
                    logger.LogDebug (sprintf "[Cache Event] MessageReceived: Action=%A, Key=%s, SourceId=%s" e.Message.Action e.Message.CacheKey e.Message.SourceId)
                    // Manually invalidate L1 cache
                    if e.Message.SourceId <> statePerAggregate.InstanceId then
                        if e.Message.Action = ZiggyCreatures.Caching.Fusion.Backplane.BackplaneMessageAction.EntryRemove || e.Message.Action = ZiggyCreatures.Caching.Fusion.Backplane.BackplaneMessageAction.EntrySet then
                            let prefix = "statePerAggregate:"
                            if e.Message.CacheKey.StartsWith(prefix) then
                                let key = e.Message.CacheKey.Substring(prefix.Length)
                                statePerAggregate.Remove(key, receiverOptions)

                                let (isGuid, guidKey) = Guid.TryParse(key)
                                if isGuid then
                                    DetailsCache.Instance.RefreshDependentDetails(guidKey)
                                else
                                    logger.LogWarning (sprintf "[Cache Event] AggregateCache3: Could not parse Guid from key %s" key)
                                logger.LogDebug (sprintf "[Cache Event] AggregateCache3 manually removed L1 entry for %s" key)
                ))
            ()

        member private this.TryCache (aggregateId, eventId: EventId, resultState: obj) =
            try
                let key = aggregateId.ToString()
                statePerAggregate.Set<(EventId * obj)>(key, (eventId, resultState), entryOptions)
                
                // Manually notify backplane if FusionCache skips it
                if _backplane.IsSome then
                    let fullKey = "statePerAggregate:" + key
                    let msg = ZiggyCreatures.Caching.Fusion.Backplane.BackplaneMessage.CreateForEntrySet(
                        statePerAggregate.InstanceId, 
                        fullKey, 
                        System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    )
                    _backplane.Value.PublishAsync(msg, entryOptions, System.Threading.CancellationToken.None).AsTask() |> ignore
            with e -> 
                logger.LogError (sprintf "error: cache is doing something wrong. Resetting. %A\n" e)
                statePerAggregate.Clear()
                DetailsCache.Instance.Clear()
                () 
   
        member this.Memoize (f: unit -> Result<EventId * obj, string>) (aggregateId: AggregateId): Result<EventId * obj, string> =
            let v = statePerAggregate.GetOrDefault<(EventId * obj)>(aggregateId.ToString(), null)
            if not (obj.ReferenceEquals(v, null)) then
                v |> Ok
            else
                let res = f()
                match res with
                | Ok (eventId, state) ->
                    this.TryCache (aggregateId, eventId, state)
                    Ok (eventId, state)
                | Error e ->
                    Error e

        member this.MemoizeAsync (f: Option<CancellationToken> -> Task<Result<EventId * obj, string>>) (aggregateId: AggregateId) (ct: Option<CancellationToken>): Task<Result<EventId * obj, string>> =
            let ct = ct |> Option.defaultValue CancellationToken.None
            task {
                let key = aggregateId.ToString()
                let! v = statePerAggregate.GetOrDefaultAsync<(EventId * obj)>(key, token = ct)
                if not (obj.ReferenceEquals(v, null)) then
                    return v |> Ok
                else
                    let! res = f (Some ct)
                    match res with
                    | Ok (eventId, state) ->
                        this.TryCache (aggregateId, eventId, state)
                        return Ok (eventId, state)
                    | Error e ->
                        return Error e
            }
              
        member this.Memoize2 (eventId: EventId, x:'A) (aggregateId: AggregateId) =
            this.Clean aggregateId
            this.TryCache (aggregateId, eventId, x)
        
        member this.Clean (aggregateId: AggregateId)  =
            let key = aggregateId.ToString()
            statePerAggregate.Remove(key)
            if _backplane.IsSome then
                let fullKey = "statePerAggregate:" + key
                let msg = ZiggyCreatures.Caching.Fusion.Backplane.BackplaneMessage.CreateForEntryRemove(
                    statePerAggregate.InstanceId, 
                    fullKey, 
                    System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                )
                _backplane.Value.PublishAsync(msg, entryOptions, System.Threading.CancellationToken.None).AsTask() |> ignore
        
        member this.Clear () =
            statePerAggregate.Clear()

        member this.ClearL1 () =
            statePerAggregate.Clear(true)

        member this.ClearL2 () =
            statePerAggregate.Clear(false)
        
        member this.LastEventId (aggregateId: AggregateId) =
            let v = statePerAggregate.GetOrDefault<(EventId * obj)>(aggregateId.ToString(), null)
            if not (obj.ReferenceEquals(v, null)) then v |> fst |> Some else None
        
        member this.GetState (aggregateId: AggregateId) =
            let v = statePerAggregate.GetOrDefault<(EventId * obj)>(aggregateId.ToString(), null)
            if not (obj.ReferenceEquals(v, null)) then v |> snd |> Ok else Error "aggregate not found"        
     
    type StateCache2<'A> private () =
        let mutable cachedValue: 'A option = None
        let mutable eventId: EventId = 0
        static let instance = StateCache2<'A>()
        static member Instance = instance
           
        [<MethodImpl(MethodImplOptions.Synchronized)>]
        member this.TryCache (res: 'A, evId: EventId) =
            cachedValue <- Some res
            eventId <- evId
            ()
               
        member this.GetState() =
            match cachedValue with
            | Some res -> Ok res
            | None -> Error "context state not found"
   
        member this.Memoize (f: unit -> Result<'A, string>)  (eventId: EventId)=
            match cachedValue with
            | Some res -> Ok res
            | _ ->
                let res = f()
                match res with
                | Ok result ->
                    let _  = this.TryCache (result, eventId)
                    Ok result
                | Error e ->
                    Error (e.ToString())
        member this.GetEventIdAndState () =
            match cachedValue with
            | Some res -> Some (eventId, res)
            | None -> None
            
        member this.Memoize2 (x: 'A) (eventId: EventId) =
            this.TryCache (x, eventId)
       
        member this.LastEventId() =
            eventId

        [<MethodImpl(MethodImplOptions.Synchronized)>]      
        member this.Invalidate() =
            cachedValue <- None         
            eventId <- 0
            ()

    let setupSecondLevelCacheAndBackplane 
        (distributedCache: IDistributedCache option) 
        (serializer: IFusionCacheSerializer option) 
        (backplane: IFusionCacheBackplane option) =
        DetailsCache.Instance.SetupL2AndBackplane(distributedCache, serializer, backplane)
        AggregateCache3.Instance.SetupL2AndBackplane(distributedCache, serializer, backplane)

    let setupAzureSqlCache (connectionString: string) (schemaName: string) (tableName: string) =
        let options = SqlServerCacheOptions(
            ConnectionString = connectionString,
            SchemaName = schemaName,
            TableName = tableName
        )
        let opts = Options.Create(options)
        let sqlCache = new SqlServerCache(opts)
        
        setupSecondLevelCacheAndBackplane (Some (sqlCache :> IDistributedCache)) (Some (serializer :> IFusionCacheSerializer)) None

    do // initialize L2 cache in azure Sql mode
        let l2SqlCacheEnabled = config.GetValue<bool>("Cache:L2SqlCacheEnabled", false)
        logger.LogInformation (sprintf "[Cache] Config: L2SqlCacheEnabled = %b" l2SqlCacheEnabled)
        if l2SqlCacheEnabled then
            let l2CacheSqlUrl = config.GetValue<string>("Cache:L2CacheSqlUrl", String.Empty)
            let l2CacheSqlTableName = config.GetValue<string>("Cache:L2CacheSqlTableName", String.Empty)
            match l2CacheSqlUrl, l2CacheSqlTableName with
            | "", _ -> logger.LogCritical ("[Cache] Error: L2CacheSqlUrl is empty")
            | _, "" -> logger.LogCritical ("[Cache] Error: L2CacheSqlTableName is empty")
            | _ ->
                logger.LogInformation (sprintf "[Cache] Initializing L2 SQL Cache with table: %s" l2CacheSqlTableName)
                setupAzureSqlCache l2CacheSqlUrl "dbo" l2CacheSqlTableName |> ignore
                logger.LogInformation (sprintf "[Cache] L2 SQL Cache initialized.")
        else 
            ()

    let setupEventGridMqttOptions (hostname: string) (port: int) (clientId: string) (username: string) (password: string) =
        MqttClientOptionsBuilder()
            .WithTcpServer(hostname, port)
            .WithCredentials(username, password)
            .WithClientId(clientId)
            .WithTlsOptions(fun o -> o.UseTls() |> ignore)
            .Build()

    let setupAzureServiceBusBackplane (connectionString: string) (topicName: string) (subscriptionName: string) (managementConnectionString: string option) =
        let bp =
            match managementConnectionString with
            | Some mcs -> new AzureServiceBusBackplane(connectionString, topicName, subscriptionName, mcs)
            | None     -> new AzureServiceBusBackplane(connectionString, topicName, subscriptionName)
        bp :> IFusionCacheBackplane

    do // initialize backplane in Service Bus
        let backplaneEnabled = config.GetValue<bool>("Cache:L2ServiceBusEnabled", false)
        if backplaneEnabled then
            printfn "[Cache] Initializing Service Bus Backplane..."
            let serviceBusConnectionString = config.GetValue<string>("Cache:ServiceBusConnectionString", String.Empty)
            let serviceBusTopicName = config.GetValue<string>("Cache:ServiceBusTopicName", String.Empty)
            let serviceBusSubscriptionName = config.GetValue<string>("Cache:ServiceBusSubscriptionName", String.Empty)
            match serviceBusConnectionString, serviceBusTopicName, serviceBusSubscriptionName with
            | "", _, _ -> logger.LogCritical "[Cache] Error: ServiceBusConnectionString is empty"
            | _, "", _ -> logger.LogCritical "[Cache] Error: ServiceBusTopicName is empty"
            | _, _, "" -> logger.LogCritical "[Cache] Error: ServiceBusSubscriptionName is empty"
            | _ ->
                let mgmtUrl = config.GetValue<string>("Cache:ServiceBusManagementConnectionString", String.Empty)
                let mgmtOpt = if String.IsNullOrWhiteSpace mgmtUrl then None else Some mgmtUrl
                let bp = setupAzureServiceBusBackplane serviceBusConnectionString serviceBusTopicName serviceBusSubscriptionName mgmtOpt
                setupSecondLevelCacheAndBackplane None None (Some bp)
                logger.LogCritical (sprintf "[Cache] Service Bus Backplane initialized (Subscription: %s)" serviceBusSubscriptionName)
        else
            logger.LogInformation "[Cache] Service Bus Backplane is disabled."

    let setupMqttBackplane (options: MqttClientOptions) (topicPrefix: string) =
        let bp = new MqttBackplane(options, topicPrefix)
        bp :> IFusionCacheBackplane