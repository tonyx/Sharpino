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

        testCase "add a todo, delete it and retrieve ids by the eventstore: the event store is able to skip the deleted ones " <| fun _ ->
            setUp ()
            let todoManager = TodoManager (MessageSenders.NoSender, pgEventStore, todoViewer)
            let learnFSharp = Todo.New "Learn F#"
            let addLearnFSharp = todoManager.AddTodo learnFSharp
            Expect.isOk addLearnFSharp "should be ok"
            let deleteTodo = todoManager.DeleteTodo learnFSharp.TodoId
            Expect.isOk deleteTodo "should be ok"
            let getTodoAgain = todoManager.GetTodo learnFSharp.TodoId
            Expect.isError getTodoAgain "should be error"
            let eventStoreIds = (pgEventStore :> IEventStore<string>).GetAggregateIds Todo.Version Todo.StorageName
            Expect.isOk eventStoreIds "should be ok"
            let eventStoreIds = eventStoreIds |> Result.get
            Expect.hasLength eventStoreIds 1 "should be 1"
            let eventStoreIdsExcludingDeleteOnes =  (pgEventStore :> IEventStore<string>).GetUndeletedAggregateIds Todo.Version Todo.StorageName
            Expect.isOk eventStoreIdsExcludingDeleteOnes "should be ok"
            let eventStoreIdsExcludingDeleteOnes = eventStoreIdsExcludingDeleteOnes |> Result.get
            Expect.hasLength eventStoreIdsExcludingDeleteOnes 0 "should be 0"
            
        testCase "add two todos, delete one of them and then  and retrieve ids by the eventstore: the event store is able to skip the deleted ones, so will return just one " <| fun _ ->
            setUp ()
            let todoManager = TodoManager (MessageSenders.NoSender, pgEventStore, todoViewer)
            let learnFSharp = Todo.New "Learn F#"
            let addLearnFSharp = todoManager.AddTodo learnFSharp
            Expect.isOk addLearnFSharp "should be ok"
            let learnRust = Todo.New "Learn Rust"
            let addLearnRust = todoManager.AddTodo learnRust
            Expect.isOk addLearnRust "should be ok"
            let deleteTodo = todoManager.DeleteTodo learnFSharp.TodoId
            Expect.isOk deleteTodo "should be ok"
            let getTodoAgain = todoManager.GetTodo learnFSharp.TodoId
            Expect.isError getTodoAgain "should be error"
            let eventStoreIds = (pgEventStore :> IEventStore<string>).GetAggregateIds Todo.Version Todo.StorageName
            Expect.isOk eventStoreIds "should be ok"
            let eventStoreIds = eventStoreIds |> Result.get
            Expect.hasLength eventStoreIds 2 "should be 2"
            
            let eventStoreIdsExcludingDeleteOnes =  (pgEventStore :> IEventStore<string>).GetUndeletedAggregateIds Todo.Version Todo.StorageName
            Expect.isOk eventStoreIdsExcludingDeleteOnes "should be ok"
            let eventStoreIdsExcludingDeleteOnes = eventStoreIdsExcludingDeleteOnes |> Result.get
            Expect.hasLength eventStoreIdsExcludingDeleteOnes 1 "should be 0"
            
        testCase "add two todos, delete one of them and then  and retrieve ids by the eventstore: the event store is able to skip the deleted ones, so will return just one: use inmemory eventstore this time " <| fun _ ->
            setUp ()
            let todoManager = TodoManager (MessageSenders.NoSender, memoryEventStore, todoViewer)
            let learnFSharp = Todo.New "Learn F#"
            let addLearnFSharp = todoManager.AddTodo learnFSharp
            Expect.isOk addLearnFSharp "should be ok"
            let learnRust = Todo.New "Learn Rust"
            let addLearnRust = todoManager.AddTodo learnRust
            Expect.isOk addLearnRust "should be ok"
            let deleteTodo = todoManager.DeleteTodo learnFSharp.TodoId
            Expect.isOk deleteTodo "should be ok"
            let getTodoAgain = todoManager.GetTodo learnFSharp.TodoId
            Expect.isError getTodoAgain "should be error"
            let eventStoreIds = (memoryEventStore :> IEventStore<string>).GetAggregateIds Todo.Version Todo.StorageName
            Expect.isOk eventStoreIds "should be ok"
            let eventStoreIds = eventStoreIds |> Result.get
            Expect.hasLength eventStoreIds 2 "should be 2"
            
            let eventStoreIdsExcludingDeleteOnes =  (memoryEventStore :> IEventStore<string>).GetUndeletedAggregateIds Todo.Version Todo.StorageName
            Expect.isOk eventStoreIdsExcludingDeleteOnes "should be ok"
            let eventStoreIdsExcludingDeleteOnes = eventStoreIdsExcludingDeleteOnes |> Result.get
            Expect.hasLength eventStoreIdsExcludingDeleteOnes 1 "should be 0"
            
        testCase "add two todos, delete one of them and then  and retrieve ids by stateView: the stateview is able to skip the deleted ones, so will return just one: use async version " <| fun _ ->
            setUp ()
            let todoManager = TodoManager (MessageSenders.NoSender, pgEventStore, todoViewer)
            let learnFSharp = Todo.New "Learn F#"
            let addLearnFSharp = todoManager.AddTodo learnFSharp
            Expect.isOk addLearnFSharp "should be ok"
            let learnRust = Todo.New "Learn Rust"
            let addLearnRust = todoManager.AddTodo learnRust
            Expect.isOk addLearnRust "should be ok"
            let deleteTodo = todoManager.DeleteTodo learnFSharp.TodoId
            Expect.isOk deleteTodo "should be ok"
            let getTodoAgain = todoManager.GetTodo learnFSharp.TodoId
            Expect.isError getTodoAgain "should be error"
            
            let aggregateStates = StateView.getAllAggregateStates<Todo, TodoEvents, string> pgEventStore
            Expect.isOk aggregateStates "should be ok"
            let aggregateStates = aggregateStates |> Result.get
            Expect.hasLength aggregateStates 1 "should be 1"
            
        testCase "add two todos, delete one of them and then  and retrieve ids by the stateview in async mode: the stateview is able to skip the deleted ones, so will return just one: use async version " <| fun _ ->
            setUp ()
            let todoManager = TodoManager (MessageSenders.NoSender, pgEventStore, todoViewer)
            let learnFSharp = Todo.New "Learn F#"
            let addLearnFSharp = todoManager.AddTodo learnFSharp
            Expect.isOk addLearnFSharp "should be ok"
            let learnRust = Todo.New "Learn Rust"
            let addLearnRust = todoManager.AddTodo learnRust
            Expect.isOk addLearnRust "should be ok"
            let deleteTodo = todoManager.DeleteTodo learnFSharp.TodoId
            Expect.isOk deleteTodo "should be ok"
            let getTodoAgain = todoManager.GetTodo learnFSharp.TodoId
            Expect.isError getTodoAgain "should be error"
           
            let aggregateStates =
                StateView.getAllAggregateStatesAsync<Todo, TodoEvents, string> pgEventStore None
                |> Async.AwaitTask
                |> Async.RunSynchronously
             
            Expect.isOk aggregateStates "should be ok"
            let aggregateStates = aggregateStates |> Result.get
            Expect.hasLength aggregateStates 1 "should be 1"
            
        testCase "add two todos, delete one of them and then  and retrieve ids by the eventstore: the event store is able to skip the deleted ones, so will return just one: use async version " <| fun _ ->
            setUp ()
            let todoManager = TodoManager (MessageSenders.NoSender, pgEventStore, todoViewer)
            let learnFSharp = Todo.New "Learn F#"
            let addLearnFSharp = todoManager.AddTodo learnFSharp
            Expect.isOk addLearnFSharp "should be ok"
            let learnRust = Todo.New "Learn Rust"
            let addLearnRust = todoManager.AddTodo learnRust
            Expect.isOk addLearnRust "should be ok"
            let deleteTodo = todoManager.DeleteTodo learnFSharp.TodoId
            Expect.isOk deleteTodo "should be ok"
            let getTodoAgain = todoManager.GetTodo learnFSharp.TodoId
            Expect.isError getTodoAgain "should be error"
            let eventStoreIds = (pgEventStore :> IEventStore<string>).GetAggregateIds Todo.Version Todo.StorageName
            Expect.isOk eventStoreIds "should be ok"
            let eventStoreIds = eventStoreIds |> Result.get
            Expect.hasLength eventStoreIds 2 "should be 2"
            
            let eventStoreIdsExcludingDeleteOnes =
                (pgEventStore :> IEventStore<string>).GetUndeletedAggregateIdsAsync (Todo.Version, Todo.StorageName)
                |> Async.AwaitTask
                |> Async.RunSynchronously
            Expect.isOk eventStoreIdsExcludingDeleteOnes "should be ok"
            let eventStoreIdsExcludingDeleteOnes = eventStoreIdsExcludingDeleteOnes |> Result.get
            Expect.hasLength eventStoreIdsExcludingDeleteOnes 1 "should be 0"

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
    ] 
    |> testSequenced
