namespace Sharpino.Template

open System.Threading
open FsToolkit.ErrorHandling
open Sharpino
open Sharpino.Cache
open FSharpPlus.Operators
open Sharpino.CommandHandler
open Sharpino.Core
open Sharpino.EventBroker
open Sharpino.Storage

open Sharpino.Template.Models
open Sharpino.Template.Models.Details
open Sharpino.Template.Commons
open Sharpino.MemoryStorage

open FsToolkit.ErrorHandling
open System

module TodosManager =

    type TodoManager (messageSenders: MessageSenders, eventStore: IEventStore<string>, todosViewer: AggregateViewer<Todo>, usersViewer: AggregateViewer<User>) =
        new() =
            let memoryStorage = new MemoryStorage()
            let todosViewer: AggregateViewer<Todo> = fun id -> getAggregateStorageFreshStateViewer<Todo, TodoEvents, string> memoryStorage id
            let usersViewer: AggregateViewer<User> = fun id -> getAggregateStorageFreshStateViewer<User, UserEvents, string> memoryStorage id
            TodoManager(NoSender, memoryStorage, todosViewer, usersViewer)
        
        member this.AddTodo (todo: Todo) =
            result
                {
                    return!
                        todo
                        |> runInit<Todo, TodoEvents, string> eventStore  messageSenders 
                }
        member this.AddUser (user: User) =
            result
                {
                    return!
                        user
                        |> runInit<User, UserEvents, string> eventStore messageSenders
                }

        member this.AssignTodo (userId: UserId) (todoId: TodoId) =
            result
                {
                    return!
                        UserCommands.AssignTodo todoId
                        |> runAggregateCommand<User, UserEvents, string> userId.Value eventStore messageSenders
                }

        member this.DetachTodo (userId: UserId) (todoId: TodoId) =
            result
                {
                    return!
                        UserCommands.DetachTodo todoId
                        |> runAggregateCommand<User, UserEvents, string> userId.Value eventStore messageSenders
                }

        member this.GetAllTodos () =
            result
                {
                    let! todos = StateView.getAggregateStatesInATimeInterval<Todo, TodoEvents, string> eventStore DateTime.MinValue DateTime.MaxValue
                    return (todos |>> snd)
                }


        member this.GetTodosOfUser (userId: UserId) =
            result
                {
                    let! _, user = usersViewer userId.Value
                    let! todos = 
                        user.Todos 
                        |> List.map (fun todoId -> todoId.Value |> todosViewer) 
                        |> Result.sequence

                    return todos |>> snd  |> List.ofArray
                }

        member this.GetUserDetails (userId: UserId) =
            let detailsBuilder =
                fun () ->
                    let refresher =
                        fun () ->
                            result
                                {
                                    let! _, user = usersViewer userId.Value
                                    let! todos = 
                                        this.GetTodosOfUser userId
                                    return (user, todos)
                                }
                    result {
                        let! user, todos = refresher ()
                        return
                            {
                                User = user
                                Todos = todos
                                Refresher = refresher
                            } 
                            :> Refreshable<UserDetails>
                            ,
                            userId.Value:: (todos |> List.map _.Id)
                    }
            let key = DetailsCacheKey.OfType typeof<UserDetails> userId.Value 
            StateView.getRefreshableDetails<UserDetails> detailsBuilder key 

        member this.RenameTodo (id: TodoId) (newName: string) =
            result
                {
                    return!
                        TodoCommands.Rename newName
                        |> runAggregateCommand<Todo, TodoEvents, string> id.Value eventStore messageSenders
                }

        member this.RenameUser (id: UserId) (newName: string) =
            result
                {
                    return!
                        UserCommands.Rename newName
                        |> runAggregateCommand<User, UserEvents, string> id.Value eventStore messageSenders
                }

        member this.Start (id: TodoId) =
            result
                {
                    return!  
                        Activate DateTime.Now
                        |> runAggregateCommand<Todo, TodoEvents, string> id.Value eventStore messageSenders
                }
        member this.Complete (id: TodoId) =
            result
                {
                    return! 
                        Complete DateTime.Now
                        |> runAggregateCommand<Todo, TodoEvents, string> id.Value eventStore messageSenders 
                }
        member this.GetTodo (id: TodoId) =
            result
                {
                    let! _, result = todosViewer id.Value
                    return result
                }
                
        member this.DeleteTodo (id: TodoId) =
            result
                {
                    return!
                        runDelete<Todo, TodoEvents, string> eventStore messageSenders id.Value (fun _ -> true)
                }

        member this.GetTodosAsync (?ct: CancellationToken) =
            taskResult
                {
                    let ct = defaultArg ct CancellationToken.None
                    let! todos = StateView.getAggregateStatesInATimeIntervalAsync<Todo, TodoEvents, string> eventStore DateTime.MinValue DateTime.MaxValue  (ct |> Some)
                    return 
                        todos 
                        |>> snd 
                }

