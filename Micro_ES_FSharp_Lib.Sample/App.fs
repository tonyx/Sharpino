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
    let getAllTodos'() =
        ceResult {
            let! (_, state) = getState<Tonyx.EventSourcing.Sample_02.TodosAggregate.TodosAggregate, Tonyx.EventSourcing.Sample_02.Todos.TodoEvents.TodoEvent>()
            let todos = state.GetTodos()
            return todos
        }
    let addTodo todo =
        let lockobj1 = Conf.syncobjects |> Map.tryFind (TodosAggregate.Version,TodosAggregate.StorageName)
        let lockobj2 = Conf.syncobjects |> Map.tryFind (TagsAggregate.Version,TagsAggregate.StorageName)

        match lockobj1, lockobj2 with
        | Some lock1, Some lock2 ->
            lock (lock1, lock2) <| fun () ->
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
        | _ -> Error "No lock object found for TodosAggregate or TagsAggregate"

    let addTodo' todo =
        let lock1 = Conf.syncobjects |> Map.tryFind (Tonyx.EventSourcing.Sample_02.TodosAggregate.TodosAggregate.Version,Tonyx.EventSourcing.Sample_02.TodosAggregate.TodosAggregate.StorageName)
        let lock2 = Conf.syncobjects |> Map.tryFind (CategoriesAggregate.Version,CategoriesAggregate.StorageName)
        let lockobj2 = Conf.syncobjects |> Map.tryFind (TagsAggregate.Version,TagsAggregate.StorageName)

        match (lock1, lock2) with
            Some lock1Val, Some lock2Val ->
                lock (lock1Val, lock2Val) <| fun () ->
                    ceResult {              
                        let! (_, tagState) = getState<TagsAggregate, TagEvent>()
                        let tagIds = tagState.GetTags() |>> (fun x -> x.Id)

                        let! (_, categoriesState) = getState<Tonyx.EventSourcing.Sample_02.CategoriesAggregate.CategoriesAggregate, Tonyx.EventSourcing.Sample_02.Categories.CategoriesEvents.CategoryEvent>()
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
                            |> Tonyx.EventSourcing.Sample_02.Todos.TodoCommands.TodoCommand.AddTodo
                            |> runCommand<Tonyx.EventSourcing.Sample_02.TodosAggregate.TodosAggregate, Tonyx.EventSourcing.Sample_02.Todos.TodoEvents.TodoEvent> 
                        let _ =  mksnapshotIfInterval<Tonyx.EventSourcing.Sample_02.TodosAggregate.TodosAggregate, Tonyx.EventSourcing.Sample_02.Todos.TodoEvents.TodoEvent> ()
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
        let lockobj = Conf.syncobjects |> Map.tryFind (Tonyx.EventSourcing.Sample_02.TodosAggregate.TodosAggregate.Version, Tonyx.EventSourcing.Sample_02.TodosAggregate.TodosAggregate.StorageName)
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
                        |> Tonyx.EventSourcing.Sample_02.Todos.TodoCommands.TodoCommand.Add2Todos
                        |> runCommand<Tonyx.EventSourcing.Sample_02.TodosAggregate.TodosAggregate, Tonyx.EventSourcing.Sample_02.Todos.TodoEvents.TodoEvent> 
                    let _ =  mksnapshotIfInterval<Tonyx.EventSourcing.Sample_02.TodosAggregate.TodosAggregate, Tonyx.EventSourcing.Sample_02.Todos.TodoEvents.TodoEvent> ()
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
    let removeTodo' id =
        ceResult {
            let! _ =
                id
                |> Tonyx.EventSourcing.Sample_02.Todos.TodoCommands.RemoveTodo
                |> runCommand<Tonyx.EventSourcing.Sample_02.TodosAggregate.TodosAggregate, Tonyx.EventSourcing.Sample_02.Todos.TodoEvents.TodoEvent> 
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
                |> Tonyx.EventSourcing.Sample_02.Categories.CategoriesCommands.CategoryCommand.AddCategory
                |> runCommand<Tonyx.EventSourcing.Sample_02.CategoriesAggregate.CategoriesAggregate, Tonyx.EventSourcing.Sample_02.Categories.CategoriesEvents.CategoryEvent>
            let _ = mksnapshotIfInterval<Tonyx.EventSourcing.Sample_02.CategoriesAggregate.CategoriesAggregate, Tonyx.EventSourcing.Sample_02.Categories.CategoriesEvents.CategoryEvent> ()
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
            printf "removeCategory'\n"
            let removeCategory = CategoryCommand.RemoveCategory id
            let removeCategoryRef = Tonyx.EventSourcing.Sample_02.Todos.TodoCommands.TodoCommand.RemoveCategoryRef id
            printf "abc:\n"
            let uuu = runTwoCommands<Tonyx.EventSourcing.Sample_02.CategoriesAggregate.CategoriesAggregate, Tonyx.EventSourcing.Sample_02.TodosAggregate.TodosAggregate, Tonyx.EventSourcing.Sample_02.Categories.CategoriesEvents.CategoryEvent, Tonyx.EventSourcing.Sample_02.Todos.TodoEvents.TodoEvent> removeCategory removeCategoryRef 
            printf "uuu: %A\n" uuu
            let! _ = uuu
            let _ = mksnapshotIfInterval<Tonyx.EventSourcing.Sample_02.CategoriesAggregate.CategoriesAggregate, Tonyx.EventSourcing.Sample_02.Categories.CategoriesEvents.CategoryEvent> ()
            let _ = mksnapshotIfInterval<Tonyx.EventSourcing.Sample_02.TodosAggregate.TodosAggregate, Tonyx.EventSourcing.Sample_02.Todos.TodoEvents.TodoEvent> ()   
            printf "returning\n"
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
    let removeTag' id =
        ceResult {
            let removeTag = TagCommand.RemoveTag id
            let removeTagRef = Tonyx.EventSourcing.Sample_02.Todos.TodoCommands.RemoveTagRef id
            let! _ = runTwoCommands<TagsAggregate, Tonyx.EventSourcing.Sample_02.TodosAggregate.TodosAggregate, TagEvent, Tonyx.EventSourcing.Sample_02.Todos.TodoEvents.TodoEvent> removeTag removeTagRef
            let _ = mksnapshotIfInterval<TagsAggregate, TagEvent> ()
            let _ = mksnapshotIfInterval<Tonyx.EventSourcing.Sample_02.TodosAggregate.TodosAggregate, Tonyx.EventSourcing.Sample_02.Todos.TodoEvents.TodoEvent> ()
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