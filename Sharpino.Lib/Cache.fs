namespace Sharpino

open System.Linq
open Sharpino
open Sharpino.Core
open Sharpino.Definitions
open System.Runtime.CompilerServices
open System.Collections
open FSharp.Core
open log4net
open System

module Cache =
    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    let config = 
        try
            Conf.config ()
        with
        | :? _ as ex -> 
            // if appSettings.json is missing
            log.Error (sprintf "appSettings.json file not found using default!!! %A\n" ex)
            Conf.defaultConf

    type StateCache<'A > private () =
        let dic = Generic.Dictionary<EventId, Result<'A, string>>()
        let queue = Generic.Queue<EventId>()
        static let instance = StateCache<'A>()
        static member Instance = instance

        [<MethodImpl(MethodImplOptions.Synchronized)>]
        member private this.TryAddToDictionary (arg, res) =
            try
                dic.Add(arg, res)
                queue.Enqueue arg
                // I guess I can keep only the only one latest state
                if (queue.Count > 1) then
                    let removed = queue.Dequeue()
                    dic.Remove removed |> ignore
                ()
            with :? _ as e -> 
                log.Error(sprintf "error: cache is doing something wrong. Resetting. %A\n" e)    
                dic.Clear()
                queue.Clear()
                ()

        member this.Memoize (f: unit -> Result<'A, string>) (arg: EventId) =
            let (b, res) = dic.TryGetValue arg
            if b then
                res
            else
                let res = f()
                this.TryAddToDictionary(arg, res)
                res
                
            // sometimes you skip cache for test
            // f ()
        
        member this.Memoize2 (x: Result<'A, string>) (arg: EventId)  =
            this.TryAddToDictionary(arg, x)  
            // sometimes you skip cache for test
            // ()
                
        member this.LastEventId() =
            dic.Keys  
            |> List.ofSeq 
            |> List.sort 
            |> List.tryLast

        member this.GestState (key: EventId) =
            let (b, res) = dic.TryGetValue key
            if b then
                res
            else
                Error "state not found"

        [<MethodImpl(MethodImplOptions.Synchronized)>]
        member this.Clear() =
            dic.Clear()
            queue.Clear()

    type AggregateCache<'A, 'F when 'A :> Aggregate<'F>> private () =
        let dic = Generic.Dictionary<EventId * AggregateId, Result<'A, string>>()
        let queue = Generic.Queue<EventId * AggregateId>()
        static let instance = AggregateCache<'A, 'F>()
        static member Instance = instance

        [<MethodImpl(MethodImplOptions.Synchronized)>]
        member private this.TryAddToDictionary (arg, res) =
            try
                dic.Add(arg, res)
                queue.Enqueue arg
                if (queue.Count > config.CacheAggregateSize) then
                    let removed = queue.Dequeue()
                    dic.Remove removed |> ignore
                ()
            with :? _ as e -> 
                log.Error(sprintf "error: cache is doing something wrong. Resetting. %A\n" e)    
                dic.Clear()
                queue.Clear()
                ()
        
        member this.Memoize (f: unit -> Result<'A, string>) (arg: EventId * AggregateId)  =
            // sometimes you want to bypass cache for test 
            // f()
            
            // why  I decided to not cache the initial value?
            // match arg with
            // | 0, _ ->
            //     f()
            // | _ ->
                let (b, res) = dic.TryGetValue arg
                if b then
                    res
                else
                    this.Clean (arg |> snd)
                    let res = f()
                    this.TryAddToDictionary(arg, res)
                    res
        
        member this.Memoize2 (x: Result<'A, string>) (arg: EventId * AggregateId)  =
            this.Clean (arg |> snd)
            this.TryAddToDictionary(arg, x)
            // sometimes you want to bypass cache for test 
            // ()    
       
        member this.Clean (aggregateId: AggregateId) =
            let keys = dic.Keys
            let keys' = keys |> List.ofSeq |> List.filter (fun (_, aggregateId') -> aggregateId = aggregateId')
            keys' |> List.iter (fun key -> dic.Remove key |> ignore)
            ()
            
        member this.LastEventId() =
            dic.Keys  
            |> List.ofSeq 
            |> List.sort 
            |> List.tryLast

        member this.LastEventId(aggregateId: Guid) =
            dic.Keys  
            |> List.ofSeq 
            |> List.filter (fun (_, aggregateId') -> aggregateId = aggregateId')
            |> List.map (fun (eventId, _) -> eventId)
            |> List.sort 
            |> List.tryLast

        member this.GetState (key: EventId * AggregateId) =
            let (b, res) = dic.TryGetValue key
            if b then
                res
            else
                Error "state not found"

        [<MethodImpl(MethodImplOptions.Synchronized)>]
        member this.Clear() =
            dic.Clear()
            queue.Clear()
