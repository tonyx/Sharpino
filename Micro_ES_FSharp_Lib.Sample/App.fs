namespace Tonyx.EventSourcing.Sample
open Tonyx.EventSourcing
open Tonyx.EventSourcing.Utils
open Tonyx.EventSourcing.Repository

open Tonyx.EventSourcing.Sample.TodosAggregate
open Tonyx.EventSourcing.Sample.Todos.TodoEvents
open Tonyx.EventSourcing.Sample.Todos.TodoCommands
open Tonyx.EventSourcing.Sample.Todos.Models.TodosModel

open Tonyx.EventSourcing.Sample.TagsAggregate
open Tonyx.EventSourcing.Sample.Tags.TagsEvents
open Tonyx.EventSourcing.Sample.Tags.TagCommands
open Tonyx.EventSourcing.Sample.Tags.Models.TagsModel

open Tonyx.EventSourcing.Sample_02
open Tonyx.EventSourcing.Sample_02.Categories
open Tonyx.EventSourcing.Sample_02.CategoriesAggregate
open Tonyx.EventSourcing.Sample_02.Categories.CategoriesCommands
open Tonyx.EventSourcing.Sample_02.Categories.CategoriesEvents
open System
open FSharpPlus

module App =
    let getAllTodos() =
        ceResult {
            let! (_, state) = getState<TodosAggregate, TodoEvent>()
            let todos = state.GetTodos()
            return todos
        }
    let addTodo todo =
        let lockobj = Conf.syncobjects |> Map.tryFind (TodosAggregate.Version,TodosAggregate.StorageName)
        if lockobj.IsNone then
            Error (sprintf "No lock object found for %A %A" TodosAggregate.Version TodosAggregate.StorageName)
        else
            lock lockobj <| fun () ->
                ceResult {              
                    let! (_, tagState) = getState<TagsAggregate, TagEvent>()
                    let tagIds = tagState.GetTags() |>> (fun x -> x.Id)

                    let! tagIdIsValid =    
                        (todo.TagIds.IsEmpty ||
                        todo.TagIds |> List.forall (fun x -> (tagIds |> List.contains x)))
                        |> boolToResult "A tag reference contained is in the todo is related to a tag that does not exist"

                    let! _ =
                        todo
                        |> TodoCommand.AddTodo
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
                |> TodoCommand.Add2Todos
                |> runCommand<TodosAggregate, TodoEvent> 
            let _ =  mksnapshotIfInterval<TodosAggregate, TodoEvent> ()
            return ()
        }
    let removeTodo id =
        ceResult {
            let! _ =
                id
                |> TodoCommand.RemoveTodo
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
                |> TodoCommand.AddCategory
                |> runCommand<TodosAggregate, TodoEvent> 
            let _ = mksnapshotIfInterval<TodosAggregate, TodoEvent> ()
            return ()
        }

    // version ready for upgrade as it uses the new aggregate
    let addCategory' category =
        ceResult {
            let! _ =
                category
                |> CategoryCommand.AddCategory
                |> runCommand<CategoriesAggregate, CategoryEvent>
            let _ = mksnapshotIfInterval<CategoriesAggregate, CategoryEvent> ()
            return ()
        }

    let removeCategory id = 
        ceResult {
            let! _ =
                id
                |> TodoCommand.RemoveCategory
                |> runCommand<TodosAggregate, TodoEvent> 
            let _ = mksnapshotIfInterval<TodosAggregate, TodoEvent> ()
            return ()
        }

    let removeCategory' id =
        ceResult {
            let! _ =
                id
                |> CategoryCommand.RemoveCategory
                |> runCommand<CategoriesAggregate, CategoryEvent>
            let _ = mksnapshotIfInterval<CategoriesAggregate, CategoryEvent> ()
            return ()
        }

    let removeCetegoryRef' id =
        ceResult {
            let! _ =
                id
                |> TodoCommand.RemoveCategoryRef
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
