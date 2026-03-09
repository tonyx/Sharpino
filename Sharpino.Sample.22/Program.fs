module Main

open System
open Sharpino.Template.Models
open Sharpino.Template.Commons
open Sharpino.Template.TodosManager
open Sharpino
open Sharpino.Storage
open Sharpino.EventBroker
open Sharpino.CommandHandler
open DotNetEnv

/// Constant Guid used to identify the well-known Todo across runs.
let todoGuid = Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890")
let todoId   = TodoId todoGuid

[<EntryPoint>]
let main argv =
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
    let todoViewer = getAggregateStorageFreshStateViewer<Todo, TodoEvents, string> pgEventStore
    
    let manager = TodoManager(MessageSenders.NoSender, pgEventStore, todoViewer)

    printfn "======================================="
    printfn " Sharpino.Sample.21 - Todo sync demo"
    printfn " Started: %s" (DateTime.Now.ToString("HH:mm:ss"))
    printfn "======================================="

    // Read & print the actual config values being used by the Cache module
    // (same config object it uses at module-init time)
    let cfg    = Sharpino.Cache.config
    let sbEnabled  = cfg.["Cache:L2ServiceBusEnabled"]
    let sbTopic    = cfg.["Cache:ServiceBusTopicName"]
    let sbSub      = cfg.["Cache:ServiceBusSubscriptionName"]
    let sqlEnabled = cfg.["Cache:L2SqlCacheEnabled"]
    let env        = Sharpino.Cache.env
    printfn "[Config] ASPNETCORE_ENVIRONMENT  : %s" (if String.IsNullOrWhiteSpace env then "(default)" else env)
    printfn "[Config] Cache:L2SqlCacheEnabled : %s" sqlEnabled
    printfn "[Config] Cache:L2ServiceBusEnabled: %s" sbEnabled
    printfn "[Config] ServiceBusTopicName     : %s" sbTopic
    printfn "[Config] ServiceBusSubscriptionName: %s" sbSub
    printfn "(If the backplane is active, changes made in another instance"
    printfn " will appear here after pressing Enter to refresh.)"
    printfn ""

    // Ensure the todo exists before starting the loop
    let existing = manager.GetTodo todoId
    match existing with
    | Ok _ -> ()
    | Error _ ->
        let todo = { Todo.New "My persistent todo" with TodoId = todoId }
        match manager.AddTodo todo with
        | Ok _ -> printfn "Initial todo created."
        | Error err -> printfn "Warning: could not create initial todo: %s" err

    let rec loop () =
        let current = manager.GetTodo todoId
        match current with
        | Error err -> 
            printfn "Error getting todo: %s" err
            1
        | Ok todo ->
            printfn "\n[%s] ----------------------------------------" (DateTime.Now.ToString("HH:mm:ss"))
            printfn " Current Todo Text: %s" todo.Text
            printfn "----------------------------------------"
            printfn "Options:"
            printfn "1) Delete L2 cache  (force next read from PostgreSQL)"
            printfn "2) Delete Local L1  (force next read from L2/PostgreSQL)"
            printfn "3) Rename Todo"
            printf "Select an option (or press Enter to refresh): "
            let choice = Console.ReadLine()
            
            match choice with
            | "1" -> 
                Cache.AggregateCache3.Instance.ClearL2()
                Cache.DetailsCache.Instance.ClearL2()
                printfn "[%s] L2 Cache deleted." (DateTime.Now.ToString("HH:mm:ss"))
                loop ()
            | "2" -> 
                Cache.AggregateCache3.Instance.ClearL1()
                Cache.DetailsCache.Instance.ClearL1()
                printfn "[%s] Local L1 Cache deleted." (DateTime.Now.ToString("HH:mm:ss"))
                loop ()
            | "3" ->
                printf "Enter new name: "
                let input = Console.ReadLine()
                if not (String.IsNullOrWhiteSpace input) then
                    match manager.RenameTodo todoId input with
                    | Ok _ -> printfn "[%s] Rename to '%s' successful." (DateTime.Now.ToString("HH:mm:ss")) input
                    | Error err -> printfn "Rename failed: %s" err
                loop ()
            | _ ->
                loop ()

    loop ()
