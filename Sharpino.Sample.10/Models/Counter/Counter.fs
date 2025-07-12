module Sharpino.Sample._10.Models.Counter
open System

type Counter =
    {
        Id: Guid
        Value: int
    }
    static member Initial =
        { Value = 0 }
    
    




