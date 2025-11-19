namespace Sharpino

open System.Collections.Concurrent
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Caching.Memory
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
        let statePerAggregate = new MemoryCache(MemoryCacheOptions())
        static let instance = AggregateCache3()
        static member Instance = instance
        
        member private this.TryCache (aggregateId, eventId: EventId, resultState: obj) =
            try
                let entryOptions = MemoryCacheEntryOptions().SetSize(1L)
                statePerAggregate.Set<(EventId * obj)>(aggregateId, (eventId, resultState), entryOptions) |> ignore
                ()
                
            with :? _ as e -> 
                logger.Value.LogError (sprintf "error: cache is doing something wrong. Resetting. %A\n" e)
                statePerAggregate.Compact(1.0)
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
            
           
            