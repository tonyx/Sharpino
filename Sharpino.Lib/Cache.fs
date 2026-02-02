namespace Sharpino

open System.Collections.Concurrent
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Caching.Memory
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
        let statesDetails = new MemoryCache(MemoryCacheOptions())
            
        let detailsCacheExpirationConfigInSeconds = config.GetValue<float>("DetailsCacheExpiration", 300)
        let detailsCacheDependenciesExpirationConfigInSeconds = config.GetValue<float>("DetailsCacheDependenciesExpiration", 301)
        
        let detailsEntryOptions =
            MemoryCacheEntryOptions().
                SetSize(1L).
                SetSlidingExpiration(TimeSpan.FromSeconds(detailsCacheExpirationConfigInSeconds))
        
        let detailsDependenciesEntryOptions =
            MemoryCacheEntryOptions().
                SetSize(1L).
                SetSlidingExpiration(TimeSpan.FromSeconds(detailsCacheDependenciesExpirationConfigInSeconds))        
        
        let objectDetailsAssociationsCache = new MemoryCache(MemoryCacheOptions())
        
        static let instance = DetailsCache ()
        static member Instance = instance
            
        member this.UpdateMultipleAggregateIdAssociation (aggregateIds: AggregateId[]) (key: DetailsCacheKey) =
            for aggregateId in aggregateIds do
                let existingKeys = objectDetailsAssociationsCache.Get<List<DetailsCacheKey>>(aggregateId)
                let updatedKeys = 
                    if isNull (box existingKeys) then
                        [key]
                    elif not (List.contains key existingKeys) then
                        key :: existingKeys
                    else
                        existingKeys
                objectDetailsAssociationsCache.Set(aggregateId, updatedKeys, detailsDependenciesEntryOptions) |> ignore
            ()
            
        // will be deprecated as passing an expiration is a risk (or at least it should be greater than the default)
        member this.UpdateMultipleAggregateIdAssociationRef (aggregateIds: AggregateId[]) (key: DetailsCacheKey) (expiration: TimeSpan option)=
            
            for aggregateId in aggregateIds do
                let existingKeys = objectDetailsAssociationsCache.Get<List<DetailsCacheKey>>(aggregateId)
                let updatedKeys = 
                    if isNull (box existingKeys) then
                        [key]
                    elif not (List.contains key existingKeys) then
                        key :: existingKeys
                    else
                        existingKeys
                objectDetailsAssociationsCache.Set(aggregateId, updatedKeys, detailsDependenciesEntryOptions) |> ignore
            ()
        
        member this.Refresh (key: DetailsCacheKey) =
            let v = statesDetails.Get<obj>(key.Value)
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
                                statesDetails.Set<obj>(key.Value, resultObj, detailsEntryOptions) |> ignore
                                Ok resultObj
                            with :? _ as e ->
                                logger.LogError (sprintf "error: cache update failed. %A\n" e)
                                statesDetails.Compact(1.0)
                                Error "Failed to update cache"
                        | Error e -> Error e
                    | _ -> Error "Object does not implement Refreshable interface"
                else
                    Error "not found"
                
        
        member this.Evict (key: DetailsCacheKey)  =
            statesDetails.Remove key.Value
        
        member this.RefreshDependentDetails (aggregateId: AggregateId) =
            let keys = objectDetailsAssociationsCache.Get<List<DetailsCacheKey>>(aggregateId)
            if not (obj.ReferenceEquals(keys, null)) then
                for key in keys do
                    let refreshed =
                        this.Refresh key
                    ()    
            ()
        
        member this.evictDependentDetails (aggregateId: AggregateId) =
            let keys = objectDetailsAssociationsCache.Get<List<DetailsCacheKey>>(aggregateId)
            if not (obj.ReferenceEquals(keys, null)) then
                for key in keys do
                    this.Evict key
                ()
        
        member private this.TryCache (key: string, value: Refreshable<_>) =
            try
                statesDetails.Set<obj>(key, value, detailsEntryOptions) |> ignore
            with :? _ as e ->
                logger.LogError (sprintf "error: cache is doing something wrong. Resetting. %A\n" e)
                statesDetails.Compact(1.0)
                ()
                    
        member this.Memoize (f: unit -> Result<Refreshable<_>*List<AggregateId>, string>) (key: DetailsCacheKey) =
            let v = statesDetails.Get<obj>(key.Value)
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
            statesDetails.Compact(1.0)
            objectDetailsAssociationsCache.Compact(1.0)
    
    type AggregateCache3 private () =
        let statePerAggregate = new MemoryCache(MemoryCacheOptions())
        let cacheExpirationConfigInSeconds = config.GetValue<float>("AggregateCacheExpiration", 600)
        let entryOptions =
            MemoryCacheEntryOptions().
                SetSize(1L).
                SetSlidingExpiration(TimeSpan.FromSeconds(cacheExpirationConfigInSeconds))
        static let instance = AggregateCache3()
        static member Instance = instance
        
        member private this.TryCache (aggregateId, eventId: EventId, resultState: obj) =
            try
                statePerAggregate.Set<(EventId * obj)>(aggregateId, (eventId, resultState), entryOptions) |> ignore
                ()
                
            with :? _ as e -> 
                logger.LogError (sprintf "error: cache is doing something wrong. Resetting. %A\n" e)
                statePerAggregate.Compact(1.0)
                // if an object failed to be cached then clear the details cache as well (otherwise dependencies could be out of sync)
                DetailsCache.Instance.Clear()
                () 
   
        member this.Memoize (f: unit -> Result<EventId * obj, string>) (aggregateId: AggregateId): Result<EventId * obj, string> =
            let v = statePerAggregate.Get<(EventId * obj)>(aggregateId)
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
            statePerAggregate.Remove aggregateId
        
        member this.Clear () =
            statePerAggregate.Compact(1.0)
        
        member this.LastEventId (aggregateId: AggregateId) =
            let v = statePerAggregate.Get<(EventId * obj)>(aggregateId)
            if not (obj.ReferenceEquals(v, null)) then v |> fst |> Some else None
        
        member this.GetState (aggregateId: AggregateId) =
            let v = statePerAggregate.Get<(EventId * obj)>(aggregateId)
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
            
           
            