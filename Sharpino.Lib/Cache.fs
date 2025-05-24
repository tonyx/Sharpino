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

    // type AggregateCache<'A, 'F when 'A :> Aggregate<'F>> private () =
    //     
    //     // future use, reminder
    //     // let cache = System.Runtime.Caching.MemoryCache.Default // will use this instead of dictionary later
    //     // let concurrentDic = ConcurrentDictionary<EventId * AggregateId, Result<'A, string>>()
    //     let dic = Generic.Dictionary<EventId * AggregateId, Result<'A, string>>()
    //     let queue = Generic.Queue<EventId * AggregateId>()
    //     static let instance = AggregateCache<'A, 'F>()
    //     static member Instance = instance
    //
    //     [<MethodImpl(MethodImplOptions.Synchronized)>]
    //     member private this.TryAddToDictionary (arg, res) =
    //         try
    //             dic.Add(arg, res)
    //             queue.Enqueue arg
    //             if (queue.Count > config.CacheAggregateSize) then
    //                 let removed = queue.Dequeue()
    //                 dic.Remove removed |> ignore
    //             ()
    //         with :? _ as e -> 
    //             logger.Value.LogError (sprintf "error: cache is doing something wrong. Resetting. %A\n" e)
    //             dic.Clear()
    //             queue.Clear()
    //             ()
    //           
    //     member this.Memoize (f: unit -> Result<'A, string>) (arg: EventId * AggregateId)  =
    //         let (b, res) = dic.TryGetValue arg
    //         if b then
    //             res
    //         else
    //             this.Clean (arg |> snd)
    //             let res = f()
    //             this.TryAddToDictionary(arg, res)
    //             res
    //    
    //     member this.Memoize2 (x: Result<'A, string>) (arg: EventId * AggregateId)  =
    //         this.Clean (arg |> snd)
    //         this.TryAddToDictionary(arg, x)
    //    
    //     member this.Clean (aggregateId: AggregateId) =
    //         let keys = dic.Keys
    //         let keys' = keys |> List.ofSeq |> List.filter (fun (_, aggregateId') -> aggregateId = aggregateId')
    //         keys' |> List.iter (fun key -> dic.Remove key |> ignore)
    //         ()
    //
    //     member this.LastEventId(aggregateId: Guid) =
    //         dic.Keys  
    //         |> List.ofSeq 
    //         |> List.filter (fun (_, aggregateId') -> aggregateId = aggregateId')
    //         |> List.map (fun (eventId, _) -> eventId)
    //         |> List.sort 
    //         |> List.tryLast
    //
    //     member this.GetState (key: EventId * AggregateId) =
    //         let (b, res) = dic.TryGetValue key
    //         if b then
    //             res
    //         else
    //             Error "state not found"
    //
    //     [<MethodImpl(MethodImplOptions.Synchronized)>]
    //     member this.Clear() =
    //         dic.Clear()
    //         queue.Clear()
    //
    // a track of refactoring for AggregateCache
    
    
    type AggregateCache<'A, 'F when 'A :> Aggregate<'F>> private () =
        
        let lastEventIdPerAggregate = Generic.Dictionary<AggregateId, EventId>(config.CacheAggregateSize)
        let aggregateQueue = Generic.Queue<AggregateId>(config.CacheAggregateSize)
        let statePerAggregate = Generic.Dictionary<AggregateId, Result<'A, string>>(config.CacheAggregateSize)
        let queue = Generic.Queue<AggregateId>()
        
        static let instance = AggregateCache<'A, 'F>()
        static member Instance = instance
              
        [<MethodImpl(MethodImplOptions.Synchronized)>]
        member private this.TryAddToDictionary ((eventId, aggregateId), resultState: Result<'A, string>) =
            try
                lastEventIdPerAggregate.[aggregateId] <- eventId
                statePerAggregate.[aggregateId] <- resultState
                
                if (not (aggregateQueue.Contains aggregateId)) then 
                    aggregateQueue.Enqueue aggregateId
                    
                if (aggregateQueue.Count > config.CacheAggregateSize) then
                    let removed = aggregateQueue.Dequeue ()
                    lastEventIdPerAggregate.Remove removed  |> ignore
                    statePerAggregate.Remove removed  |> ignore
                ()
                
            with :? _ as e -> 
                logger.Value.LogError (sprintf "error: cache is doing something wrong. Resetting. %A\n" e)
                lastEventIdPerAggregate.Clear()
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
       
        // suspicious as it doesn't remove anything from the queue
        // however: 1. max size is not affected. 1. Just a deprecated saga-ish function in command handler uses it
        member this.Clean (aggregateId: AggregateId) =
            lastEventIdPerAggregate.Remove aggregateId  |> ignore
            statePerAggregate.Remove aggregateId  |> ignore
        
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
        
        [<MethodImpl(MethodImplOptions.Synchronized)>]
        member this.Clear () =
            lastEventIdPerAggregate.Clear ()
            aggregateQueue.Clear ()
            statePerAggregate.Clear ()
        
        [<MethodImpl(MethodImplOptions.Synchronized)>]
        member this.Invalidate (aggregateId: AggregateId) =
            lastEventIdPerAggregate.Remove aggregateId  |> ignore
            statePerAggregate.Remove aggregateId  |> ignore
            // can't pick a specific element the queue and remove it. Not a big deal as the size is limited anyway
            // will fix when adopting .net caching
        
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
            
           
            