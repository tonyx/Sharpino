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

[<EntryPoint>]
let main argv =
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

    let manager = TodoManager(MessageSenders.NoSender, pgEventStore, todoViewer, userViewer)

    printfn "======================================="
    printfn " Sharpino.Sample.22 - Todo interactive demo"
    printfn " Started: %s" (DateTime.Now.ToString("HH:mm:ss"))
    printfn "======================================="

    let cfg         = Sharpino.Cache.config
    let sbEnabled   = cfg.["Cache:L2ServiceBusEnabled"]
    let sbTopic     = cfg.["Cache:ServiceBusTopicName"]
    let sbSub       = cfg.["Cache:ServiceBusSubscriptionName"]
    let sqlEnabled  = cfg.["Cache:L2SqlCacheEnabled"]
    let env         = Sharpino.Cache.env
    printfn "[Config] ASPNETCORE_ENVIRONMENT    : %s" (if String.IsNullOrWhiteSpace env then "(default)" else env)
    printfn "[Config] Cache:L2SqlCacheEnabled   : %s" sqlEnabled
    printfn "[Config] Cache:L2ServiceBusEnabled : %s" sbEnabled
    printfn "[Config] ServiceBusTopicName       : %s" sbTopic
    printfn "[Config] ServiceBusSubscriptionName: %s" sbSub
    printfn ""

    // Helper: fetch and print all todos, return them
    let listUsers () : User list =
        // StateView gets all users from the event store
        match StateView.getAggregateStatesInATimeInterval<User, UserEvents, string> pgEventStore DateTime.MinValue DateTime.MaxValue with
        | Error err ->
            printfn "Error fetching users: %s" err
            []
        | Ok users ->
            let userList = users |> List.map snd
            if userList.IsEmpty then
                printfn "  (no users yet)"
            else
                userList
                |> List.iteri (fun i (u: User) ->
                    let todoCount = u.Todos.Length
                    printfn "  %d) [%s] %s (Todos: %d)" (i + 1) (u.Id.ToString("D").Substring(0, 8)) u.Name todoCount)
            userList

    let listTodos () : Todo list =
        match manager.GetAllTodos() with
        | Error err ->
            printfn "Error fetching todos: %s" err
            []
        | Ok (todos: Todo list) ->
            if todos.IsEmpty then
                printfn "  (no todos yet)"
            else
                todos
                |> List.iteri (fun i (t: Todo) ->
                    printfn "  %d) [%s] %s" (i + 1) (t.Id.ToString("D").Substring(0, 8)) t.Text)
            todos

    let rec loop () =
        printfn "\n[%s] ========================================" (DateTime.Now.ToString("HH:mm:ss"))
        printfn "Options:"
        printfn "  --- Todos ---"
        printfn "  1) Add new Todo"
        printfn "  2) List Todos"
        printfn "  3) Select a Todo and rename it"
        printfn "  --- Users ---"
        printfn "  4) Add new User"
        printfn "  5) List Users & their assigned Todos"
        printfn "  6) Assign a Todo to a User"
        printfn "  7) Detach a Todo from a User"
        printfn "  8) Select a User and rename it"
        printfn "  --- Cache ---"
        printfn "  9) Clear L2 cache  (force next read from PostgreSQL)"
        printfn "  10) Clear local L1  (force next read from L2/PostgreSQL)"
        printf "Select an option (or Enter to refresh): "
        let choice = Console.ReadLine()

        match choice with
        | "1" ->
            printf "Enter todo text: "
            let text = Console.ReadLine()
            if not (String.IsNullOrWhiteSpace text) then
                let todo = Todo.New text
                match manager.AddTodo todo with
                | Ok _    -> printfn "[%s] Todo '%s' created." (DateTime.Now.ToString("HH:mm:ss")) text
                | Error e -> printfn "Error creating todo: %s" e
            loop ()

        | "2" ->
            printfn "\nAll Todos:"
            listTodos () |> ignore
            loop ()

        | "3" ->
            printfn "\nAll Todos:"
            let todos = listTodos ()
            if not todos.IsEmpty then
                printf "Enter the number of the Todo to rename: "
                let input = Console.ReadLine()
                match Int32.TryParse(input) with
                | false, _ ->
                    printfn "Invalid selection."
                | true, n when n < 1 || n > List.length todos ->
                    printfn "Out of range."
                | true, n ->
                    let selected: Todo = todos |> List.item (n - 1)
                    printf "Enter new name for '%s': " selected.Text
                    let newName = Console.ReadLine()
                    if not (String.IsNullOrWhiteSpace newName) then
                        match manager.RenameTodo (TodoId selected.Id) newName with
                        | Ok _    -> printfn "[%s] Renamed to '%s' successfully." (DateTime.Now.ToString("HH:mm:ss")) newName
                        | Error e -> printfn "Rename failed: %s" e
            loop ()

        | "4" ->
            printf "Enter user name: "
            let name = Console.ReadLine()
            if not (String.IsNullOrWhiteSpace name) then
                let user = User.New name
                match manager.AddUser user with
                | Ok _    -> printfn "[%s] User '%s' created." (DateTime.Now.ToString("HH:mm:ss")) name
                | Error e -> printfn "Error creating user: %s" e
            loop ()

        | "5" ->
            printfn "\nAll Users:"
            let users = listUsers ()
            users |> List.iter (fun u ->
                if u.Todos.Length > 0 then
                    printfn "  User %s's Todos:" u.Name
                    match manager.GetUserDetails (UserId u.Id) with
                    | Ok details ->
                        details.Todos |> List.iter (fun t ->
                            printfn "    - [%s] %s" (t.Id.ToString("D").Substring(0, 8)) t.Text
                        )
                    | Error err -> printfn "    (Error fetching details: %s)" err
            )
            loop ()

        | "6" ->
            printfn "\nSelect User:"
            let users = listUsers ()
            if not users.IsEmpty then
                printf "User number: "
                match Int32.TryParse(Console.ReadLine()) with
                | true, uIdx when uIdx >= 1 && uIdx <= users.Length ->
                    let u = users.[uIdx - 1]
                    printfn "\nSelect Todo to assign:"
                    let todos = listTodos ()
                    if not todos.IsEmpty then
                        printf "Todo number: "
                        match Int32.TryParse(Console.ReadLine()) with
                        | true, tIdx when tIdx >= 1 && tIdx <= todos.Length ->
                            let t = todos.[tIdx - 1]
                            match manager.AssignTodo (UserId u.Id) (TodoId t.Id) with
                            | Ok _ -> printfn "Assigned Todo '%s' to User '%s'." t.Text u.Name
                            | Error e -> printfn "Error assigning Todo: %s" e
                        | _ -> printfn "Invalid Todo selection."
                | _ -> printfn "Invalid User selection."
            loop ()

        | "7" ->
            printfn "\nSelect User:"
            let users = listUsers ()
            if not users.IsEmpty then
                printf "User number: "
                match Int32.TryParse(Console.ReadLine()) with
                | true, uIdx when uIdx >= 1 && uIdx <= users.Length ->
                    let u = users.[uIdx - 1]
                    if u.Todos.IsEmpty then
                        printfn "User has no assigned Todos."
                    else
                        printfn "\nSelect Todo to detach:"
                        let userTodos = 
                            manager.GetUserDetails (UserId u.Id) 
                            |> Result.map (fun d -> d.Todos) 
                            |> Result.defaultValue []
                        
                        userTodos |> List.iteri (fun i t -> 
                            printfn "  %d) [%s] %s" (i + 1) (t.Id.ToString("D").Substring(0, 8)) t.Text)
                            
                        printf "Todo number: "
                        match Int32.TryParse(Console.ReadLine()) with
                        | true, tIdx when tIdx >= 1 && tIdx <= userTodos.Length ->
                            let t = userTodos.[tIdx - 1]
                            match manager.DetachTodo (UserId u.Id) (TodoId t.Id) with
                            | Ok _ -> printfn "Detached Todo '%s' from User '%s'." t.Text u.Name
                            | Error e -> printfn "Error detaching Todo: %s" e
                        | _ -> printfn "Invalid Todo selection."
                | _ -> printfn "Invalid User selection."
            loop ()

        | "8" ->
            printfn "\nSelect User:"
            let users = listUsers ()
            if not users.IsEmpty then
                printf "User number: "
                match Int32.TryParse(Console.ReadLine()) with
                | true, uIdx when uIdx >= 1 && uIdx <= users.Length ->
                    let selected = users.[uIdx - 1]
                    printf "Enter new name for '%s': " selected.Name
                    let newName = Console.ReadLine()
                    if not (String.IsNullOrWhiteSpace newName) then
                        match manager.RenameUser (UserId selected.Id) newName with
                        | Ok _    -> printfn "[%s] Renamed to '%s' successfully." (DateTime.Now.ToString("HH:mm:ss")) newName
                        | Error e -> printfn "Rename failed: %s" e
                | _ -> printfn "Invalid User selection."
            loop ()

        | "9" ->
            Cache.AggregateCache3.Instance.ClearL2()
            Cache.DetailsCache.Instance.ClearL2()
            printfn "[%s] L2 cache cleared." (DateTime.Now.ToString("HH:mm:ss"))
            loop ()

        | "10" ->
            Cache.AggregateCache3.Instance.ClearL1()
            Cache.DetailsCache.Instance.ClearL1()
            printfn "[%s] L1 cache cleared." (DateTime.Now.ToString("HH:mm:ss"))
            loop ()

        | _ ->
            loop ()

    loop ()
