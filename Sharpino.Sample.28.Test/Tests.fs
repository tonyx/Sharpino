module Tests

open Expecto
open DotNetEnv
open System
open Npgsql
open StackExchange.Redis
open Microsoft.Extensions.Configuration
open Sharpino
open Sharpino.Storage
open Sharpino.Cache
open Sharpino.CommandHandler
open Sharpino.EventBroker
open Sharpino.Template.Models
open Sharpino.Template.Models.Details
open Sharpino.Template.Commons
open Sharpino.Template.TodosManager

Env.Load() |> ignore

let password = Environment.GetEnvironmentVariable("password")
let userId   = Environment.GetEnvironmentVariable("userId")
let port     = Environment.GetEnvironmentVariable("port")
let database = Environment.GetEnvironmentVariable("database")

let connection =
    "Host=127.0.0.1;" +
    $"Port={port};" +
    $"Database={database};" +
    $"User Id={userId};" +
    $"Password={password}"

let pgEventStore = PgStorage.PgEventStore connection
let todoViewer = getAggregateStorageFreshStateViewer<Todo, TodoEvents, string> pgEventStore
let userViewer = getAggregateStorageFreshStateViewer<User, UserEvents, string> pgEventStore

let todoViewerAsync = getAggregateStorageFreshStateViewerAsync<Todo, TodoEvents, string> pgEventStore
let userViewerAsync = getAggregateStorageFreshStateViewerAsync<User, UserEvents, string> pgEventStore

let manager = TodoManager(MessageSenders.NoSender, pgEventStore, todoViewer, userViewer, todoViewerAsync, userViewerAsync)

let pgReset () =
    pgEventStore.Reset Todo.Version Todo.StorageName |> ignore
    pgEventStore.Reset User.Version User.StorageName |> ignore
    pgEventStore.ResetAggregateStream Todo.Version Todo.StorageName |> ignore
    pgEventStore.ResetAggregateStream User.Version User.StorageName |> ignore
    StateCache2<Todo>.Instance.Invalidate()
    StateCache2<User>.Instance.Invalidate()
    AggregateCache3.Instance.Clear()
    DetailsCache.Instance.Clear()

// ─── Postgres L2 helpers ──────────────────────────────────────────────────────

let pgCacheConnectionString = "Host=127.0.0.1;Port=5435;Database=sharpino_l2_cache;Username=sharpino;Password=password"

let clearL2PgCache () =
    use conn = new NpgsqlConnection(pgCacheConnectionString)
    conn.Open()
    use cmd = conn.CreateCommand()
    cmd.CommandText <- "TRUNCATE TABLE public.\"L2CacheTable\""
    cmd.ExecuteNonQuery() |> ignore

let countL2PgCacheEntries () =
    use conn = new NpgsqlConnection(pgCacheConnectionString)
    conn.Open()
    use cmd = conn.CreateCommand()
    cmd.CommandText <- "SELECT COUNT(*) FROM public.\"L2CacheTable\""
    let result = cmd.ExecuteScalar()
    Convert.ToInt32(result)

let getL2PgCacheKeys () =
    use conn = new NpgsqlConnection(pgCacheConnectionString)
    conn.Open()
    use cmd = conn.CreateCommand()
    cmd.CommandText <- "SELECT \"Id\" FROM public.\"L2CacheTable\""
    use reader = cmd.ExecuteReader()
    let keys = System.Collections.Generic.List<string>()
    while reader.Read() do
        keys.Add(reader.GetString(0))
    List.ofSeq keys

// ─── Redis L2 helpers ─────────────────────────────────────────────────────────

let redisCacheConnectionString = "localhost:6380"

/// Attempt to connect to Redis; returns None if the server is unavailable.
let tryConnectRedis () =
    try
        let opts = ConfigurationOptions.Parse(redisCacheConnectionString)
        opts.ConnectTimeout <- 1000
        opts.AbortOnConnectFail <- false
        let conn = ConnectionMultiplexer.Connect(opts)
        if conn.IsConnected then Some conn else None
    with _ -> None

let clearL2RedisCache (conn: ConnectionMultiplexer) =
    let db = conn.GetDatabase()
    let endpoints = conn.GetEndPoints()
    for ep in endpoints do
        let server = conn.GetServer(ep)
        // The StackExchange.Redis cache uses an instance name prefix "sharpino:"
        let keys = server.Keys(pattern = "sharpino:*") |> Seq.toArray
        for k in keys do
            db.KeyDelete(k) |> ignore

let countL2RedisCacheEntries (conn: ConnectionMultiplexer) =
    let endpoints = conn.GetEndPoints()
    let mutable count = 0
    for ep in endpoints do
        let server = conn.GetServer(ep)
        count <- count + (server.Keys(pattern = "sharpino:*") |> Seq.length)
    count

let getL2RedisCacheKeys (conn: ConnectionMultiplexer) =
    let endpoints = conn.GetEndPoints()
    [for ep in endpoints do
        let server = conn.GetServer(ep)
        yield! server.Keys(pattern = "sharpino:*") |> Seq.map (fun k -> k.ToString())]

// ─── Tests ───────────────────────────────────────────────────────────────────

[<Tests>]
let postgresTests =
    testList "L2 Postgres Cache Verification" [
        testCase "Expect that L2 Postgres cache gets populated when fetching user details" <| fun () ->
            // Arrange
            pgReset()
            clearL2PgCache()

            let initialCount = countL2PgCacheEntries()
            Expect.equal initialCount 0 "Postgres L2 Cache should be empty initially"

            // Act
            let todo = Todo.New "Read a book"
            let addTodoResult = manager.AddTodo todo
            Expect.isOk addTodoResult "Should successfully add todo"

            let user = User.New "Alice"
            let addUserResult = manager.AddUser user
            Expect.isOk addUserResult "Should successfully add user"

            let assignResult = manager.AssignTodo (UserId user.Id) (TodoId todo.Id)
            Expect.isOk assignResult "Should successfully assign todo to user"

            // Request User Details, which executes Memoize to populate details cache
            let detailsResult = manager.GetUserDetails (UserId user.Id)
            Expect.isOk detailsResult "Should successfully get user details"

            // Assert
            let finalCount = countL2PgCacheEntries()
            let keys = getL2PgCacheKeys()
            printfn "Postgres L2 Cache Keys found: %A" keys

            Expect.isTrue (finalCount > 0) "Postgres L2 Cache should have populated entries"
            let hasObjectDetailsKey = keys |> List.exists (fun k -> k.Contains("objectDetails:"))
            Expect.isTrue hasObjectDetailsKey "Postgres L2 Cache should contain keys starting with 'objectDetails:'"

        testCase "Expect that PG LISTEN/NOTIFY invalidates L1 cache across nodes" <| fun () ->
            // Arrange
            pgReset()
            let aggregateId = Guid.NewGuid()
            
            // Prime L1 cache with an initial state
            let initialEventId = 1
            let initialValue = "Initial value"
            let result1 = AggregateCache3.Instance.Memoize (fun () -> Ok (initialEventId, box initialValue)) aggregateId
            Expect.equal (result1 |> Result.map (snd >> unbox<string>)) (Ok initialValue) "Cache should return the initial value"

            // Attempt to fetch again with a different resolver function (should return cached initial value)
            let result2 = AggregateCache3.Instance.Memoize (fun () -> Ok (2, box "New value")) aggregateId
            Expect.equal (result2 |> Result.map (snd >> unbox<string>)) (Ok initialValue) "Cache should return the cached initial value, bypassing resolver"

            // Act: Publish eviction notice via pg_notify simulating another node evicting this aggregate
            do
                use conn = new NpgsqlConnection(pgCacheConnectionString)
                conn.Open()
                use cmd = conn.CreateCommand()
                cmd.CommandText <- "SELECT pg_notify('sharpino_cache_eviction', $1)"
                let payload = sprintf "EntryRemove:some_other_node:statePerAggregate:%s" (aggregateId.ToString())
                cmd.Parameters.AddWithValue(payload) |> ignore
                cmd.ExecuteNonQuery() |> ignore

            // Wait a moment for async LISTEN loop to receive notification and invalidate L1
            System.Threading.Thread.Sleep(500)

            // Assert: Memoize again. Since it was evicted, it should invoke resolver and return the new value
            let newValue = "New value"
            let result3 = AggregateCache3.Instance.Memoize (fun () -> Ok (2, box newValue)) aggregateId
            Expect.equal (result3 |> Result.map (snd >> unbox<string>)) (Ok newValue) "Cache should invoke resolver and return the new value after L1 invalidation"
    ]

[<Tests>]
let redisTests =
    testList "L2 Redis Cache Verification" [
        testCase "Redis L2 cache gets populated when fetching user details" <| fun () ->
            match tryConnectRedis() with
            | None ->
                Tests.skiptest "Redis is not available on localhost:6380 — start it with ./setup-redis-cache.sh and configure L2CacheProvider=Redis"
            | Some conn ->
                use _ = conn

                // Verify that the active L2 provider is Redis
                let provider = Sharpino.Cache.config.["Cache:L2CacheProvider"]
                if not (String.Equals(provider, "Redis", StringComparison.OrdinalIgnoreCase)) then
                    Tests.skiptest (sprintf "L2CacheProvider is '%s', not 'Redis' — set L2CacheProvider=Redis in appSettings.json to run this test" provider)

                // Arrange
                pgReset()
                clearL2RedisCache conn

                let initialCount = countL2RedisCacheEntries conn
                Expect.equal initialCount 0 "Redis L2 Cache should be empty initially"

                // Act
                let todo = Todo.New "Learn F#"
                Expect.isOk (manager.AddTodo todo) "Should add todo"

                let user = User.New "Bob"
                Expect.isOk (manager.AddUser user) "Should add user"

                Expect.isOk (manager.AssignTodo (UserId user.Id) (TodoId todo.Id)) "Should assign todo"

                let detailsResult = manager.GetUserDetails (UserId user.Id)
                Expect.isOk detailsResult "Should get user details"

                // Wait briefly for async L2 writes to settle
                System.Threading.Thread.Sleep(200)

                // Assert
                let keys = getL2RedisCacheKeys conn
                printfn "Redis L2 Cache Keys found: %A" keys
                let finalCount = countL2RedisCacheEntries conn

                Expect.isTrue (finalCount > 0) "Redis L2 Cache should have populated entries after fetching user details"
                let hasObjectDetailsKey = keys |> List.exists (fun k -> k.Contains("objectDetails:"))
                Expect.isTrue hasObjectDetailsKey "Redis L2 Cache should contain objectDetails keys"

        testCase "Redis pub/sub backplane invalidates L1 cache when a key is deleted" <| fun () ->
            match tryConnectRedis() with
            | None ->
                Tests.skiptest "Redis is not available on localhost:6380 — start it with ./setup-redis-cache.sh"
            | Some conn ->
                use _ = conn

                let provider = Sharpino.Cache.config.["Cache:L2CacheProvider"]
                if not (String.Equals(provider, "Redis", StringComparison.OrdinalIgnoreCase)) then
                    Tests.skiptest (sprintf "L2CacheProvider is '%s', not 'Redis'" provider)

                let backplaneCfg = Sharpino.Cache.config.["Cache:L2RedisBackplaneEnabled"]
                let backplaneEnabled = not (isNull backplaneCfg) && backplaneCfg.Equals("true", StringComparison.OrdinalIgnoreCase)
                if not backplaneEnabled then
                    Tests.skiptest "L2RedisBackplaneEnabled is false — enable it in appSettings.json to test the Redis backplane"

                // Arrange
                pgReset()
                let aggregateId = Guid.NewGuid()

                let initialValue = "Initial Redis value"
                let result1 = AggregateCache3.Instance.Memoize (fun () -> Ok (1, box initialValue)) aggregateId
                Expect.equal (result1 |> Result.map (snd >> unbox<string>)) (Ok initialValue) "Should return initial cached value"

                // Confirm cache hit (resolver not called)
                let result2 = AggregateCache3.Instance.Memoize (fun () -> Ok (2, box "Should not be returned")) aggregateId
                Expect.equal (result2 |> Result.map (snd >> unbox<string>)) (Ok initialValue) "Should return cached value (not resolver result)"

                // Act: Simulate another node evicting via Redis pub/sub
                let db = conn.GetDatabase()
                let channelName : string = 
                    let v = Sharpino.Cache.config.["Cache:L2RedisBackplaneChannel"]
                    if isNull v then "sharpino_cache_eviction" else v
                let payload = sprintf "EntryRemove:some_other_node:statePerAggregate:%s" (aggregateId.ToString())
                db.Publish(RedisChannel.Literal(channelName : string), payload) |> ignore

                // Wait for async invalidation
                System.Threading.Thread.Sleep(500)

                // Assert: cache should be invalidated; resolver should be called
                let newValue = "New Redis value"
                let result3 = AggregateCache3.Instance.Memoize (fun () -> Ok (2, box newValue)) aggregateId
                Expect.equal (result3 |> Result.map (snd >> unbox<string>)) (Ok newValue) "Cache should be invalidated by Redis pub/sub and resolver should be called"
    ]
