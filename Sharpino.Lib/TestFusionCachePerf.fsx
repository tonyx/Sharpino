#r "nuget: ZiggyCreatures.FusionCache, 2.5.0"
open ZiggyCreatures.Caching.Fusion
let t = typeof<FusionCacheOptions>
let p = t.GetProperty("EnableSyncEventHandlersExecution")
printfn "%b" (p <> null)
