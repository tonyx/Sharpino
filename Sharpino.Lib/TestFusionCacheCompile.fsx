#r "nuget: ZiggyCreatures.FusionCache, 2.5.0"
#r "nuget: Microsoft.Extensions.Caching.Abstractions, 10.0.0"
open ZiggyCreatures.Caching.Fusion
open ZiggyCreatures.Caching.Fusion.Backplane
open ZiggyCreatures.Caching.Fusion.Serialization
open Microsoft.Extensions.Caching.Distributed
open System

try
    let fc = new FusionCache(new FusionCacheOptions())
    let cache = fc :> IFusionCache
    
    // Test if we can compile a function that calls SetupDistributedCache
    let testSetup (dc: IDistributedCache, ser: IFusionCacheSerializer, bp: IFusionCacheBackplane) =
        cache.SetupDistributedCache(dc, ser) |> ignore
        cache.SetupBackplane(bp) |> ignore
        printfn "Methods match and compile correctly"
    printfn "OK"
with _ -> ()
