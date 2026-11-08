module Tests

open Expecto
open DotNetEnv
open System
open Microsoft.Data.SqlClient
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

let sqlCacheConnectionString = "Server=127.0.0.1,1433;Database=sharpinoCache;User Id=sa;Password=Sharpino@1234;TrustServerCertificate=True;"

let clearL2SqlCache () =
    use conn = new SqlConnection(sqlCacheConnectionString)
    conn.Open()
    use cmd = conn.CreateCommand()
    cmd.CommandText <- "TRUNCATE TABLE dbo.SharpinoL2Cache"
    cmd.ExecuteNonQuery() |> ignore

let countL2SqlCacheEntries () =
    use conn = new SqlConnection(sqlCacheConnectionString)
    conn.Open()
    use cmd = conn.CreateCommand()
    cmd.CommandText <- "SELECT COUNT(*) FROM dbo.SharpinoL2Cache"
    let result = cmd.ExecuteScalar()
    Convert.ToInt32(result)

let getL2SqlCacheKeys () =
    use conn = new SqlConnection(sqlCacheConnectionString)
    conn.Open()
    use cmd = conn.CreateCommand()
    cmd.CommandText <- "SELECT Id FROM dbo.SharpinoL2Cache"
    use reader = cmd.ExecuteReader()
    let keys = System.Collections.Generic.List<string>()
    while reader.Read() do
        keys.Add(reader.GetString(0))
    List.ofSeq keys

[<Tests>]
let tests =
    testList "L2 SQL Cache Verification" [
        testCase "Expect that L2 SQL cache gets populated when fetching user details" <| fun () ->
            // Arrange
            pgReset()
            clearL2SqlCache()

            let initialCount = countL2SqlCacheEntries()
            Expect.equal initialCount 0 "SQL L2 Cache should be empty initially"

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
            let finalCount = countL2SqlCacheEntries()
            let keys = getL2SqlCacheKeys()
            printfn "SQL L2 Cache Keys found: %A" keys

            Expect.isTrue (finalCount > 0) "SQL L2 Cache should have populated entries"
            let hasObjectDetailsKey = keys |> List.exists (fun k -> k.Contains("objectDetails:"))
            Expect.isTrue hasObjectDetailsKey "SQL L2 Cache should contain keys starting with 'objectDetails:'"

        testCase "Expect that runAggregateCommand populates SQL Server L2 cache and another node resolves L1 miss with L2 hit" <| fun () ->
            // Arrange
            pgReset()
            clearL2SqlCache()

            // Act 1: Execute runAggregateCommand via manager.AddTodo and manager.UpdatePrivateData
            let todo = Todo.New "SQL L2 Cache Stress Test"
            let addTodoResult = manager.AddTodo todo
            Expect.isOk addTodoResult "Should successfully add todo"

            let updateResult = manager.RenameTodo todo.TodoId "Updated private info in SQL"
            Expect.isOk updateResult "Should successfully update private data via runAggregateCommand"

            // Wait briefly for async L2 writes to settle
            System.Threading.Thread.Sleep(200)

            // Assert: L2 SQL cache table has the statePerAggregate entry
            let keys = getL2SqlCacheKeys()
            printfn "SQL L2 Cache Keys after command: %A" keys
            let hasAggregateKey = keys |> List.exists (fun k -> k.Contains(todo.TodoId.ToString()))
            Expect.isTrue hasAggregateKey "SQL L2 Cache should contain an entry for the updated aggregate"

            // Act 2: Simulate another node evicting its L1 cache entry
            AggregateCache3.Instance.ClearL1()

            // Act 3: Retrieve the todo on this node (L1 cache miss)
            let retrievedTodoResult = manager.GetTodo todo.TodoId
            Expect.isOk retrievedTodoResult "Should successfully retrieve todo from L2 hit following L1 miss"
            let retrievedTodo = retrievedTodoResult |> Result.get
            Expect.equal retrievedTodo.Text "Updated private info in SQL" "Retrieved todo should reflect the state updated in L2"
    ]
