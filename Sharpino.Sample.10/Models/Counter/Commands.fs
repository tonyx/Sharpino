namespace Sharpino.Sample._10.Models

open Sharpino.Commons
open Sharpino.Core
open Sharpino.Sample._10.Models.Counter
open Sharpino.Sample._10.Models.Events

module Commands =

    type CounterCommands =
        | Increment
        | Decrement
        
        interface AggregateCommand<Counter, CounterEvents> with
            member this.Execute (counter: Counter) =
                match this with
                | Increment ->
                    counter.Increment ()
                    |> Result.map (fun s -> (s, [Incremented]))
                | Decrement ->
                    counter.Decrement ()
                    |> Result.map (fun s -> (s, [Decremented]))
            member this.Undoer =
                None