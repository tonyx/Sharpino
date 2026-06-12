namespace Sharpino.Template

open System.Threading
open System.Threading.Tasks
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
open Sharpino.Template.Models.DetailsRefactor
open Sharpino.Template.Commons
open Sharpino.MemoryStorage

open FsToolkit.ErrorHandling
open System

module TodosManager =

    type AggregateViewerAsync2<'A> = Option<CancellationToken> -> Guid -> Task<Result<int * 'A,string>>

    type TodoManager 
        (messageSenders: MessageSenders, 
        eventStore: IEventStore<string>, 
        todosViewer: AggregateViewer<Todo>, 
        usersViewer: AggregateViewer<User>,
        todosViewerAsync: AggregateViewerAsync2<Todo>,
        usersViewerAsync: AggregateViewerAsync2<User>
        ) =

        new() =
            let memoryStorage = new MemoryStorage()
            let todosViewer: AggregateViewer<Todo> = fun id -> getAggregateStorageFreshStateViewer<Todo, TodoEvents, string> memoryStorage id
            let usersViewer: AggregateViewer<User> = fun id -> getAggregateStorageFreshStateViewer<User, UserEvents, string> memoryStorage id
            let todosViewerAsync = getAggregateStorageFreshStateViewerAsync<Todo, TodoEvents, string> memoryStorage 
            let usersViewerAsync = getAggregateStorageFreshStateViewerAsync<User, UserEvents, string> memoryStorage 
            TodoManager(NoSender, memoryStorage, todosViewer, usersViewer, todosViewerAsync, usersViewerAsync)
        
        member this.AddTodo (todo: Todo) =
            result
                {
                    return!
                        todo
                        |> runInit<Todo, TodoEvents, string> eventStore  messageSenders 
                }

        member this.AddTodoAsync (todo: Todo) (ct: Option<CancellationToken>) =
            taskResult
                {
                    return!
                        runInitAsync<Todo, TodoEvents, string> eventStore messageSenders todo ct
                }

        member this.AddUser (user: User) =
            result
                {
                    return!
                        user
                        |> runInit<User, UserEvents, string> eventStore messageSenders
                }

        member this.AddUserAsync (user: User) (ct: Option<CancellationToken>) =
            taskResult
                {
                    return!
                        runInitAsync<User, UserEvents, string> eventStore messageSenders user ct
                }

        member this.AssignTodo (userId: UserId) (todoId: TodoId) =
            result
                {
                    return!
                        UserCommands.AssignTodo todoId
                        |> runAggregateCommand<User, UserEvents, string> userId.Value eventStore messageSenders
                }

        member this.AssignTodoAsync (userId: UserId) (todoId: TodoId) (ct: Option<CancellationToken>) =
            taskResult {
                let command = UserCommands.AssignTodo todoId
                return! 
                    runAggregateCommandMdAsync<User, UserEvents, string> userId.Value eventStore messageSenders "" command ct
            }

        member this.DetachTodo (userId: UserId) (todoId: TodoId) =
            result
                {
                    return!
                        UserCommands.DetachTodo todoId
                        |> runAggregateCommand<User, UserEvents, string> userId.Value eventStore messageSenders
                }

        member this.DetachTodoAsync (userId: UserId) (todoId: TodoId) (ct: Option<CancellationToken>) =
            taskResult
                {
                    let command = UserCommands.DetachTodo todoId
                    return!
                        runAggregateCommandMdAsync<User, UserEvents, string> userId.Value eventStore messageSenders "" command ct
                }

        member this.GetAllTodos () =
            result
                {
                    let! todos = StateView.getAggregateStatesInATimeInterval<Todo, TodoEvents, string> eventStore DateTime.MinValue DateTime.MaxValue
                    return (todos |>> snd)
                }

        member this.GetAllTodosAsync (ct: Option<CancellationToken>) =
            taskResult
                {
                    let! todos = StateView.getAggregateStatesInATimeIntervalAsync<Todo, TodoEvents, string> eventStore DateTime.MinValue DateTime.MaxValue ct
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

        member this.GetTodosOfUserAsync (userId: UserId) (ct: Option<CancellationToken>) =
            taskResult {
                let! _, user = usersViewerAsync ct userId.Value
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
                            :> RefreshableAsync<UserDetails>
                            ,
                            userId.Value:: (todos |> List.map _.Id)
                    }
            let key = DetailsCacheKey.OfType typeof<UserDetails> userId.Value 
            StateView.getRefreshableDetails<UserDetails> detailsBuilder key


        member this.GetRefreshableUserDetailsAsync (userId: UserId) (ct: Option<CancellationToken>) =
            let detailsBuilder =
                fun (ct: Option<CancellationToken>) ->
                    let refresher =
                        fun (ct: Option<CancellationToken>) ->
                            taskResult
                                {
                                    let! _, user = usersViewerAsync ct userId.Value
                                    let! todos = 
                                        this.GetTodosOfUserAsync userId ct
                                    return 
                                        {
                                            User = user

                                            Todos = todos
                                        } 
                                }
                    taskResult {
                        let! userDetails = refresher ct
                        return
                            {
                                UserDetails = userDetails
                                RefresherAsync = refresher
                            } 
                            :> RefreshableAsync<RefreshableUserDetailsAsync>
                            ,
                            userId.Value:: (userDetails.Todos |> List.map _.Id)
                    }
            let key = DetailsCacheKey.OfType typeof<RefreshableUserDetailsAsync> userId.Value 
            StateView.getRefreshableDetailsTaskResultAsync<RefreshableUserDetailsAsync> (fun ct -> detailsBuilder ct) key ct

        member this.RenameTodo (id: TodoId) (newName: string) =
            result
                {
                    return!
                        TodoCommands.Rename newName
                        |> runAggregateCommand<Todo, TodoEvents, string> id.Value eventStore messageSenders
                }

        member this.RenameTodoAsync (id: TodoId) (newName: string) (ct: Option<CancellationToken>)=
            taskResult
                {
                    let command = TodoCommands.Rename newName
                    return!
                        runAggregateCommandMdAsync<Todo, TodoEvents, string> id.Value eventStore messageSenders "" command ct
                }


        member this.RenameUser (id: UserId) (newName: string) =
            result
                {
                    return!
                        UserCommands.Rename newName
                        |> runAggregateCommand<User, UserEvents, string> id.Value eventStore messageSenders
                }

        member this.RenameUserAsync (id: UserId) (newName: string) (ct: Option<CancellationToken>)= 
            taskResult {
                let command = UserCommands.Rename newName
                return! 
                    runAggregateCommandMdAsync<User, UserEvents, string> id.Value eventStore messageSenders "" command ct
            }

        member this.Start (id: TodoId) =
            result
                {
                    return!  
                        Activate DateTime.Now
                        |> runAggregateCommand<Todo, TodoEvents, string> id.Value eventStore messageSenders
                }

        member this.StartAsync (id: TodoId) (ct: Option<CancellationToken>)= 
            taskResult {
                let command = Activate DateTime.Now
                return! 
                    runAggregateCommandMdAsync<Todo, TodoEvents, string> id.Value eventStore messageSenders "" command ct
            }


        member this.Complete (id: TodoId) =
            result
                {
                    return! 
                        Complete DateTime.Now
                        |> runAggregateCommand<Todo, TodoEvents, string> id.Value eventStore messageSenders 
                }

        member this.CompleteAsync (id: TodoId) (ct: Option<CancellationToken>)= 
            taskResult {
                let command = Complete DateTime.Now
                return! 
                    runAggregateCommandMdAsync<Todo, TodoEvents, string> id.Value eventStore messageSenders "" command ct
            }

        member this.GetTodo (id: TodoId) =
            result
                {
                    let! _, result = todosViewer id.Value
                    return result
                }

        member this.GetTodoAsync (id: TodoId) (ct: Option<CancellationToken>)= 
            taskResult {
                let! _, result = todosViewer id.Value
                return result
            }

        member this.DeleteTodo (id: TodoId) =
            result
                {
                    return!
                        runDelete<Todo, TodoEvents, string> eventStore messageSenders id.Value (fun _ -> true)
                }
                
        member this.DeleteTodoAsync (id: TodoId) (ct: Option<CancellationToken>)= 
            taskResult {
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

