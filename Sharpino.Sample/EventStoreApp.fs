
namespace Sharpino.Sample

open Sharpino
open Sharpino.Utils
open Sharpino.LightCommandHandler

open Sharpino.Sample
open Sharpino.Storage
open Sharpino.Sample.Todos
open Sharpino.Sample.TodosCluster
open Sharpino.Sample.Todos.TodoEvents
open Sharpino.Sample.Todos.TodoCommands
open Sharpino.Sample.Entities.Todos

open Sharpino.Sample.TagsCluster
open Sharpino.Sample.Tags.TagsEvents
open Sharpino.Sample.Tags.TagCommands
open Sharpino.Sample.Entities.Tags
open Sharpino.Sample.Entities.TodosReport
open Sharpino.Sample.Shared.Entities

open Sharpino.Sample.Categories
open Sharpino.Sample.CategoriesCluster
open Sharpino.Sample.Categories.CategoriesCommands
open Sharpino.Sample.Categories.CategoriesEvents
open System
open FSharpPlus
open FsToolkit.ErrorHandling
open log4net
open log4net.Config

module EventStoreApp =
    let log = LogManager.GetLogger(Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    type EventStoreApp(storage: ILightStorage) =
        member this.AddTag tag =
            result {
                return!
                    tag
                    |> TagCommand.AddTag
                    |> runCommand<TagsCluster, TagEvent> storage
            }
                
        member this.GetAllTags() =
            result {
                
                let! (_, stateX ) = storage |> getState<TagsCluster, TagEvent >

                let tags = stateX.GetTags()
                return tags
            }

        member this.GetAllTodos() =
            result {
                let! (_, state') = storage |> getState<TodosCluster, TodoEvents.TodoEvent>
                let todos = state'.GetTodos()
                return todos
            }

        member this.AddTodo todo =
            result {
                let! (_, tagState' ) = storage |> getState<TagsCluster, TagEvent>
                let tagIds = tagState'.GetTags() |>> fun x -> x.Id
                    
                let! tagIdIsValid = 
                    (todo.TagIds.IsEmpty || 
                    (todo.TagIds |> List.forall (fun x -> tagIds |> List.contains x)))
                    |> boolToResult "Tag id is not valid"

                let! _ =
                    todo
                    |> TodoCommand.AddTodo
                    |> runCommand<TodosCluster, TodoEvent> storage

                let _ =
                    storage |> mkSnapshotIfIntervalPassed<TodosCluster, TodoEvent> 
                return ()
            }

        member this.RemoveTodo id =
            let f = fun() ->
                result {
                    return!
                        id
                        |> TodoCommand.RemoveTodo
                        |> runCommand<TodosCluster, TodoEvent> storage
                }
            async {
                return lightProcessor.PostAndReply (fun rc -> f, rc)
            }
            |> Async.RunSynchronously

        member this.AddCategory category =
            let f = fun() ->
                result {
                    return!
                        category
                        |> TodoCommand.AddCategory
                        |> runCommand<TodosCluster, TodoEvent> storage
                }
            async {
                return lightProcessor.PostAndReply (fun rc -> f, rc)
            }
            |> Async.RunSynchronously

        member this.GetAllCategories() =
            result {
                let! (_, state' ) = storage |> getState<TodosCluster, TodoEvents.TodoEvent>
                let categories = state'.GetCategories()
                return categories
            }

        member this.RemoveCategory id =
            let f = fun() ->
                result {
                    return! 
                        id
                        |> TodoCommand.RemoveCategory
                        |> runCommand<TodosCluster, TodoEvent> storage
                }
            async { 
                return lightProcessor.PostAndReply (fun rc -> f, rc)
            }   
            |> Async.RunSynchronously

        member this.RemoveTag id =
            let f = fun() ->
                result {
                    let removeTag = TagCommand.RemoveTag id
                    let removeTagRef = TodoCommand.RemoveTagRef id
                    return! runTwoCommands<TagsCluster, TodosCluster, TagEvent, TodoEvent> storage removeTag removeTagRef
                }
            async { 
                return lightProcessor.PostAndReply (fun rc -> f, rc)
            }   
            |> Async.RunSynchronously
        member this.RemoveTagFakingErrorOnSecondCommand id =
            let f = fun() ->
                result {
                    let removeTag = TagCommand.RemoveTag id
                    let removeTagRef = TodoCommand.RemoveTagRef id
                    return! runTwoCommandsWithFailure_USE_IT_ONLY_TO_TEST_THE_UNDO<TagsCluster, TodosCluster, TagEvent, TodoEvent> storage removeTag removeTagRef
                }
            async { 
                return lightProcessor.PostAndReply (fun rc -> f, rc)
            }   
            |> Async.RunSynchronously

        member this.Add2Todos (todo1, todo2) =
            let f = fun() ->
                result {
                    let! (_, tagState' ) = storage |> getState<TagsCluster, TagEvent>
                    let tagIds = 
                        tagState'.GetTags() 
                        |> List.map (fun x -> x.Id)
                    let! tagId1IsValid =
                        (todo1.TagIds.IsEmpty || 
                        (todo1.TagIds |> List.forall (fun x -> tagIds |> List.contains x)))
                        |> boolToResult "Tag id is not valid"
                    let! tagId2IsValid =
                        (todo2.TagIds.IsEmpty || 
                        (todo2.TagIds |> List.forall (fun x -> tagIds |> List.contains x)))
                        |> boolToResult "Tag id is not valid"

                    return! 
                        (todo1, todo2)
                        |> TodoCommand.Add2Todos
                        |> runCommand<TodosCluster, TodoEvent> storage
                }
            async {
                return lightProcessor.PostAndReply (fun rc -> f, rc)
            }
            |> Async.RunSynchronously

        member this.TodoReport (dateFrom: DateTime) (dateTo: DateTime) =
            try
                let events = storage.ConsumeEventsInATimeInterval TodosCluster.Version TodosCluster.StorageName dateFrom dateTo |>> snd
                let result = {InitTime = dateFrom; EndTime = dateTo; TodoEvents = events}
                result
            with _ as ex ->
                log.Error (sprintf "error: %A\n" ex)
                {InitTime = dateFrom; EndTime = dateTo; TodoEvents = []}