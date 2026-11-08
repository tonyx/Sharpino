namespace Sharpino.Sample._10.Models
open System
open Sharpino
open FsToolkit.ErrorHandling
open Sharpino.Commons
open Sharpino.Core
open Sharpino.Sample._10.Models.Counter

module Events =
    type CounterEvents =
        | Incremented
        | Decremented
        | CounterNoOp
        
        interface Event<Counter> with
            member this.Process (counter: Counter) =
                match this with
                | Incremented -> counter.Increment ()
                | Decremented -> counter.Decrement ()
                | CounterNoOp -> Ok counter
                
        static member Deserialize x =
            binarySerializer.Deserialize<CounterEvents> x
            
        member this.Serialize =
            binarySerializer.Serialize this    