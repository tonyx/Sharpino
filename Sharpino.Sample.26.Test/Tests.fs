module Tests

open Expecto
open DotNetEnv
open System
open Npgsql
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

[<Tests>]
let tests =
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
    ]
