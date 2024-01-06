
namespace Sharpino.Sample
open Sharpino
open Sharpino.Core
open Sharpino.Utils
open Sharpino.CommandHandler
open Sharpino.StateView
open Sharpino.Definitions

open Sharpino.Sample
open Sharpino.Storage
open Sharpino.Sample.Todos
open Sharpino.Sample.TodosContext
open Sharpino.Sample.Todos.TodoEvents
open Sharpino.Sample.Todos.TodoCommands
open Sharpino.Sample.Entities.Todos

open Sharpino.Sample.TagsContext
open Sharpino.Sample.Tags.TagsEvents
open Sharpino.Sample.Tags.TagCommands
open Sharpino.Sample.Entities.Tags
open Sharpino

open Sharpino.Sample.Categories
open Sharpino.Sample.CategoriesContext
open Sharpino.Sample.Categories.CategoriesCommands
open Sharpino.Sample.Categories.CategoriesEvents
open Sharpino.Sample.Entities.TodosReport
open Sharpino.Sample.Shared.Entities
open Sharpino.Sample.Converters
open Sharpino.StateView
open System
open FSharpPlus
open FsToolkit.ErrorHandling
open log4net
open Sharpino.KafkaReceiver

// todo: I need to refactor this class but I leave it as it is to get insights for the future
// basically I  need to "ping" each context/topic and then assign the offset to the consumer 
module EventBrokerBasedApp =

    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    // log4net.Config.BasicConfigurator.Configure() |> ignore
    type EventBrokerBasedApp
        (storage: IEventStore, eventBroker: IEventBroker) =
        let todoSubscriber = KafkaSubscriber.Create ("localhost:9092", TodosContext.Version, TodosContext.StorageName, "SharpinoClient") |> Result.get
        let mutable storageTodoStateViewer = getStorageFreshStateViewer<TodosContext, TodoEvent> storage


        // at the moment I need to "ping" and then assign the offset to the consumer for each context/topic (I am sure there must be something clever to do here)
        let pinged = 
            result {
                let! result =
                    TodoCommand.Ping()
                    |> runCommand<TodosContext, TodoEvent> storage eventBroker storageTodoStateViewer
                return result
            }
        let deliveryResult = pinged.OkValue |> snd |> List.head |> Option.get |> List.head
        let offSet = deliveryResult.Offset
        let partition = deliveryResult.Partition

        let _ = 
            todoSubscriber.Assign(offSet, partition) |> ignore
        let todosKafkaViewer = mkKafkaViewer<TodosContext, TodoEvent> todoSubscriber storageTodoStateViewer (ApplicationInstance.ApplicationInstance.Instance.GetGuid())
        let currentStateTodoKafkaViewer = 
            fun () -> 
                todosKafkaViewer.Refresh() |> ignore
                todosKafkaViewer.State () |> Result.Ok  

        let tagSubscriber = KafkaSubscriber.Create ("localhost:9092", TagsContext.Version, TagsContext.StorageName, "SharpinoClient") |> Result.get 
        let mutable storageTagStateViewer = getStorageFreshStateViewer<TagsContext, TagEvent> storage

        // at the moment I need to "ping" and then assign the offset to the consumer for each context/topic (I am sure there must be something clever to do here)
        let pinged = 
            result {
                let! result =
                    TagCommand.Ping()
                    |> runCommand<TagsContext, TagEvent> storage eventBroker storageTagStateViewer
                return result
            }
        let deliveryResult = pinged.OkValue |> snd |> List.head |> Option.get |> List.head  
        let offSet = deliveryResult.Offset
        let partition = deliveryResult.Partition
        let _ =
            tagSubscriber.Assign(offSet, partition) |> ignore

        let tagsKafkaViewer = mkKafkaViewer<TagsContext, TagEvent> tagSubscriber storageTagStateViewer (ApplicationInstance.ApplicationInstance.Instance.GetGuid())
        let currentStateTagKafkaViewer = 
            fun () -> 
                tagsKafkaViewer.Refresh() |> ignore
                tagsKafkaViewer.State () |> Result.Ok
        member this._eventBroker = eventBroker

        member this.PingTodo() =
            result {
                let! result =
                    TodoCommand.Ping()
                    |> runCommand<TodosContext, TodoEvent> storage eventBroker currentStateTodoKafkaViewer
                return result
            }
        member this.PingTag() =
            result {
                let! result =
                    TagCommand.Ping()
                    |> runCommand<TagsContext, TagEvent> storage eventBroker currentStateTagKafkaViewer
                return result
            }
        member this.PingCategory() =
            failwith "unimplemented"
            result {
                let! result =
                    TodoCommand.Ping()
                    |> runCommand<TodosContext, TodoEvent> storage eventBroker currentStateTodoKafkaViewer
                return result
            }

        member this.GetAllTodos() =
            result  {
                let! (_, state, _, _) = currentStateTodoKafkaViewer()
                return state.GetTodos()
            }

        member this.AddTodo todo =

            lock (TodosContext.Lock, TagsContext.Lock) (fun () -> 
                result {
                    let! (_, tagState, _, _) = currentStateTagKafkaViewer ()
                    let tagIds = tagState.GetTags() |>> (fun x -> x.Id)

                    let! tagIdIsValid =    
                        (todo.TagIds.IsEmpty ||
                        todo.TagIds |> List.forall (fun x -> (tagIds |> List.contains x)))
                        |> boolToResult "A tag reference contained in the todo is related to a tag that does not exist"

                    let! result =
                        todo
                        |> TodoCommand.AddTodo
                        |> runCommand<TodosContext, TodoEvent> storage eventBroker currentStateTodoKafkaViewer
                    return result
                }
            )

        member this.Add2Todos (todo1, todo2) =

            lock (TodosContext.Lock, TagsContext.Lock) (fun () -> 
                result {
                    let! (_, tagState, _, _) = currentStateTagKafkaViewer ()
                    let tagIds = tagState.GetTags() |>> (fun x -> x.Id)

                    let! tagId1IsValid =  
                        (todo1.TagIds.IsEmpty ||
                        todo1.TagIds |> List.forall (fun x -> (tagIds |> List.contains x)))
                        |> boolToResult "A tag reference contained in the todo is related to a tag that does not exist"

                    let! tagId2IsValid =    
                        (todo2.TagIds.IsEmpty ||
                        todo2.TagIds |> List.forall (fun x -> (tagIds |> List.contains x)))
                        |> boolToResult "A tag reference contained in the todo is related to a tag that does not exist"

                    let! result =
                        (todo1, todo2)
                        |> TodoCommand.Add2Todos
                        |> runCommand<TodosContext, TodoEvent> storage eventBroker currentStateTodoKafkaViewer
                    return result
                }
            )

        member this.RemoveTodo id =
            result {
                let! result =
                    id
                    |> TodoCommand.RemoveTodo
                    |> runCommand<TodosContext, TodoEvent> storage eventBroker currentStateTodoKafkaViewer
                return result 
            }
        member this.GetAllCategories() =
            result  {
                let! (_, state, _, _) = currentStateTodoKafkaViewer()
                return state.GetCategories()
            }

        member this.AddCategory category =
            result {
                let! result =
                    category
                    |> TodoCommand.AddCategory
                    |> runCommand<TodosContext, TodoEvent> storage eventBroker currentStateTodoKafkaViewer
                return result
            }

        member this.RemoveCategory id = 
            result {
                let! result =
                    id
                    |> TodoCommand.RemoveCategory
                    |> runCommand<TodosContext, TodoEvent> storage eventBroker currentStateTodoKafkaViewer
                return result 
            }

        member this.AddTag tag =

            result {
                let! result =
                    tag
                    |> AddTag
                    |> runCommand<TagsContext, TagEvent> storage eventBroker currentStateTagKafkaViewer
                return result 
            }

        member this.RemoveTag id =
            result {
                let removeTag = TagCommand.RemoveTag id
                let removeTagRef = TodoCommand.RemoveTagRef id
                let! result = runTwoCommands<TagsContext, TodosContext, TagEvent, TodoEvent> storage eventBroker removeTag removeTagRef currentStateTagKafkaViewer currentStateTodoKafkaViewer
                return result
            }

        member this.GetAllTags () =
            result  {
                let! (_, state, _, _) = currentStateTagKafkaViewer()
                return state.GetTags()
            }

        member this.TodoReport (dateFrom: DateTime)  (dateTo: DateTime) =
            let events = storage.GetEventsInATimeInterval TodosContext.Version TodosContext.StorageName dateFrom dateTo |>> snd
            result {
                let! events'' = 
                    events |> catchErrors (serializer.Deserialize)
                return 
                    { InitTime = dateFrom; EndTime = dateTo; TodoEvents = events'' }
            }

