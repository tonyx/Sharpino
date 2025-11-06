namespace Sharpino

open System.Collections.Concurrent
open Microsoft.Extensions.Logging
open Sharpino
open Sharpino.Core
open Sharpino.Definitions
open System.Runtime.CompilerServices
open Microsoft.Extensions.Logging.Abstractions
open System.Collections
open FSharp.Core
open System

module Cache =
    let numProcs = Environment.ProcessorCount
    let concurrencyLevel = numProcs * 2

    let logger: Microsoft.Extensions.Logging.ILogger ref = ref NullLogger.Instance
    let setLogger (newLogger: Microsoft.Extensions.Logging.ILogger) =
        logger := newLogger
    let config = 
        try
            Conf.config ()
        with
        | :? _ as ex -> 
            // if sharpinoSettings.json is missing
            printf "sharpinoSettings.json file not found using default!!! %A\n" ex
            Conf.defaultConf
   
    
    type AggregateCache3 private ()  =
        let aggregateQueue = Generic.Queue<AggregateId>(config.CacheAggregateSize)
        let statePerAggregate = ConcurrentDictionary<AggregateId, EventId * obj>(concurrencyLevel, config.CacheAggregateSize)
        static let instance = AggregateCache3()
        static member Instance = instance
        
        member private this.TryCache (aggregateId, eventId: EventId, resultState: obj) =
            try
                statePerAggregate.[aggregateId] <- (eventId, resultState)
                
                if (not (aggregateQueue.Contains aggregateId)) then 
                    aggregateQueue.Enqueue aggregateId
                    
                if (aggregateQueue.Count > config.CacheAggregateSize) then
                    let removed = aggregateQueue.Dequeue()
                    statePerAggregate.TryRemove removed  |> ignore
                ()
                
            with :? _ as e -> 
                logger.Value.LogError (sprintf "error: cache is doing something wrong. Resetting. %A\n" e)
                statePerAggregate.Clear()
                aggregateQueue.Clear()
                () 
    
        member this.Memoize (f: unit -> Result<EventId * obj, string>) (aggregateId: AggregateId): Result<EventId * obj, string> =
            if (statePerAggregate.ContainsKey aggregateId) then
                statePerAggregate.[aggregateId] |> Ok
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
            statePerAggregate.TryRemove aggregateId  |> ignore
        
        member this.Clear () =
            statePerAggregate.Clear()
            aggregateQueue.Clear() 
         
    type AggregateCache2  private () =
        let lastEventIdPerAggregate = ConcurrentDictionary<AggregateId, EventId>(concurrencyLevel, config.CacheAggregateSize)
        let aggregateQueue = Generic.Queue<AggregateId>(config.CacheAggregateSize)
        let statePerAggregate = ConcurrentDictionary<AggregateId, Result<'A, string>>(concurrencyLevel, config.CacheAggregateSize)

        static let instance = AggregateCache2()
        static member Instance = instance
        
        member private this.TryAddToDictionary ((eventId, aggregateId), resultState: Result<'A, string>) =
            try
                lastEventIdPerAggregate.[aggregateId] <- eventId
                statePerAggregate.[aggregateId] <- resultState
                
                if (not (aggregateQueue.Contains aggregateId)) then 
                    aggregateQueue.Enqueue aggregateId
                    
                if (aggregateQueue.Count > config.CacheAggregateSize) then
                    let removed = aggregateQueue.Dequeue ()
                    lastEventIdPerAggregate.TryRemove removed  |> ignore
                    statePerAggregate.TryRemove removed  |> ignore
                ()
                
            with :? _ as e -> 
                logger.Value.LogError (sprintf "error: cache is doing something wrong. Resetting. %A\n" e)
                lastEventIdPerAggregate.Clear()
                statePerAggregate.Clear()
                aggregateQueue.Clear()
                () 
        
        member this.TryGetLastEventId(aggregateId: AggregateId) =
            if (lastEventIdPerAggregate.ContainsKey aggregateId) then
                lastEventIdPerAggregate.[aggregateId] |> Some
            else
                None
                    
        
        member this.Memoize (f: unit -> Result<'A, string>) (eventId: EventId, aggregateId: AggregateId): Result<'A, string> =
            if ((lastEventIdPerAggregate.ContainsKey aggregateId) &&
                (lastEventIdPerAggregate.[aggregateId] = eventId) &&
                (statePerAggregate.ContainsKey aggregateId)) 
            then 
                statePerAggregate[aggregateId]
            else
                let res = f()
                this.TryAddToDictionary ((eventId, aggregateId), res) 
                res
               
        member this.Clean (aggregateId: AggregateId) =
            aggregateQueue.TryDequeue() |> ignore
            lastEventIdPerAggregate.TryRemove aggregateId  |> ignore
            statePerAggregate.TryRemove aggregateId  |> ignore
       
        member this.Memoize2 (x:Result<'A, string>) (eventId: EventId, aggregateId: AggregateId) =
            this.Clean aggregateId
            this.TryAddToDictionary ((eventId, aggregateId), x)
                    
        member this.GetState (eventId: EventId, aggregateId: AggregateId) =
            if ((lastEventIdPerAggregate.ContainsKey aggregateId) &&
                (lastEventIdPerAggregate.[aggregateId] = eventId) &&
                (statePerAggregate.ContainsKey aggregateId))
            then
                statePerAggregate.[aggregateId]
            else
                Error (sprintf "cache miss: %A" aggregateId)
        
        member this.Clear () =
            lastEventIdPerAggregate.Clear ()
            aggregateQueue.Clear ()
            statePerAggregate.Clear ()
        
        member this.LastEventId(aggregateId: AggregateId) =
            if (lastEventIdPerAggregate.ContainsKey aggregateId) then
                lastEventIdPerAggregate.[aggregateId] |> Some
            else
                None
  
    
     
     
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
            // f ()
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
            // None
            match cachedValue with
            | Some res -> Some (eventId, res)
            | None -> None
            
        member this.Memoize2 (x: 'A) (eventId: EventId) =
            // ()
            this.TryCache (x, eventId)
       
        member this.LastEventId() =
            // 0
            eventId

        [<MethodImpl(MethodImplOptions.Synchronized)>]      
        member this.Invalidate() =
            cachedValue <- None         
            eventId <- 0
            ()
            
           
            