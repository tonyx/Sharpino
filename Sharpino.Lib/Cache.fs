namespace Sharpino

open Sharpino
open Sharpino.Core
open System.Runtime.CompilerServices
open System.Collections
open FSharp.Core
open log4net

module Cache =
    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    type EventCache<'A when 'A: equality> private () =
        let dic = Generic.Dictionary<'A * List<Event<'A>>, Result<'A, string>>()
        let queue = Generic.Queue<'A * List<Event<'A>>>()
        static let instance = EventCache<'A>()
        static member Instance = instance

        [<MethodImpl(MethodImplOptions.Synchronized)>]
        member private this.TryAddToDictionary (arg, res) =
            try
                dic.Add(arg, res)
                queue.Enqueue arg
                if (queue.Count > Conf.cacheSize) then
                    let removed = queue.Dequeue()
                    dic.Remove removed |> ignore
                ()
            with :? _ as e -> 
                printf "error: cache is doing something wrong. Resetting. %A\n" e   
                log.Error(sprintf "error: cache is doing something wrong. Resetting. %A\n" e)    
                dic.Clear()
                queue.Clear()
                ()

        // event cache is disabled because at the moment it's not helping
        member this.Memoize (f: unit -> Result<'A, string>) (arg: 'A * List<Event<'A>>) =
            #if EVENTS_CACHE_IS_DISABLED
                f()
            #else
                let fromCacheOrCalculated =
                    let (b, res) = dic.TryGetValue arg
                    if b then
                        res
                    else
                        let res = f()
                        this.TryAddToDictionary(arg, res)
                        res
                fromCacheOrCalculated
            #endif

        member this.Clear() =
            dic.Clear()
            queue.Clear()

    type StateCache<'A> private () =
        let dic = Generic.Dictionary<(int * string), Result<int*'A, string>>()
        let queue = Generic.Queue<(int * string)>()
        static let instance = StateCache<'A>()
        static member Instance = instance

        [<MethodImpl(MethodImplOptions.Synchronized)>]
        member private this.TryAddToDictionary (arg, res) =
            try
                dic.Add(arg, res)
                queue.Enqueue arg
                if (queue.Count > Conf.cacheSize) then
                    let removed = queue.Dequeue()
                    dic.Remove removed |> ignore
                ()
            with :? _ as e -> 
                printf "error: cache is doing something wrong. Resetting. %A\n" e   
                log.Error(sprintf "error: cache is doing something wrong. Resetting. %A\n" e)    
                dic.Clear()
                queue.Clear()
                ()

        member this.Memoize (f: unit -> Result<int*'A, string>) (arg: int * string) =
            let fromCacheOrCalculated =
                let (b, res) = dic.TryGetValue arg
                if b then
                    res
                else
                    let res = f()
                    this.TryAddToDictionary(arg, res)
                    res
            fromCacheOrCalculated

        member this.Clear() =
            dic.Clear()
            queue.Clear()

    type SnapCache<'A> private () =
        let dic = Generic.Dictionary<int * string, Result<'A, string>>()
        let queue = Generic.Queue<int * string>()
        static let instance = SnapCache<'A>()
        static member Instance = instance

        [<MethodImpl(MethodImplOptions.Synchronized)>]
        member private this.TryAddToDictionary(arg, res) =
            try
                dic.Add(arg, res)
                queue.Enqueue arg
                if (queue.Count > 1) then
                    let removed = queue.Dequeue()
                    dic.Remove removed |> ignore
                ()
            with :? _ as e -> 
                printf "error: cache is doing something wrong. Resetting. %A\n" e
                log.Error(sprintf "error: cache is doing something wrong. Resetting. %A\n" e)    
                dic.Clear()
                queue.Clear()
                ()

        // this one looks like it's helping
        member this.Memoize (f: unit -> Result<'A, string>) (arg: int * string) =
            let fromCacheOrCalculated =
                let (b, res) = dic.TryGetValue arg
                if b then
                    res
                else
                    let res = f()
                    this.TryAddToDictionary(arg, res)
                    res
            fromCacheOrCalculated
        member this.Clear() =
            dic.Clear()
            queue.Clear()