namespace Sharpino.Sample

open Sharpino
open Sharpino.Utils
open Sharpino.Repository

open Sharpino.Sample
open Sharpino.Sample.Todos
open Sharpino.Sample.TodosAggregate
open Sharpino.Sample.Todos.TodoEvents
open Sharpino.Sample.Todos.TodoCommands
open Sharpino.Sample.Models.TodosModel

open Sharpino.Sample.TagsAggregate
open Sharpino.Sample.Tags.TagsEvents
open Sharpino.Sample.Tags.TagCommands
open Sharpino.Sample.Models.TagsModel

open Sharpino.Sample.Categories
open Sharpino.Sample.CategoriesAggregate
open Sharpino.Sample.Categories.CategoriesCommands
open Sharpino.Sample.Categories.CategoriesEvents
open System
open FSharpPlus
open FsToolkit.ErrorHandling
module App =
    [<CurrentVersion>]
    type CurrentVersionApp(storage: IStorage) =
        member this.GetAllTodos() =
            async {
                return
                    ResultCE.result  {
                        let! (_, state) = getState<TodosAggregate, TodoEvent>(storage)
                        let todos = state.GetTodos()
                        return todos
                    }
            }
            |> Async.RunSynchronously
        member this.AddTodo todo =
            async {
                return
                    lock TagsAggregate.LockObj <| fun () ->
                        ResultCE.result {
                            let! (_, tagState) = getState<TagsAggregate, TagEvent>(storage)
                            let tagIds = tagState.GetTags() |>> (fun x -> x.Id)

                            let! tagIdIsValid =    
                                (todo.TagIds.IsEmpty ||
                                todo.TagIds |> List.forall (fun x -> (tagIds |> List.contains x)))
                                |> boolToResult "A tag reference contained is in the todo is related to a tag that does not exist"

                            let! _ =
                                todo
                                |> TodoCommand.AddTodo
                                |> (runCommand<TodosAggregate, TodoEvent> storage)
                            let _ =  mkSnapshotIfInterval<TodosAggregate, TodoEvent> (storage)
                        return ()
                    }
            }
            |> Async.RunSynchronously
        member this.Add2Todos (todo1, todo2) =
            async {
                return
                    lock TagsAggregate.LockObj <| fun () ->
                        ResultCE.result {
                            let! (_, tagState) = getState<TagsAggregate, TagEvent> storage
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
                                |> (runCommand<TodosAggregate, TodoEvent> storage)
                            let _ =  mkSnapshotIfInterval<TodosAggregate, TodoEvent> storage
                            return ()
                        }
            }
            |> Async.RunSynchronously

        member this.RemoveTodo id =
            async {
                return
                    ResultCE.result {
                        let! _ =
                            id
                            |> TodoCommand.RemoveTodo
                            |> (runCommand<TodosAggregate, TodoEvent> storage)
                        let _ = mkSnapshotIfInterval<TodosAggregate, TodoEvent> storage
                        return ()
                    }
            }
            |> Async.RunSynchronously

        member this.GetAllCategories() =
            async {
                return
                    ResultCE.result {
                        let! (_, state) = getState<TodosAggregate, TodoEvent> storage
                        let categories = state.GetCategories()
                        return categories
                    }
            }
            |> Async.RunSynchronously
        member this.AddCategory category =
            async {
                return
                    ResultCE.result {
                        let! _ =
                            category
                            |> TodoCommand.AddCategory
                            |> (runCommand<TodosAggregate, TodoEvent> storage)
                        let _ = mkSnapshotIfInterval<TodosAggregate, TodoEvent> storage
                        return ()
                    }
            }
            |> Async.RunSynchronously

        member this.RemoveCategory id = 
            async {
                return
                    ResultCE.result {
                        let! _ =
                            id
                            |> TodoCommand.RemoveCategory
                            |> (runCommand<TodosAggregate, TodoEvent> storage)
                        let _ = mkSnapshotIfInterval<TodosAggregate, TodoEvent> storage
                        return ()
                    }
            }
            |> Async.RunSynchronously
        member this.AddTag tag =
            async {
                return
                    ResultCE.result {
                        let! _ =
                            tag
                            |> AddTag
                            |> (runCommand<TagsAggregate, TagEvent> storage)
                        let _ = (mkSnapshotIfInterval<TagsAggregate, TagEvent> storage)
                        return ()
                    }
            }
            |> Async.RunSynchronously
        member this.RemoveTag id =
            async {
                return
                    ResultCE.result {
                        let removeTag = TagCommand.RemoveTag id
                        let removeTagRef = TodoCommand.RemoveTagRef id
                        let! _ = runTwoCommands<TagsAggregate, TodosAggregate, TagEvent, TodoEvent> storage removeTag removeTagRef
                        let _ = mkSnapshotIfInterval<TagsAggregate, TagEvent> storage
                        let _ = mkSnapshotIfInterval<TodosAggregate, TodoEvent> storage
                        return ()
                    }
            }
            |> Async.RunSynchronously
        member this.GetAllTags () =
            async {
                return
                    ResultCE.result {
                        let! (_, state) = getState<TagsAggregate, TagEvent> storage
                        let tags = state.GetTags()
                        return tags
                    }
            }
            |> Async.RunSynchronously

        member this.Migrate() =
            async {
                return
                    lock TodosAggregate.LockObj <| fun () ->
                        ResultCE.result {
                            let! categoriesFrom = this.GetAllCategories()
                            let! todosFrom = this.GetAllTodos()
                            let command = CategoryCommand.AddCategories categoriesFrom
                            let command2 = TodoCommand'.AddTodos todosFrom
                            let! _ = 
                                runTwoCommands<
                                    CategoriesAggregate.CategoriesAggregate, 
                                    TodosAggregate.TodosAggregate', 
                                    CategoriesEvents.CategoryEvent, 
                                    TodoEvents.TodoEvent'> 
                                        storage
                                        command 
                                        command2
                            return () 
                        }
            }
            |> Async.RunSynchronously
    [<UpgradedVersion>]
    type UpgradedApp(storage: IStorage) =
        member this.GetAllTodos() =
            async {
                return
                    ResultCE.result {
                        let! (_, state) = getState<TodosAggregate',TodoEvents.TodoEvent'>(storage)
                        let todos = state.GetTodos()
                        return todos
                    }
            }
            |> Async.RunSynchronously

        member this.AddTodo todo =
            async {
                return
                    lock (CategoriesAggregate.LockObj, TagsAggregate.LockObj) <| fun () ->
                            ResultCE.result {
                                let! (_, tagState) = getState<TagsAggregate, TagEvent> storage
                                let tagIds = tagState.GetTags() |>> (fun x -> x.Id)

                                let! (_, categoriesState) = getState<CategoriesAggregate.CategoriesAggregate, CategoriesEvents.CategoryEvent> storage
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
                                    |> (runCommand<TodosAggregate.TodosAggregate', TodoEvents.TodoEvent'> storage)

                                let _ =  mkSnapshotIfInterval<TodosAggregate.TodosAggregate', Todos.TodoEvents.TodoEvent'> storage
                            return ()
                        }
            }
            |> Async.RunSynchronously

        member this.Add2Todos (todo1, todo2) =
            async {
                return
                    lock (TagsAggregate.LockObj, CategoriesAggregate.LockObj) <| fun () ->
                        ResultCE.result {
                            let! (_, tagState) = getState<TagsAggregate, TagEvent> storage
                            let tagIds = tagState.GetTags() |>> (fun x -> x.Id)

                            let! (_, categoriesState) = getState<CategoriesAggregate, CategoryEvent> storage
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
                                |> (runCommand<TodosAggregate.TodosAggregate', TodoEvents.TodoEvent'> storage)
                            let _ = mkSnapshotIfInterval<TodosAggregate.TodosAggregate', TodoEvents.TodoEvent'> storage
                            return ()
                        }
            }
            |> Async.RunSynchronously

        member this.RemoveTodo id =
            async { 
                return
                    ResultCE.result {
                        let! _ =
                            id
                            |> TodoCommand'.RemoveTodo
                            |> (runCommand<TodosAggregate.TodosAggregate', TodoEvents.TodoEvent'> storage)
                        let _ = mkSnapshotIfInterval<TodosAggregate', TodoEvent'> storage
                        return ()
                    }
            }
            |> Async.RunSynchronously

        member this.GetAllCategories() =
            async { 
                return
                    ResultCE.result {
                        let! (_, state) = getState<CategoriesAggregate, CategoryEvent> storage
                        let categories = state.GetCategories()
                        return categories
                    }
            }
            |> Async.RunSynchronously

        member this.AddCategory category =
            async {
                return
                    ResultCE.result {
                        let! _ =
                            category
                            |> CategoriesCommands.CategoryCommand.AddCategory
                            |> (runCommand<CategoriesAggregate.CategoriesAggregate, CategoriesEvents.CategoryEvent> storage)
                        let _ = mkSnapshotIfInterval<CategoriesAggregate.CategoriesAggregate, CategoriesEvents.CategoryEvent> storage
                        return ()
                    }
            }
            |> Async.RunSynchronously

        member this.RemoveCategory id =
            async {
                return
                    ResultCE.result {
                        let removeCategory = CategoryCommand.RemoveCategory id
                        let removeCategoryRef = TodoCommand'.RemoveCategoryRef id
                        let! _ = 
                            runTwoCommands<
                                CategoriesAggregate.CategoriesAggregate, 
                                TodosAggregate.TodosAggregate', 
                                CategoriesEvents.CategoryEvent, 
                                TodoEvents.TodoEvent'> 
                                storage removeCategory removeCategoryRef
                        let _ = mkSnapshotIfInterval<CategoriesAggregate.CategoriesAggregate, CategoriesEvents.CategoryEvent>  storage
                        let _ = mkSnapshotIfInterval<TodosAggregate.TodosAggregate', TodoEvents.TodoEvent'> storage
                        return ()
                }
            }
            |> Async.RunSynchronously

        member this.AddTag tag =
            async {
                return
                    ResultCE.result {
                        let! _ =
                            tag
                            |> AddTag
                            |> (runCommand<TagsAggregate, TagEvent> storage)
                        let _ = (mkSnapshotIfInterval<TagsAggregate, TagEvent> storage)
                        return ()
                    }
            }
            |> Async.RunSynchronously

        member this.removeTag id =
            async {
                return 
                    ResultCE.result {
                        let removeTag = TagCommand.RemoveTag id
                        let removeTagRef = TodoCommand'.RemoveTagRef id
                        let! _ = runTwoCommands<TagsAggregate, TodosAggregate', TagEvent, TodoEvent'> storage removeTag removeTagRef
                        let _ = mkSnapshotIfInterval<TagsAggregate, TagEvent> storage
                        let _ = mkSnapshotIfInterval<TodosAggregate', TodoEvent'> storage
                        return ()
                    }
            }
            |> Async.RunSynchronously

        member this.GetAllTags () =
            async {
                return
                    ResultCE.result {
                        let! (_, state) = getState<TagsAggregate, TagEvent> storage
                        let tags = state.GetTags()
                        return tags
                    }
            }
            |> Async.RunSynchronously

