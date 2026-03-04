#r "nuget: ZiggyCreatures.FusionCache, 2.5.0"
open ZiggyCreatures.Caching.Fusion

let t = typeof<FusionCacheOptions>
for p in t.GetProperties() do
    printfn "Prop: %s" p.Name
