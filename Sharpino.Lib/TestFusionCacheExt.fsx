#r "nuget: ZiggyCreatures.FusionCache, 2.5.0"
#r "nuget: Microsoft.Extensions.Caching.Abstractions, 8.0.0"
open ZiggyCreatures.Caching.Fusion
open ZiggyCreatures.Caching.Fusion.Backplane
open ZiggyCreatures.Caching.Fusion.Serialization
open Microsoft.Extensions.Caching.Distributed
open System

try
    let fc = new FusionCache(new FusionCacheOptions())

    let cache = fc :> IFusionCache
    printfn "Is SetupDistributedCache available?"
    let m = typeof<FusionCache>.GetMethod("SetupDistributedCache")
    printfn "SetupDistributedCache exists: %b" (m <> null)
    let m2 = typeof<FusionCache>.GetMethod("SetupBackplane")
    printfn "SetupBackplane exists: %b" (m2 <> null)
with e -> printfn "Error %s" e.Message
