module Tests
open DotNetEnv

open Expecto
open System
open Sharpino
open Sharpino.EventBroker
open Sharpino.Storage
open Sharpino.Template.Models
open Sharpino.Cache
open Sharpino.CommandHandler
open Sharpino.Template
open Sharpino.Template.Commons
open FsToolkit.ErrorHandling

Env.Load() |> ignore
let password = Environment.GetEnvironmentVariable("password")
let userId = Environment.GetEnvironmentVariable("userId")
let port = Environment.GetEnvironmentVariable("port")
let database = Environment.GetEnvironmentVariable("database")
let connection =
    "Host=127.0.0.1;" +
    $"Port={port};" +
    $"Database={database};" +
    $"User Id={userId};" +
    $"Password={password}"

let pgEventStore = PgStorage.PgEventStore connection
let memoryEventStore = MemoryStorage.MemoryStorage()

let setUp () =
    pgEventStore.Reset Todo.Version Todo.StorageName |> ignore
    pgEventStore.ResetAggregateStream Todo.Version Todo.StorageName |> ignore
    memoryEventStore.Reset Todo.Version Todo.StorageName |> ignore
    AggregateCache3.Instance.Clear()
    DetailsCache.Instance.Clear()

let todoViewer = getAggregateStorageFreshStateViewer<Todo, TodoEvents, string> pgEventStore

[<Tests>]
let tests =
    testList "todos tests" [
        testCase "a new object consist of a single initial snapshot having eventId null - Ok" <| fun _ ->
            setUp ()
            let todoManager = TodoManager (MessageSenders.NoSender, pgEventStore, todoViewer)
            let learnFSharp = Todo.New "Learn F#"
            let learnFSharpid = learnFSharp.Id
            let tryGetSnapshot = 
                (pgEventStore :> IEventStore<string>).TryGetLastAggregateSnapshot Todo.Version Todo.StorageName learnFSharpid
            Expect.isError tryGetSnapshot "should be None"

            // now add a new initial instance
            let todoAdded = todoManager.AddTodo learnFSharp
            Expect.isOk todoAdded "must be ok"

            // now there must be an initial snapshot with eventId null/none
            let tryGetSnapshot = 
                (pgEventStore :> IEventStore<string>).TryGetLastAggregateSnapshot Todo.Version Todo.StorageName learnFSharpid
            Expect.isOk tryGetSnapshot "should be Some"
            let (eventId, _) = tryGetSnapshot.OkValue
            Expect.isNone eventId "should be None"
    ] 
    |> testSequenced
