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
open Sharpino.Template.Commons
open Sharpino.MemoryStorage

open FsToolkit.ErrorHandling
open System

type TodoManager (messageSenders: MessageSenders, eventStore: IEventStore<string>, todosViewer: AggregateViewer<Todo>) =
    new() =
        let memoryStorage = new MemoryStorage()
        let viewer: AggregateViewer<Todo> = fun id -> getAggregateStorageFreshStateViewer<Todo, TodoEvents, string> memoryStorage id
        TodoManager(NoSender, memoryStorage, viewer)
    
    member this.AddTodo (todo: Todo) =
        result
            {
                return!
                    todo
                    |> runInit<Todo, TodoEvents, string> eventStore  messageSenders 
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

