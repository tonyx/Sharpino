namespace Tonyx.EventSourcing.Sample_02
open Tonyx.EventSourcing.Utils
open Tonyx.EventSourcing.Repository

open Tonyx.EventSourcing.Sample_02.TodosAggregate
open Tonyx.EventSourcing.Sample_02.Todos.TodoEvents
open Tonyx.EventSourcing.Sample_02.Todos.TodoCommands
open Tonyx.EventSourcing.Sample_02.Todos.Models.TodosModel

open Tonyx.EventSourcing.Sample_02.TagsAggregate
open Tonyx.EventSourcing.Sample_02.Tags.TagsEvents
open Tonyx.EventSourcing.Sample_02.Tags.TagCommands
open Tonyx.EventSourcing.Sample_02.Tags.Models.TagsModel
open System
open FSharpPlus

module App_02 =
    let getAllTodos() =
        ceResult {
            let! (_, state) = getState<TodosAggregate, TodoEvent>()
            let todos = state.GetTodos()
            return todos
        }
    let addTodo todo =
        ceResult {
            let! (_, tagState) = getState<TagsAggregate, TagEvent>()
            let tagIds = tagState.GetTags() |>> (fun x -> x.Id)

            let! tagIdIsValid =    
                (todo.TagIds.IsEmpty ||
                todo.TagIds |> List.forall (fun x -> (tagIds |> List.contains x)))
                |> boolToResult "A tag reference contained is in the todo is related to a tag that does not exist"

            let! _ =
                todo
                |> AddTodo
                |> runCommand<TodosAggregate, TodoEvent> 
            let _ =  mksnapshotIfInterval<TodosAggregate, TodoEvent> ()
            return ()
        }
    let add2Todos (todo1, todo2) =
        ceResult {
            let! (_, tagState) = getState<TagsAggregate, TagEvent>()
            let tagIds = tagState.GetTags() |>> (fun x -> x.Id)

            let! tagId1IsValid =    
                (todo1.TagIds.IsEmpty ||
                todo1.TagIds |> List.forall (fun x -> (tagIds |> List.contains x)))
                |> boolToResult "A tag reference contained is in the todo is related to a tag that does not exist"

            let! tagId2IsValid =    
                (todo2.TagIds.IsEmpty ||
                todo2.TagIds |> List.forall (fun x -> (tagIds |> List.contains x)))
                |> boolToResult "A tag reference contained is in the todo is related to a tag that does not exist"

            let! _ =
                (todo1, todo2)
                |> Add2Todos
                |> runCommand<TodosAggregate, TodoEvent> 
            let _ =  mksnapshotIfInterval<TodosAggregate, TodoEvent> ()
            return ()
        }
    let removeTodo id =
        ceResult {
            let! _ =
                id
                |> RemoveTodo
                |> runCommand<TodosAggregate, TodoEvent> 
            let _ = mksnapshotIfInterval<TodosAggregate, TodoEvent> ()
            return ()
        }

    let getAllCategories() =
        ceResult {
            let! (_, state) = getState<TodosAggregate, TodoEvent>()
            let categories = state.GetCategories()
            return categories
        }
    let addCategory category =
        ceResult {
            let! _ =
                category
                |> AddCategory
                |> runCommand<TodosAggregate, TodoEvent> 
            let _ = mksnapshotIfInterval<TodosAggregate, TodoEvent> ()
            return ()
        }

    let removeCategory id = 
        ceResult {
            let! _ =
                id
                |> RemoveCategory
                |> runCommand<TodosAggregate, TodoEvent> 
            let _ = mksnapshotIfInterval<TodosAggregate, TodoEvent> ()
            return ()
        }

    let addTag tag =
        ceResult {
            let! _ =
                tag
                |> AddTag
                |> runCommand<TagsAggregate, TagEvent> 
            let _ = mksnapshotIfInterval<TagsAggregate, TagEvent> ()
            return ()
        }

    let removeTag id =
        ceResult {
            let removeTag = TagCommand.RemoveTag id
            let removeTagRef = TodoCommand.RemoveTagRef id
            let! _ = runTwoCommands<TagsAggregate, TodosAggregate, TagEvent, TodoEvent> removeTag removeTagRef
            let _ = mksnapshotIfInterval<TagsAggregate, TagEvent> ()
            let _ = mksnapshotIfInterval<TodosAggregate, TodoEvent> ()
            return ()
        }

    let getAllTags () =
        ceResult {
            let! (_, state) = getState<TagsAggregate, TagEvent>()
            let tags = state.GetTags()
            return tags
        }
