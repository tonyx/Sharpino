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
        ftestCase "a new object consist of a single initial snapshot having eventId null - Ok" <| fun _ ->
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

        ftestCase "a new object and only two more events will end up in not changing any snapshot - Ok" <| fun _ ->
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

            // now add a comment
            let todoCommented = todoManager.AddComment learnFSharp.TodoId "comment1"
            Expect.isOk todoCommented "must be ok"

            // now add a comment
            let todoCommented = todoManager.AddComment learnFSharp.TodoId "comment2"
            Expect.isOk todoCommented "must be ok"

            // the last snapshot is still the initial one
            let tryGetSnapshot = 
                (pgEventStore :> IEventStore<string>).TryGetLastAggregateSnapshot Todo.Version Todo.StorageName learnFSharpid
            Expect.isOk tryGetSnapshot "should be ok"
            let (eventId, _) = tryGetSnapshot.OkValue
            Expect.isNone eventId "should be None"

        // after a careful analysis I decided that this test needs dbmate drop/dbmate up before running the entire test suite
        ftestCase "a new object and three new events will end up in a new snapshot - Ok" <| fun _ ->
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

            // now add a comment
            let todoCommented = todoManager.AddComment learnFSharp.TodoId "comment1"
            Expect.isOk todoCommented "must be ok"

            // now add a comment
            let todoCommented = todoManager.AddComment learnFSharp.TodoId "comment2"
            Expect.isOk todoCommented "must be ok"

            // now add a comment
            let todoCommented = todoManager.AddComment learnFSharp.TodoId "comment3"
            Expect.isOk todoCommented "must be ok"

            // the last snapshot is still the initial one
            let tryGetSnapshot = 
                (pgEventStore :> IEventStore<string>).TryGetLastAggregateSnapshot Todo.Version Todo.StorageName learnFSharpid
            Expect.isOk tryGetSnapshot "should be Ok"
            let (eventId, _) = tryGetSnapshot.OkValue
            Expect.isSome eventId "should be Some"

        ftestCase "add a single object and add just two events, then the latest snapshot is just the very first one " <| fun _ ->
            setUp ()
            let todoManager = TodoManager (MessageSenders.NoSender, pgEventStore, todoViewer)
            let learnFSharp = Todo.New "Learn F#"
            let addLearnFSharp = todoManager.AddTodo learnFSharp
            Expect.isOk addLearnFSharp "must be ok"

            let todoCommented = todoManager.AddComment learnFSharp.TodoId "comment1"
            Expect.isOk todoCommented "must be ok"

            let todoCommented = todoManager.AddComment learnFSharp.TodoId "comment2"
            Expect.isOk todoCommented "must be ok"

            let tryGetSnapshot =
                (pgEventStore :> IEventStore<string>).TryGetLastAggregateSnapshot Todo.Version Todo.StorageName learnFSharp.Id
            Expect.isOk tryGetSnapshot "should be Ok"
            let (eventId, _) = tryGetSnapshot.OkValue
            Expect.isNone eventId "should be None"

        ftestCase "add a single object and add three events, then the latest snapthot is the second one having some event id " <| fun _ ->
            setUp ()
            let todoManager = TodoManager (MessageSenders.NoSender, pgEventStore, todoViewer)
            let learnFSharp = Todo.New "Learn F#"
            let addLearnFSharp = todoManager.AddTodo learnFSharp
            Expect.isOk addLearnFSharp "must be ok"

            let todoCommented = todoManager.AddComment learnFSharp.TodoId "comment1"
            Expect.isOk todoCommented "must be ok"

            let todoCommented = todoManager.AddComment learnFSharp.TodoId "comment2"
            Expect.isOk todoCommented "must be ok"

            let todoCommented = todoManager.AddComment learnFSharp.TodoId "comment3"
            Expect.isOk todoCommented "must be ok"

            let tryGetSnapshot =
                (pgEventStore :> IEventStore<string>).TryGetLastAggregateSnapshot Todo.Version Todo.StorageName learnFSharp.Id
            Expect.isOk tryGetSnapshot "should be Ok"
            let (eventId, _) = tryGetSnapshot.OkValue
            Expect.isSome eventId "should be Some"

        ftestCase "add two objects and add two events to the first object and three events to the second object. The second object doesn't have any snapshot" <| fun _ ->
            setUp ()
            let todoManager = TodoManager (MessageSenders.NoSender, pgEventStore, todoViewer)
            let learnFSharp = Todo.New "Learn F#"
            let learnFSharpid = learnFSharp.Id
            let learnRust = Todo.New "Learn Rust"
            let learnRustId = learnRust.Id
            let addLearnFSharp = todoManager.AddTodo learnFSharp
            Expect.isOk addLearnFSharp "must be ok"
            let todoCommented = todoManager.AddComment learnFSharp.TodoId "comment1"
            Expect.isOk todoCommented "must be ok"
            let todoCommented = todoManager.AddComment learnFSharp.TodoId "comment2"
            Expect.isOk todoCommented "must be ok"
            let addLearnRust = todoManager.AddTodo learnRust
            Expect.isOk addLearnRust "must be ok"
            let todoCommented = todoManager.AddComment learnRust.TodoId "comment1"
            Expect.isOk todoCommented "must be ok"
            let todoCommented = todoManager.AddComment learnRust.TodoId "comment2"
            Expect.isOk todoCommented "must be ok"
            let todoCommented = todoManager.AddComment learnRust.TodoId "comment3"
            Expect.isOk todoCommented "must be ok"

            let tryGetFSharpSnapshot =
                (pgEventStore :> IEventStore<string>).TryGetLastAggregateSnapshot Todo.Version Todo.StorageName learnFSharp.Id
            Expect.isOk tryGetFSharpSnapshot "should be Ok"
            let (eventId, _) = tryGetFSharpSnapshot.OkValue
            Expect.isNone eventId "should be None"

            let tryGetRustSnapshot =
                (pgEventStore :> IEventStore<string>).TryGetLastAggregateSnapshot Todo.Version Todo.StorageName learnRust.Id
            Expect.isOk tryGetRustSnapshot "should be Ok"
            let (eventId, _) = tryGetRustSnapshot.OkValue
            Expect.isSome eventId "should be Some"
            

        ftestCase "add a snapshot, then add an event. Check the distance from the last snapshot of that event has a value" <| fun _ ->
            setUp ()
            let todoManager = TodoManager (MessageSenders.NoSender, pgEventStore, todoViewer)
            let learnFSharp = Todo.New "Learn F#"
            let addLearnFSharp = todoManager.AddTodo learnFSharp
            Expect.isOk addLearnFSharp "should be ok"
            let addTodoComment = todoManager.AddComment learnFSharp.TodoId "comment 1"
            Expect.isOk addTodoComment "should be ok"
            let lastEventId = 
                (pgEventStore :> IEventStore<string>).TryGetLastEventId Todo.Version Todo.StorageName
            Expect.isSome lastEventId "should be some"

    ] 
    |> testSequenced
