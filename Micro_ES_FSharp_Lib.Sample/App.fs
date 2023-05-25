namespace Tonyx.EventSourcing.Sample
open Tonyx.EventSourcing
open Tonyx.EventSourcing.Utils
open Tonyx.EventSourcing.Repository

open Tonyx.EventSourcing.Sample.TodosAggregate
open Tonyx.EventSourcing.Sample.Todos.TodoEvents
open Tonyx.EventSourcing.Sample.Todos.TodoCommands
open Tonyx.EventSourcing.Sample.Todos.Models.TodosModel
open Tonyx.EventSourcing.Sample.Todos

open Tonyx.EventSourcing.Sample.TagsAggregate
open Tonyx.EventSourcing.Sample.Tags.TagsEvents
open Tonyx.EventSourcing.Sample.Tags.TagCommands
open Tonyx.EventSourcing.Sample.Tags.Models.TagsModel

open Tonyx.EventSourcing.Sample
open Tonyx.EventSourcing.Sample.Categories
open Tonyx.EventSourcing.Sample.CategoriesAggregate
open Tonyx.EventSourcing.Sample.Categories.CategoriesCommands
open Tonyx.EventSourcing.Sample.Categories.CategoriesEvents
open Tonyx.EventSourcing.Sample.Categories
open System
open FSharpPlus

module App =
    type App(storage: IStorage) =

        member this.getAllTodos() =
            ceResult {
                let! (_, state) = Repository'.getState<TodosAggregate, TodoEvent>(storage)
                let todos = state.GetTodos()
                return todos
            }
        member this.getAllTodos'() =
            ceResult {
                let! (_, state) = Repository'.getState<TodosAggregate',TodoEvents.TodoEvent'>(storage)
                let todos = state.GetTodos()
                return todos
            }

        member this.addTodo todo =
            let lockobj1 = Conf.syncobjects |> Map.tryFind (TodosAggregate.Version,TodosAggregate.StorageName)
            let lockobj2 = Conf.syncobjects |> Map.tryFind (TagsAggregate.Version,TagsAggregate.StorageName)

            match lockobj1, lockobj2 with
            | Some lock1, Some lock2 ->
                lock (lock1, lock2) <| fun () ->
                    ceResult {              
                        let! (_, tagState) = Repository'.getState<TagsAggregate, TagEvent>(storage)
                        let tagIds = tagState.GetTags() |>> (fun x -> x.Id)

                        let! tagIdIsValid =    
                            (todo.TagIds.IsEmpty ||
                            todo.TagIds |> List.forall (fun x -> (tagIds |> List.contains x)))
                            |> boolToResult "A tag reference contained is in the todo is related to a tag that does not exist"

                        let! _ =
                            todo
                            |> TodoCommand.AddTodo
                            |> (Repository'.runCommand<TodosAggregate, TodoEvent> storage)
                        let _ =  Repository'.mksnapshotIfInterval<TodosAggregate, TodoEvent> (storage)
                    return ()
                }
            | _ -> Error "No lock object found for TodosAggregate or TagsAggregate"

        member this.addTodo' todo =
            let lock1 = Conf.syncobjects |> Map.tryFind (TodosAggregate'.Version, TodosAggregate'.StorageName)
            let lock2 = Conf.syncobjects |> Map.tryFind (CategoriesAggregate.Version,CategoriesAggregate.StorageName)
            let lock3 = Conf.syncobjects |> Map.tryFind (TagsAggregate.Version,TagsAggregate.StorageName)

            match (lock1, lock2, lock3) with
                Some lock1Val, Some lock2Val , Some lock3Val->
                    lock (lock1Val, lock2Val, lock3Val) <| fun () ->
                        ceResult {              
                            let! (_, tagState) = Repository'.getState<TagsAggregate, TagEvent> storage
                            let tagIds = tagState.GetTags() |>> (fun x -> x.Id)

                            let! (_, categoriesState) = Repository'.getState<CategoriesAggregate.CategoriesAggregate, CategoriesEvents.CategoryEvent> storage
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
                                |> TodoCommand'.AddTodo
                                |> (Repository'.runCommand<TodosAggregate.TodosAggregate', TodoEvents.TodoEvent'> storage)

                            let _ =  Repository'.mksnapshotIfInterval<TodosAggregate.TodosAggregate', Todos.TodoEvents.TodoEvent'> storage
                        return ()
                    }
                | _ -> Error "No lock object found for TodosAggregate or CategoriesAggregate"

        member this.add2Todos (todo1, todo2) =
            let lockobj = Conf.syncobjects |> Map.tryFind (TodosAggregate.Version,TodosAggregate.StorageName)
            if lockobj.IsNone then
                Error (sprintf "No lock object found for %A %A" TodosAggregate.Version TodosAggregate.StorageName)
            else
                lock (lockobj.Value) <| fun () ->
                    ceResult {
                        let! (_, tagState) = Repository'.getState<TagsAggregate, TagEvent> storage
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
                            |> (Repository'.runCommand<TodosAggregate, TodoEvent> storage)
                        let _ =  Repository'.mksnapshotIfInterval<TodosAggregate, TodoEvent> storage
                        return ()
                    }
        member this.add2Todos' (todo1, todo2) =
            let lockobj = Conf.syncobjects |> Map.tryFind (TodosAggregate.TodosAggregate'.Version, TodosAggregate'.StorageName)
            if lockobj.IsNone then
                Error (sprintf "No lock object found for %A %A" TodosAggregate'.Version TodosAggregate'.StorageName)
            else
                lock (lockobj.Value) <| fun () ->
                    ceResult {
                        let! (_, tagState) = Repository'.getState<TagsAggregate, TagEvent> storage
                        let tagIds = tagState.GetTags() |>> (fun x -> x.Id)

                        let! (_, categoriesState) = Repository'.getState<CategoriesAggregate, CategoryEvent> storage
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
                            |> TodoCommand'.Add2Todos
                            |> (Repository'.runCommand<TodosAggregate.TodosAggregate', TodoEvents.TodoEvent'> storage)
                        let _ =  Repository'.mksnapshotIfInterval<TodosAggregate.TodosAggregate', TodoEvents.TodoEvent'> storage
                        return ()
                    }

        member this.removeTodo id =
            ceResult {
                let! _ =
                    id
                    |> TodoCommand.RemoveTodo
                    |> (Repository'.runCommand<TodosAggregate, TodoEvent> storage)
                let _ = Repository'.mksnapshotIfInterval<TodosAggregate, TodoEvent> storage
                return ()
            }
        member this.removeTodo' id =
            ceResult {
                let! _ =
                    id
                    |> TodoCommand'.RemoveTodo
                    |> (Repository'.runCommand<TodosAggregate.TodosAggregate', TodoEvents.TodoEvent'> storage)
                let _ = Repository'.mksnapshotIfInterval<TodosAggregate', TodoEvent'> storage
                return ()
            }

        member this.getAllCategories() =
            ceResult {
                let! (_, state) = Repository'.getState<TodosAggregate, TodoEvent> storage
                let categories = state.GetCategories()
                return categories
            }

        member this.getAllCategories'() =
            ceResult {
                let! (_, state) = Repository'.getState<CategoriesAggregate, CategoryEvent> storage
                let categories = state.GetCategories()
                return categories
            }

        member this.addCategory category =
            ceResult {
                let! _ =
                    category
                    |> TodoCommand.AddCategory
                    |> (Repository'.runCommand<TodosAggregate, TodoEvent> storage)
                let _ = Repository'.mksnapshotIfInterval<TodosAggregate, TodoEvent> storage
                return ()
            }

        member this.addCategory' category =
            ceResult {
                let! _ =
                    category
                    |> CategoriesCommands.CategoryCommand.AddCategory
                    |> (Repository'.runCommand<CategoriesAggregate.CategoriesAggregate, CategoriesEvents.CategoryEvent> storage)
                let _ = Repository'.mksnapshotIfInterval<CategoriesAggregate.CategoriesAggregate, CategoriesEvents.CategoryEvent> storage
                return ()
            }

        member this.removeCategory id = 
            ceResult {
                let! _ =
                    id
                    |> TodoCommand.RemoveCategory
                    |> (Repository'.runCommand<TodosAggregate, TodoEvent> storage)
                let _ = Repository'.mksnapshotIfInterval<TodosAggregate, TodoEvent> storage
                return ()
            }

        member this.removeCategory' id =
            ceResult {
                let removeCategory = CategoryCommand.RemoveCategory id
                let removeCategoryRef = TodoCommand'.RemoveCategoryRef id
                let! _ = 
                    Repository'.runTwoCommands<
                        CategoriesAggregate.CategoriesAggregate, 
                        TodosAggregate.TodosAggregate', 
                        CategoriesEvents.CategoryEvent, 
                        TodoEvents.TodoEvent'> 
                        storage removeCategory removeCategoryRef
                let _ = Repository'.mksnapshotIfInterval<CategoriesAggregate.CategoriesAggregate, CategoriesEvents.CategoryEvent>  storage
                let _ = Repository'.mksnapshotIfInterval<TodosAggregate.TodosAggregate', TodoEvents.TodoEvent'> storage
                return ()
            }

        member this.addTag tag =
            ceResult {
                let! _ =
                    tag
                    |> AddTag
                    |> (Repository'.runCommand<TagsAggregate, TagEvent> storage)
                let _ = (Repository'.mksnapshotIfInterval<TagsAggregate, TagEvent> storage)
                return ()
            }

        member this.removeTag id =
            ceResult {
                let removeTag = TagCommand.RemoveTag id
                let removeTagRef = TodoCommand.RemoveTagRef id
                let! _ = Repository'.runTwoCommands<TagsAggregate, TodosAggregate, TagEvent, TodoEvent> storage removeTag removeTagRef
                let _ = Repository'.mksnapshotIfInterval<TagsAggregate, TagEvent> storage
                let _ = Repository'.mksnapshotIfInterval<TodosAggregate, TodoEvent> storage
                return ()
            }
        member this.removeTag' id =
            ceResult {
                let removeTag = TagCommand.RemoveTag id
                let removeTagRef = TodoCommand'.RemoveTagRef id
                let! _ = Repository'.runTwoCommands<TagsAggregate, TodosAggregate', TagEvent, TodoEvent'> storage removeTag removeTagRef
                let _ = Repository'.mksnapshotIfInterval<TagsAggregate, TagEvent> storage
                let _ = Repository'.mksnapshotIfInterval<TodosAggregate', TodoEvent'> storage
                return ()
            }

        member this.getAllTags () =
            ceResult {
                let! (_, state) = Repository'.getState<TagsAggregate, TagEvent> storage
                let tags = state.GetTags()
                return tags
            }

        member this.migrate() =
            ceResult {
                let! categoriesFrom = this.getAllCategories()
                let! todosFrom = this.getAllTodos()
                let command = CategoryCommand.AddCategories categoriesFrom
                let command2 = TodoCommand'.AddTodos todosFrom
                let! _ = 
                    Repository'.runTwoCommands<
                        CategoriesAggregate.CategoriesAggregate, 
                        TodosAggregate.TodosAggregate', 
                        CategoriesEvents.CategoryEvent, 
                        TodoEvents.TodoEvent'> 
                            storage
                            command 
                            command2
                return () 
            }