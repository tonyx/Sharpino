namespace Sharpino.Sample._9
open Sharpino.Commons
open Sharpino.Core
open Sharpino.Sample._9.Item

module Events =
    type ItemEvent =
        | Renamed of string
        | ChangedDescription of string
        | ReferenceCounterDecremented of int
        | ReferenceCounterIncremented of int
        | Pinged
        interface Event<Item> with
            member this.Process (item: Item) =
                match this with
                | Renamed name -> item.Rename name
                | ChangedDescription description -> item.ChangeDescription description
                | ReferenceCounterDecremented i -> item.DecrementReferenceCounter i
                | ReferenceCounterIncremented i -> item.IncrementReferenceCounter i
                | Pinged -> item.Ping ()
            
        static member Deserialize x =
            jsonPSerializer.Deserialize<ItemEvent> x
        member this.Serialize =
            jsonPSerializer.Serialize this    