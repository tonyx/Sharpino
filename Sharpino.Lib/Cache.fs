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
            // if appSettings.json is missing
            printf "appSettings.json file not found using default!!! %A\n" ex
            Conf.defaultConf
    
    
    type AggregateCache<'A, 'F when 'A :> Aggregate<'F>> private () =
        
        let lastEventIdPerAggregate = ConcurrentDictionary<AggregateId, EventId>(concurrencyLevel, config.CacheAggregateSize)
        let aggregateQueue = Generic.Queue<AggregateId>(config.CacheAggregateSize)
        let statePerAggregate = ConcurrentDictionary<AggregateId, Result<'A, string>>(concurrencyLevel, config.CacheAggregateSize)
        
        static let instance = AggregateCache<'A, 'F>()
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
            if (lastEventIdPerAggregate.ContainsKey aggregateId) &&
               (lastEventIdPerAggregate.[aggregateId] = eventId) &&
               (statePerAggregate.ContainsKey aggregateId)
            then
                statePerAggregate.[aggregateId]
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
                Error "not found"
        
        member this.Clear () =
            lastEventIdPerAggregate.Clear ()
            aggregateQueue.Clear ()
            statePerAggregate.Clear ()
        
        member this.Invalidate (aggregateId: AggregateId) =
            lastEventIdPerAggregate.TryRemove aggregateId  |> ignore
            statePerAggregate.TryRemove aggregateId  |> ignore
        
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
            | None -> Error "state not found"
   
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
            
           
            