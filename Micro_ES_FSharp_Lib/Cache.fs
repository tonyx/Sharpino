namespace Tonyx.EventSourcing

open Tonyx.EventSourcing
open Tonyx.EventSourcing.Core
open System.Runtime.CompilerServices
open System.Collections
open System
open FSharp.Core

module Cache =
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
                dic.Clear()
                queue.Clear()
                ()

        member this.Memoize (f: unit -> Result<'A, string>) (arg: 'A * List<Event<'A>>) =
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
        let _ = 
            printf "entering snapcache constructor\n"
        let dic = Generic.Dictionary<int, Result<'A, string>>()
        let queue = Generic.Queue<int>()
        static let instance = SnapCache<'A>()
        static member Instance = instance

        [<MethodImpl(MethodImplOptions.Synchronized)>]
        member private this.TryAddToDictionary(arg, res) =
            // printf "entering TryAddToDictionary\n"
            try
                dic.Add(arg, res)
                queue.Enqueue arg
                if (queue.Count > 1) then
                    let removed = queue.Dequeue()
                    dic.Remove removed |> ignore
                ()
            with :? _ as e -> 
                printf "warning: cache is doing something wrong %A\n" e
                printf "resetting cache of snapthots"
                dic.Clear()
                queue.Clear()
                ()
        member this.Memoize (f: unit -> Result<'A, string>) (arg: int) =
            // f()
            let fromCacheOrCalculated =
                let (b, res) = dic.TryGetValue arg
                if b then
                    // printf "snapshots hit!\n"
                    res
                else
                    let res = f()
                    this.TryAddToDictionary(arg, res)
                    // printf "snapshot miss\n"
                    res
            fromCacheOrCalculated
        member this.Clear() =
            dic.Clear()
            queue.Clear()