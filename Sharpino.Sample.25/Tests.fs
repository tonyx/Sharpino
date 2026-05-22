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
open Sharpino.Storage
open System.Text.Json

Env.Load() |> ignore
let password = Environment.GetEnvironmentVariable("password")
let userId = Environment.GetEnvironmentVariable("userId")
let port = Environment.GetEnvironmentVariable("port")
let database = Environment.GetEnvironmentVariable("database")

let connection =
    "Host=127.0.0.1;"
    + $"Port={port};"
    + $"Database={database};"
    + $"User Id={userId};"

let pgEventStore = PgStorage.PgEventStore connection

let setUp () =
    pgEventStore.Reset Todo.Version Todo.StorageName |> ignore
    pgEventStore.ResetAggregateStream Todo.Version Todo.StorageName |> ignore
    AggregateCache3.Instance.Clear()
    DetailsCache.Instance.Clear()

let todoViewer =
    getAggregateStorageFreshStateViewer<Todo, TodoEvents, string> pgEventStore


type TodoV2 =
    { TodoId: TodoId
      Text: string
      PrivateData: string
      State: State
      Tag: string }

    member this.Serialize = 
        (this, jsonOptions) |> JsonSerializer.Serialize

    static member DeserializeV2 (data: string) =
        try
            let todoV2 = JsonSerializer.Deserialize<TodoV2> (data, jsonOptions)
            if obj.ReferenceEquals(todoV2.Tag, null) then
                let todoV1 = JsonSerializer.Deserialize<Todo> (data, jsonOptions)
                let upcasted = { TodoId = todoV1.TodoId; Text = todoV1.Text; PrivateData = todoV1.PrivateData; State = todoV1.State; Tag = "upcasted" }
                Ok upcasted
            else
                Ok todoV2
        with
            | _ ->
                try
                    let todoV1 = JsonSerializer.Deserialize<Todo> (data, jsonOptions)
                    let upcasted = { TodoId = todoV1.TodoId; Text = todoV1.Text; PrivateData = todoV1.PrivateData; State = todoV1.State; Tag = "upcasted" }
                    Ok upcasted
                with
                    | ex -> Error ex.Message

[<Tests>]
let tests =
    testList
        "todos tests"
        [ testCase "add and retrieve a todo"
          <| fun _ ->
              setUp ()
              let todoManager = TodoManager(MessageSenders.NoSender, pgEventStore, todoViewer)
              let learnFSharp = Todo.New "Learn F#"
              let addLearnFSharp = todoManager.AddTodo learnFSharp
              Expect.isOk addLearnFSharp "error in adding todo"
              let retrievedTodo = todoManager.GetTodo learnFSharp.TodoId
              Expect.isOk retrievedTodo "error in retrieving todo"

          testCase "add two todos and retrieve all todos"
          <| fun _ ->
              setUp ()
              let todoManager = TodoManager(MessageSenders.NoSender, pgEventStore, todoViewer)
              let learnFSharp = Todo.New "Learn F#"
              let learnRust = Todo.New "Learn Rust"
              let addLearnFSharp = todoManager.AddTodo learnFSharp
              let addLearnRust = todoManager.AddTodo learnRust
              Expect.isOk addLearnFSharp "error in adding todo"
              Expect.isOk addLearnRust "error in adding todo"

              let retrievedTodos =
                  todoManager.GetTodosAsync()
                  |> Async.AwaitTask
                  |> Async.RunSynchronously
                  |> Result.get

              Expect.hasLength retrievedTodos 2 "error in retrieving todos"
              Expect.isTrue (retrievedTodos |> List.exists (fun x -> x = learnFSharp)) "error in retrieving todos"
              Expect.isTrue (retrievedTodos |> List.exists (fun x -> x = learnRust)) "error in retrieving todos"

          testCaseTask "add two todos and retrieve all todos using task"
          <| fun _ ->
              task {
                  setUp ()
                  let todoManager = TodoManager(MessageSenders.NoSender, pgEventStore, todoViewer)
                  let learnFSharp = Todo.New "Learn F#"
                  let learnRust = Todo.New "Learn Rust"
                  let addLearnFSharp = todoManager.AddTodo learnFSharp
                  let addLearnRust = todoManager.AddTodo learnRust
                  Expect.isOk addLearnFSharp "error in adding todo"
                  Expect.isOk addLearnRust "error in adding todo"
                  let! retrievedTodos = todoManager.GetTodosAsync()
                  let retrievedTodos = retrievedTodos |> Result.get
                  Expect.hasLength retrievedTodos 2 "error in retrieving todos"
                  Expect.isTrue (retrievedTodos |> List.exists (fun x -> x = learnFSharp)) "error in retrieving todos"
                  Expect.isTrue (retrievedTodos |> List.exists (fun x -> x = learnRust)) "error in retrieving todos"
              }

          testCaseTask "update private data"
          <| fun _ ->
              task {
                  setUp ()
                  let todoManager = TodoManager(MessageSenders.NoSender, pgEventStore, todoViewer)
                  let learnFSharp = Todo.New "Learn F#"
                  let addLearnFSharp = todoManager.AddTodo learnFSharp
                  Expect.isOk addLearnFSharp "error in adding todo"
                  let retrievedTodo = todoManager.GetTodo learnFSharp.TodoId
                  Expect.isOk retrievedTodo "error in retrieving todo"

                  let updatePrivateData =
                      todoManager.UpdatePrivateData learnFSharp.TodoId "Updated private data"

                  Expect.isOk updatePrivateData "error in updating private data"
                  let retrievedTodo = todoManager.GetTodo learnFSharp.TodoId
                  Expect.isOk retrievedTodo "error in retrieving todo"

                  Expect.isTrue
                      (retrievedTodo.OkValue.PrivateData = "Updated private data")
                      "error in updating private data"
              }

          testCaseTask "get sensible events"
          <| fun _ ->
              task {
                  setUp ()
                  let todoManager = TodoManager(MessageSenders.NoSender, pgEventStore, todoViewer)
                  let learnFSharp = Todo.New "Learn F#"
                  let addLearnFSharp = todoManager.AddTodo learnFSharp
                  Expect.isOk addLearnFSharp "error in adding todo"
                  let retrievedTodo = todoManager.GetTodo learnFSharp.TodoId
                  Expect.isOk retrievedTodo "error in retrieving todo"

                  let updatePrivateData =
                      todoManager.UpdatePrivateData learnFSharp.TodoId "Updated private data"

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

          testCaseTask "replace sensible events"
          <| fun _ ->
              task {
                  setUp ()
                  let todoManager = TodoManager(MessageSenders.NoSender, pgEventStore, todoViewer)
                  let learnFSharp = Todo.New "Learn F#"
                  let addLearnFSharp = todoManager.AddTodo learnFSharp
                  Expect.isOk addLearnFSharp "error in adding todo"
                  let retrievedTodo = todoManager.GetTodo learnFSharp.TodoId
                  Expect.isOk retrievedTodo "error in retrieving todo"

                  let updatePrivateData =
                      todoManager.UpdatePrivateData learnFSharp.TodoId "Updated private data"

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

                  let replaceSensibleDataEvents =
                      todoManager.ReplaceSensibleDataEvents learnFSharp.TodoId "dummy data"

                  let! retrieveEventShouldBeObfuscated = todoManager.GetSensibleEventsAsync learnFSharp.TodoId
                  Expect.isOk retrieveEventShouldBeObfuscated "should be ok"
                  let obfuscatedEvent = retrieveEventShouldBeObfuscated.OkValue |> List.head

                  match obfuscatedEvent with
                  | TodoEvents.PrivateDataUpdated privateData ->
                      Expect.equal privateData "dummy data" "error in updating private data"
                  | _ -> failwith "error in updating private data"
              }

          testCaseTask "replace sensible events - async"
          <| fun _ ->
              task {
                  setUp ()
                  let todoManager = TodoManager(MessageSenders.NoSender, pgEventStore, todoViewer)
                  let learnFSharp = Todo.New "Learn F#"
                  let addLearnFSharp = todoManager.AddTodo learnFSharp
                  Expect.isOk addLearnFSharp "error in adding todo"
                  let retrievedTodo = todoManager.GetTodo learnFSharp.TodoId
                  Expect.isOk retrievedTodo "error in retrieving todo"

                  let updatePrivateData =
                      todoManager.UpdatePrivateData learnFSharp.TodoId "Updated private data"

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

                  let! replaceSensibleDataEvents =
                      todoManager.ReplaceSensibleDataEventsAsync learnFSharp.TodoId "dummy data"

                  let! retrieveEventShouldBeObfuscated = todoManager.GetSensibleEventsAsync learnFSharp.TodoId
                  Expect.isOk retrieveEventShouldBeObfuscated "should be ok"
                  let obfuscatedEvent = retrieveEventShouldBeObfuscated.OkValue |> List.head

                  match obfuscatedEvent with
                  | TodoEvents.PrivateDataUpdated privateData ->
                      Expect.equal privateData "dummy data" "error in updating private data"
                  | _ -> failwith "error in updating private data"
              }

          testCaseTask "sensible data in snapshots"
          <| fun _ ->
              task {
                  setUp ()
                  let todoManager = TodoManager(MessageSenders.NoSender, pgEventStore, todoViewer)

                  let todoWithSensibleData =
                      { (Todo.New "Learn F#") with
                          PrivateData = "some private data" }

                  let addTodoWithPersonalData = todoManager.AddTodo todoWithSensibleData

                  let sensibleDataSubstitution =
                      fun (s: string) ->
                          result {
                              let! deserSnapshot = s |> Todo.Deserialize

                              let replaced =
                                  { deserSnapshot with
                                      PrivateData = "hidden by GDPR" }

                              return replaced.Serialize
                          }

                  let obfuscateSnapshots =
                      (pgEventStore :> IEventStore<string>).GDPRPartialUpdateSnapshots
                          Todo.Version
                          Todo.StorageName
                          todoWithSensibleData.Id
                          sensibleDataSubstitution

                  let cacheInvalidated = AggregateCache3.Instance.Clean todoWithSensibleData.Id
                  let state = todoManager.GetTodo todoWithSensibleData.TodoId
                  Expect.isOk state "should be ok"
                  Expect.equal state.OkValue.PrivateData "hidden by GDPR" "error in updating private data"
              }

          testCaseTask "sensible data in snapshots - 2"
          <| fun _ ->
              task {
                  setUp ()
                  let todoManager = TodoManager(MessageSenders.NoSender, pgEventStore, todoViewer)

                  let todoWithSensibleData =
                      { (Todo.New "Learn F#") with
                          PrivateData = "some private data" }

                  let addTodoWithPersonalData = todoManager.AddTodo todoWithSensibleData

                  let sensibleDataSubstitution =
                      fun (s: string) ->
                          result {
                              let! deserSnapshot = s |> Todo.Deserialize

                              let replaced =
                                  { deserSnapshot with
                                      PrivateData = "hidden by GDPR" }

                              return replaced.Serialize
                          }

                  let! obfuscateSnapshots =
                      (pgEventStore :> IEventStore<string>)
                          .GDPRPartialUpdateSnapshotsAsync(
                              Todo.Version,
                              Todo.StorageName,
                              todoWithSensibleData.Id,
                              sensibleDataSubstitution
                          )

                  let cacheInvalidated = AggregateCache3.Instance.Clean todoWithSensibleData.Id
                  let state = todoManager.GetTodo todoWithSensibleData.TodoId
                  Expect.isOk state "should be ok"
                  Expect.equal state.OkValue.PrivateData "hidden by GDPR" "error in updating private data"
              }

          testCaseTask "bulk upcast snapshots with coexistence and idempotency"
          <| fun _ ->
              task {
                  setUp ()
                  let todoManager = TodoManager(MessageSenders.NoSender, pgEventStore, todoViewer)

                  // 1. Create a Todo using standard V1 format
                  let todoV1 = Todo.New "Old Version Todo"
                  let addTodo = todoManager.AddTodo todoV1
                  Expect.isOk addTodo "error in adding todo"

                  // 2. Define our upcast lambda function
                  let upcasterLambda =
                      fun (s: string) ->
                          result {
                              let! deserSnapshot = s |> TodoV2.DeserializeV2
                              return deserSnapshot.Serialize
                          }

                  // 3. Perform bulk snapshot upcast
                  let! upcastResult =
                      (pgEventStore :> IEventStore<string>).BulkSnapshotsUpcast(
                          Todo.Version,
                          Todo.StorageName,
                          upcasterLambda
                      )

                  Expect.isOk upcastResult "Bulk upcast should succeed"
                  Expect.equal upcastResult.OkValue 1 "exactly 1 snapshot should have been upcasted"

                  // Clean cache so it re-reads from the DB
                  let cacheInvalidated = AggregateCache3.Instance.Clean todoV1.Id

                  // 4. Verify upcast: retrieve the snapshot and confirm it has the new format
                  let lastSnapResult = (pgEventStore :> IEventStore<string>).TryGetLastAggregateSnapshot Todo.Version Todo.StorageName todoV1.Id
                  Expect.isOk lastSnapResult "should successfully fetch aggregate snapshot"
                  
                  let _, snapJson = lastSnapResult.OkValue
                  let decodedV2 = TodoV2.DeserializeV2 snapJson
                  Expect.isOk decodedV2 "should be able to deserialize upcasted snapshot as V2"
                  Expect.equal decodedV2.OkValue.Tag "upcasted" "the upcasted snapshot must contain the new V2 tag"

                  // 5. Ensure Idempotency: run the bulk upcast again
                  let! upcastResult2 =
                      (pgEventStore :> IEventStore<string>).BulkSnapshotsUpcast(
                          Todo.Version,
                          Todo.StorageName,
                          upcasterLambda
                      )
                  Expect.isOk upcastResult2 "second bulk upcast should succeed"

                  // Verify the snapshot is still correct and has the V2 tag
                  let lastSnapResult2 = (pgEventStore :> IEventStore<string>).TryGetLastAggregateSnapshot Todo.Version Todo.StorageName todoV1.Id
                  let _, snapJson2 = lastSnapResult2.OkValue
                  let decodedV2_2 = TodoV2.DeserializeV2 snapJson2
                  Expect.isOk decodedV2_2 "should be able to deserialize upcasted snapshot as V2 again"
                  Expect.equal decodedV2_2.OkValue.Tag "upcasted" "the upcasted snapshot must still contain the V2 tag"
              }

          ]
    |> testSequenced
