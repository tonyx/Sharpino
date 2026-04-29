module Tests
open DotNetEnv

open Expecto
open System
open Sharpino
open Sharpino.EventBroker
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
    $"User Id={userId};"

let pgEventStore = PgStorage.PgEventStore connection

let setUp () =
    pgEventStore.Reset Todo.Version Todo.StorageName |> ignore
    pgEventStore.ResetAggregateStream Todo.Version Todo.StorageName |> ignore
    AggregateCache3.Instance.Clear()
    DetailsCache.Instance.Clear()

let todoViewer = getAggregateStorageFreshStateViewer<Todo, TodoEvents, string> pgEventStore

[<Tests>]
let tests =
    testList "todos tests" [
        testCase "add and retrieve a todo" <| fun _ ->
            setUp ()
            let todoManager = TodoManager (MessageSenders.NoSender, pgEventStore, todoViewer)
            let learnFSharp = Todo.New "Learn F#"
            let addLearnFSharp = todoManager.AddTodo learnFSharp
            Expect.isOk addLearnFSharp "error in adding todo"
            let retrievedTodo = todoManager.GetTodo learnFSharp.TodoId
            Expect.isOk retrievedTodo "error in retrieving todo"

        testCase "add two todos and retrieve all todos" <| fun _ ->
            setUp ()
            let todoManager = TodoManager (MessageSenders.NoSender, pgEventStore, todoViewer)
            let learnFSharp = Todo.New "Learn F#"
            let learnRust = Todo.New "Learn Rust"
            let addLearnFSharp = todoManager.AddTodo learnFSharp
            let addLearnRust = todoManager.AddTodo learnRust
            Expect.isOk addLearnFSharp "error in adding todo"
            Expect.isOk addLearnRust "error in adding todo"
            let retrievedTodos = 
                todoManager.GetTodosAsync ()
                |> Async.AwaitTask
                |> Async.RunSynchronously
                |> Result.get
            Expect.hasLength retrievedTodos 2 "error in retrieving todos"
            Expect.isTrue (retrievedTodos |> List.exists (fun x -> x = learnFSharp)) "error in retrieving todos"
            Expect.isTrue (retrievedTodos |> List.exists (fun x -> x = learnRust)) "error in retrieving todos"

        testCaseTask "add two todos and retrieve all todos using task" <| fun _ ->
            task {
                setUp ()
                let todoManager = TodoManager (MessageSenders.NoSender, pgEventStore, todoViewer)
                let learnFSharp = Todo.New "Learn F#"
                let learnRust = Todo.New "Learn Rust"
                let addLearnFSharp = todoManager.AddTodo learnFSharp
                let addLearnRust = todoManager.AddTodo learnRust
                Expect.isOk addLearnFSharp "error in adding todo"
                Expect.isOk addLearnRust "error in adding todo"
                let! retrievedTodos = 
                    todoManager.GetTodosAsync () 
                let retrievedTodos =retrievedTodos |> Result.get
                Expect.hasLength retrievedTodos 2 "error in retrieving todos"
                Expect.isTrue (retrievedTodos |> List.exists (fun x -> x = learnFSharp)) "error in retrieving todos"
                Expect.isTrue (retrievedTodos |> List.exists (fun x -> x = learnRust)) "error in retrieving todos"
            }

        testCaseTask "update private data" <| fun _ ->
            task {
                setUp ()
                let todoManager = TodoManager (MessageSenders.NoSender, pgEventStore, todoViewer)
                let learnFSharp = Todo.New "Learn F#"
                let addLearnFSharp = todoManager.AddTodo learnFSharp
                Expect.isOk addLearnFSharp "error in adding todo"
                let retrievedTodo = todoManager.GetTodo learnFSharp.TodoId
                Expect.isOk retrievedTodo "error in retrieving todo"
                let updatePrivateData = todoManager.UpdatePrivateData learnFSharp.TodoId "Updated private data"
                Expect.isOk updatePrivateData "error in updating private data"
                let retrievedTodo = todoManager.GetTodo learnFSharp.TodoId
                Expect.isOk retrievedTodo "error in retrieving todo"
                Expect.isTrue (retrievedTodo.OkValue.PrivateData = "Updated private data") "error in updating private data"
            }

        testCaseTask "get sensible events" <| fun _ ->
            task {
                setUp ()
                let todoManager = TodoManager (MessageSenders.NoSender, pgEventStore, todoViewer)
                let learnFSharp = Todo.New "Learn F#"
                let addLearnFSharp = todoManager.AddTodo learnFSharp
                Expect.isOk addLearnFSharp "error in adding todo"
                let retrievedTodo = todoManager.GetTodo learnFSharp.TodoId
                Expect.isOk retrievedTodo "error in retrieving todo"
                let updatePrivateData = todoManager.UpdatePrivateData learnFSharp.TodoId "Updated private data"
                Expect.isOk updatePrivateData "error in updating private data"
                let! retrievedEvents = todoManager.GetSensibleEventsAsync learnFSharp.TodoId
                Expect.isOk retrievedEvents "should be ok"
                Expect.hasLength (retrievedEvents.OkValue) 1 "should be 1"
                let _event = retrievedEvents.OkValue |> List.head 
                let privateData =
                    match _event with
                    | TodoEvents.PrivateDataUpdated privateData -> privateData
                    | _ -> ""
                Expect.equal privateData "Updated private data" "error in updating private data"
            }
        ptestCaseTask "replace sensible events" <| fun _ ->
            task {
                setUp ()
                let todoManager = TodoManager (MessageSenders.NoSender, pgEventStore, todoViewer)
                let learnFSharp = Todo.New "Learn F#"
                let addLearnFSharp = todoManager.AddTodo learnFSharp
                Expect.isOk addLearnFSharp "error in adding todo"
                let retrievedTodo = todoManager.GetTodo learnFSharp.TodoId
                Expect.isOk retrievedTodo "error in retrieving todo"
                let updatePrivateData = todoManager.UpdatePrivateData learnFSharp.TodoId "Updated private data"
                Expect.isOk updatePrivateData "error in updating private data"
                let! retrievedEvents = todoManager.GetSensibleEventsAsync learnFSharp.TodoId
                Expect.isOk retrievedEvents "should be ok"
                Expect.hasLength (retrievedEvents.OkValue) 1 "should be 1"
                let _event = retrievedEvents.OkValue |> List.head 
                let privateData =
                    match _event with
                    | TodoEvents.PrivateDataUpdated privateData -> privateData
                    | _ -> ""
                Expect.equal privateData "Updated private data" "error in updating private data"
                let replaceSensibleDataEvents = todoManager.ReplaceSensibleDataEvents learnFSharp.TodoId "dummy data"
                let! retrieveEventShouldBeObfuscated = todoManager.GetSensibleEventsAsync learnFSharp.TodoId
                Expect.isOk retrieveEventShouldBeObfuscated "should be ok"
                let obfuscatedEvent = retrieveEventShouldBeObfuscated.OkValue |> List.head
                match obfuscatedEvent with 
                | TodoEvents.PrivateDataUpdated privateData -> 
                    Expect.equal privateData "dummy data" "error in updating private data"
                | _ -> 
                    failwith "error in updating private data"
            }

    ] 
    |> testSequenced
