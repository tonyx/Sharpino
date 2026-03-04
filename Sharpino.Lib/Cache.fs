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
open Microsoft.Extensions.Caching.Distributed
open ZiggyCreatures.Caching.Fusion.Backplane
open ZiggyCreatures.Caching.Fusion.Serialization

open Microsoft.Extensions.Caching.SqlServer
open Microsoft.Extensions.Options
open ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson
open System.Text.Json
open System.Text.Json.Serialization

module Cache =
    let builder = Host.CreateApplicationBuilder()
    let config = builder.Configuration
        
    let numProcs = Environment.ProcessorCount
    let concurrencyLevel = numProcs * 2

    let loggerFactory = LoggerFactory.Create(fun b ->
        if config.GetValue<bool>("Logging:Console", true) then
            b.AddConsole() |> ignore
        )
    let logger = loggerFactory.CreateLogger("Sharpino.CommandHandler")
    
    let setLogger (newLogger: Microsoft.Extensions.Logging.ILogger) =
        logger.LogError ("setting logger is not supported")
   
    type Refreshable<'A> =
        abstract member Refresh: unit -> Result<'A, string>
   
    type DetailsCacheKey =
        | DetailsCacheKey of Type * Guid
        with
            member this.Value =
                match this with
                | DetailsCacheKey (t, id) -> sprintf "%s:%A" t.Name id
        
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
        
        let detailsEntryOptions =
            FusionCacheEntryOptions().
                SetDuration(TimeSpan.FromSeconds(detailsCacheExpirationConfigInSeconds))
        
        let detailsDependenciesEntryOptions =
            FusionCacheEntryOptions().
                SetDuration(TimeSpan.FromSeconds(detailsCacheDependenciesExpirationConfigInSeconds))        
        
        let assocOptions = FusionCacheOptions(
            CacheName = "objectDetails",
            CacheKeyPrefix = "objectDetails:",
            IgnoreIncomingBackplaneNotifications = ignoreIncomingBackplane
        )
        let objectDetailsAssociationsCache = new FusionCache(assocOptions)
        
        static let instance = DetailsCache ()
        static member Instance = instance
        
        member this.SetupL2AndBackplane(dc: IDistributedCache option, ser: IFusionCacheSerializer option, bp: IFusionCacheBackplane option) =
            if dc.IsSome && ser.IsSome then
                (statesDetails :> IFusionCache).SetupDistributedCache(dc.Value, ser.Value) |> ignore
                (objectDetailsAssociationsCache :> IFusionCache).SetupDistributedCache(dc.Value, ser.Value) |> ignore
            if bp.IsSome then
                (statesDetails :> IFusionCache).SetupBackplane(bp.Value) |> ignore
                (objectDetailsAssociationsCache :> IFusionCache).SetupBackplane(bp.Value) |> ignore
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
            
        // will be deprecated as passing an expiration is a risk (or at least it should be greater than the default)
        member this.UpdateMultipleAggregateIdAssociationRef (aggregateIds: AggregateId[]) (key: DetailsCacheKey) (expiration: TimeSpan option)=
            
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
                                statesDetails.Set<obj>(key.Value, resultObj, detailsEntryOptions)
                                Ok resultObj
                            with :? _ as e ->
                                logger.LogError (sprintf "error: cache update failed. %A\n" e)
                                statesDetails.Clear()
                                Error "Failed to update cache"
                        | Error e -> Error e
                    | _ -> Error "Object does not implement Refreshable interface"
                else
                    Error "not found"
                
        
        member this.Evict (key: DetailsCacheKey)  =
            statesDetails.Remove key.Value
        
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
            with :? _ as e ->
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
    
    type AggregateCache3 private () =
        let ignoreIncomingBackplane = config.GetValue<bool>("Cache:IgnoreIncomingBackplaneNotifications", false)
        let aggregateOptions = FusionCacheOptions(
            CacheName = "statePerAggregate",
            CacheKeyPrefix = "statePerAggregate:",
            IgnoreIncomingBackplaneNotifications = ignoreIncomingBackplane
        )
        let statePerAggregate = new FusionCache(aggregateOptions)
        let cacheExpirationConfigInSeconds = config.GetValue<float>("AggregateCacheExpiration", 600)
        let entryOptions =
            FusionCacheEntryOptions().
                SetDuration(TimeSpan.FromSeconds(cacheExpirationConfigInSeconds))
        static let instance = AggregateCache3()
        static member Instance = instance
        
        member this.SetupL2AndBackplane(dc: IDistributedCache option, ser: IFusionCacheSerializer option, bp: IFusionCacheBackplane option) =
            if dc.IsSome && ser.IsSome then
                (statePerAggregate :> IFusionCache).SetupDistributedCache(dc.Value, ser.Value) |> ignore
            if bp.IsSome then
                (statePerAggregate :> IFusionCache).SetupBackplane(bp.Value) |> ignore
            ()

        member private this.TryCache (aggregateId, eventId: EventId, resultState: obj) =
            try
                statePerAggregate.Set<(EventId * obj)>(aggregateId.ToString(), (eventId, resultState), entryOptions)
                ()
                
            with :? _ as e -> 
                logger.LogError (sprintf "error: cache is doing something wrong. Resetting. %A\n" e)
                statePerAggregate.Clear()
                // if an object failed to be cached then clear the details cache as well (otherwise dependencies could be out of sync)
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
       
        member this.Memoize2 (eventId: EventId, x:'A) (aggregateId: AggregateId) =
            this.Clean aggregateId
            this.TryCache (aggregateId, eventId, x)
        
        member this.Clean (aggregateId: AggregateId)  =
            statePerAggregate.Remove (aggregateId.ToString())
        
        member this.Clear () =
            statePerAggregate.Clear()
        
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
        
        let jsonOptions = JsonFSharpOptions.Default().ToJsonSerializerOptions()
        let serializer = new FusionCacheSystemTextJsonSerializer(jsonOptions)
        setupSecondLevelCacheAndBackplane (Some (sqlCache :> IDistributedCache)) (Some (serializer :> IFusionCacheSerializer)) None