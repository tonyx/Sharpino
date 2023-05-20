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
            lock (lockobj.Value) <| fun () ->
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

    let addTodo' todo =
        let lock1 = Conf.syncobjects |> Map.tryFind (TodosAggregate.Version,TodosAggregate.StorageName)
        let lock2 = Conf.syncobjects |> Map.tryFind (CategoriesAggregate.Version,CategoriesAggregate.StorageName)

        match (lock1, lock2) with
            Some lock1Val, Some lock2Val ->
                lock (lock1Val, lock2Val) <| fun () ->
                    ceResult {              
                        let! (_, tagState) = getState<TagsAggregate, TagEvent>()
                        let tagIds = tagState.GetTags() |>> (fun x -> x.Id)

                        let! (_, categoriesState) = getState<CategoriesAggregate, CategoryEvent>()
                        let categoryIds = categoriesState.GetCategories() |>> (fun x -> x.Id)

                        let! tagIdIsValid =    
                            (todo.TagIds.IsEmpty ||
                            todo.TagIds |> List.forall (fun x -> (tagIds |> List.contains x)))
                            |> boolToResult "A tag reference contained is in the todo is related to a tag that does not exist"

                        let! categoryIdIsValid =    
                            (todo.CategoryIds.IsEmpty ||
                            todo.CategoryIds |> List.forall (fun x -> (categoryIds |> List.contains x)))
                            |> boolToResult "A category reference contained is in the todo is related to a category that does not exist"

                        let! _ =
                            todo
                            |> TodoCommand.AddTodo
                            |> runCommand<TodosAggregate, TodoEvent> 
                        let _ =  mksnapshotIfInterval<TodosAggregate, TodoEvent> ()
                    return ()
                }
            | _ -> Error "No lock object found for TodosAggregate or CategoriesAggregate"

    let add2Todos (todo1, todo2) =
        let lockobj = Conf.syncobjects |> Map.tryFind (TodosAggregate.Version,TodosAggregate.StorageName)
        if lockobj.IsNone then
            Error (sprintf "No lock object found for %A %A" TodosAggregate.Version TodosAggregate.StorageName)
        else
            lock (lockobj.Value) <| fun () ->
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
    let add2Todos' (todo1, todo2) =
        let lockobj = Conf.syncobjects |> Map.tryFind (TodosAggregate.Version,TodosAggregate.StorageName)
        if lockobj.IsNone then
            Error (sprintf "No lock object found for %A %A" TodosAggregate.Version TodosAggregate.StorageName)
        else
            lock (lockobj.Value) <| fun () ->
                ceResult {
                    let! (_, tagState) = getState<TagsAggregate, TagEvent>()
                    let tagIds = tagState.GetTags() |>> (fun x -> x.Id)

                    let! (_, categoriesState) = getState<CategoriesAggregate, CategoryEvent>()
                    let categoryIds = categoriesState.GetCategories() |>> (fun x -> x.Id)

                    let! categoryId1IsValid =    
                        (todo1.CategoryIds.IsEmpty ||
                        todo1.CategoryIds |> List.forall (fun x -> (categoryIds |> List.contains x)))
                        |> boolToResult "A category reference contained is in the todo is related to a category that does not exist"

                    let! categoryId2IsValid =
                        (todo2.CategoryIds.IsEmpty ||
                        todo2.CategoryIds |> List.forall (fun x -> (categoryIds |> List.contains x)))
                        |> boolToResult "A category reference contained is in the todo is related to a category that does not exist" 

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

    let getAllCategories'() =
        ceResult {
            let! (_, state) = getState<CategoriesAggregate, CategoryEvent>()
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
            let removeCategory = CategoryCommand.RemoveCategory id
            let removeCategoryRef = TodoCommand.RemoveCategoryRef id
            let! _ = runTwoCommands<CategoriesAggregate, TodosAggregate, CategoryEvent, TodoEvent> removeCategory removeCategoryRef 
            let _ = mksnapshotIfInterval<CategoriesAggregate, CategoryEvent> ()
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

    let migrate() =
        ceResult {
            let! categoriesFrom = getAllCategories()
            let! _ = 
                categoriesFrom
                |> catchErrors addCategory'
            return () 
        }