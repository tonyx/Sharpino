#r "nuget: ZiggyCreatures.FusionCache, 2.5.0"
#r "nuget: Microsoft.Extensions.Caching.Abstractions, 10.0.0"
#r "nuget: Microsoft.Extensions.Caching.SqlServer, 10.0.0"
#r "nuget: Microsoft.Extensions.Options, 10.0.0"
#r "nuget: ZiggyCreatures.FusionCache.Serialization.SystemTextJson, 2.5.0"

open Microsoft.Extensions.Caching.SqlServer
open Microsoft.Extensions.Options
open ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson
open Microsoft.Extensions.Caching.Distributed
open ZiggyCreatures.Caching.Fusion

let setupAzureSqlCache (connectionString: string) (schemaName: string) (tableName: string) =
    let options = SqlServerCacheOptions(ConnectionString = connectionString, SchemaName = schemaName, TableName = tableName)
    let opts = Options.Create(options)
    let sqlCache = new SqlServerCache(opts)
    let serializer = new FusionCacheSystemTextJsonSerializer()
    
    printfn "OK"

setupAzureSqlCache "Server=foo;Database=bar;User Id=user;Password=pass" "dbo" "sqlCache"
