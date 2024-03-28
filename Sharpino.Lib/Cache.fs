namespace Sharpino

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
                log.Error(sprintf "error: State cache is doing something wrong. Resetting. %A\n" e)    
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

    type AggregateCache<'A when 'A :> Aggregate> private () =
        let dic = Generic.Dictionary<EventId * AggregateId, Result<'A, string>>()
        let queue = Generic.Queue<EventId * AggregateId>()
        static let instance = AggregateCache<'A>()
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
                log.Error(sprintf "error: aggregate cache is doing something wrong. Resetting. %A\n" e)    
                dic.Clear()
                queue.Clear()
                ()

        member this.Memoize (f: unit -> Result<'A, string>) (arg: EventId * AggregateId)  =
            match arg with
            | 0, _ ->
                f()
            | _ ->
                let (b, res) = dic.TryGetValue arg
                if b then
                    res
                else
                    let res = f()
                    this.TryAddToDictionary(arg, res)
                    res
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
