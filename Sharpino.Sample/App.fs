namespace Sharpino.Sample

open Sharpino
open Sharpino.Utils
open Sharpino.CommandHandler

open Sharpino.Sample
open Sharpino.Storage
open Sharpino.Sample.Todos
open Sharpino.Sample.TodosAggregate
open Sharpino.Sample.Todos.TodoEvents
open Sharpino.Sample.Todos.TodoCommands
open Sharpino.Sample.Entities.Todos

open Sharpino.Sample.TagsAggregate
open Sharpino.Sample.Tags.TagsEvents
open Sharpino.Sample.Tags.TagCommands
open Sharpino.Sample.Entities.Tags

open Sharpino.Sample.Categories
open Sharpino.Sample.CategoriesAggregate
open Sharpino.Sample.Categories.CategoriesCommands
open Sharpino.Sample.Categories.CategoriesEvents
open Sharpino.Sample.Entities.TodosReport
open Sharpino.Sample.Converters
open System
open FSharpPlus
open FsToolkit.ErrorHandling
module App =

    [<CurrentVersion>]
    type CurrentVersionApp(storage: IStorage, eventBroker: IEventBroker) =
        member this.GetAllTodos() =
            async {
                return
                    ResultCE.result  {
                        let! (_, state) = storage |> getState<TodosAggregate, TodoEvent>
                        let todos = state.GetTodos()
                        return todos
                    }
            }
            |> Async.RunSynchronously

        member this.AddTodo todo =
            result {
                let! (_, tagState) = storage |> getState<TagsAggregate, TagEvent> 
                let tagIds = tagState.GetTags() |>> (fun x -> x.Id)

                let! tagIdIsValid =    
                    (todo.TagIds.IsEmpty ||
                    todo.TagIds |> List.forall (fun x -> (tagIds |> List.contains x)))
                    |> boolToResult "A tag reference contained in the todo is related to a tag that does not exist"

                let! _ =
                    todo
                    |> TodoCommand.AddTodo
                    |> runCommand<TodosAggregate, TodoEvent> storage eventBroker

                let _ =  
                    storage
                    |> mkSnapshotIfIntervalPassed<TodosAggregate, TodoEvent>
                return ()
        }

        // here I am using two lock object to synchronize the access to the storage in
        // dealing with two aggregates
        member this.Add2Todos (todo1, todo2) =
            lock (TodosAggregate.Lock, TagsAggregate.Lock) (fun () -> 
                result {
                    let! (_, tagState) = storage |> getState<TagsAggregate, TagEvent> 
                    let tagIds = tagState.GetTags() |>> (fun x -> x.Id)

                    let! tagId1IsValid =  
                        (todo1.TagIds.IsEmpty ||
                        todo1.TagIds |> List.forall (fun x -> (tagIds |> List.contains x)))
                        |> boolToResult "A tag reference contained in the todo is related to a tag that does not exist"

                    let! tagId2IsValid =    
                        (todo2.TagIds.IsEmpty ||
                        todo2.TagIds |> List.forall (fun x -> (tagIds |> List.contains x)))
                        |> boolToResult "A tag reference contained in the todo is related to a tag that does not exist"

                    let! _ =
                        (todo1, todo2)
                        |> TodoCommand.Add2Todos
                        |> runCommand<TodosAggregate, TodoEvent> storage eventBroker
                    let _ =  
                        storage
                        |> mkSnapshotIfIntervalPassed<TodosAggregate, TodoEvent>
                    return ()
                }
            )

        // here I am using no sync strategy at all because the worst thing that can happen is that
        // the TodoRemoved events is generated twice, so the second one will just be ignored by the evolve (Core.fs)
        // 
        member this.RemoveTodo id =
            result {
                let! _ =
                    id
                    |> TodoCommand.RemoveTodo
                    |> runCommand<TodosAggregate, TodoEvent> storage eventBroker
                let _ = 
                    storage
                    |> mkSnapshotIfIntervalPassed<TodosAggregate, TodoEvent>
                return ()
            }

        member this.GetAllCategories() =
            async {
                return
                    ResultCE.result {
                        let! (_, state) = storage |> getState<TodosAggregate, TodoEvent>
                        let categories = state.GetCategories()
                        return categories
                    }
            }
            |> Async.RunSynchronously

        // I will use mailboxprocessor even if I don't need any sync strategy
        member this.AddCategory category =
            result {
                let! _ =
                    category
                    |> TodoCommand.AddCategory
                    |> runCommand<TodosAggregate, TodoEvent> storage eventBroker
                return ()
            }

        member this.RemoveCategory id = 
            let f = fun () ->
                result {
                    let! _ =
                        id
                        |> TodoCommand.RemoveCategory
                        |> runCommand<TodosAggregate, TodoEvent> storage eventBroker
                    let _ = 
                        storage 
                        |> mkSnapshotIfIntervalPassed<TodosAggregate, TodoEvent> 
                    return ()
                }
            async {
                return processor.PostAndReply (fun rc -> f, rc)
            } 
            |> Async.RunSynchronously

        member this.AddTag tag =
            let f = fun() ->
                result {
                    let! _ =
                        tag
                        |> AddTag
                        |> runCommand<TagsAggregate, TagEvent> storage eventBroker
                    let _ =  
                        storage 
                        |> mkSnapshotIfIntervalPassed<TagsAggregate, TagEvent> 
                    return ()
                }
            async {
                return processor.PostAndReply (fun rc -> f, rc)
            } 
            |> Async.RunSynchronously

        member this.RemoveTag id =
            let f = fun () ->
                result {
                    let removeTag = TagCommand.RemoveTag id
                    let removeTagRef = TodoCommand.RemoveTagRef id
                    let! _ = runTwoCommands<TagsAggregate, TodosAggregate, TagEvent, TodoEvent> storage eventBroker removeTag removeTagRef
                    let _ = 
                        storage
                        |> mkSnapshotIfIntervalPassed<TagsAggregate, TagEvent>
                    let _ = 
                        storage
                        |> mkSnapshotIfIntervalPassed<TodosAggregate, TodoEvent>
                    return ()
                }
            async {
                return processor.PostAndReply (fun rc -> f, rc)
            }
            |> Async.RunSynchronously

        member this.GetAllTags () =
            async {
                return
                    result {
                        let! (_, state) = storage |> getState<TagsAggregate, TagEvent>
                        let tags = state.GetTags()
                        return tags
                    }
            }
            |> Async.RunSynchronously

        member this.Migrate() =
            let f = fun () ->
                result {
                    let! categoriesFrom = this.GetAllCategories()
                    let! todosFrom = this.GetAllTodos()
                    let command = CategoryCommand.AddCategories categoriesFrom
                    let command2 = TodoCommand'.AddTodos todosFrom
                    let! _ = 
                        runTwoCommands<
                            CategoriesAggregate, 
                            TodosAggregate', 
                            CategoryEvent, 
                            TodoEvent'> 
                                storage
                                eventBroker
                                command 
                                command2
                    return () 
                }
            async {
                return processor.PostAndReply (fun rc -> f, rc)
            }
            |> Async.RunSynchronously
        member this.TodoReport (dateFrom: DateTime)  (dateTo: DateTime) =
            let events = storage.GetEventsInATimeInterval TodosAggregate.Version TodosAggregate.StorageName dateFrom dateTo |>> snd
            let deserEvents = events |>> (serializer.Deserialize >> Result.get)
            let result = { InitTime = dateFrom; EndTime = dateTo; TodoEvents = deserEvents }
            result

    [<UpgradedVersion>]
    type UpgradedApp(storage: IStorage, eventBroker: IEventBroker) =
        member this.GetAllTodos() =
            async {
                return
                    result {
                        let! (_, state) = storage |> getState<TodosAggregate', TodoEvent'>
                        let todos = state.GetTodos()
                        return todos
                    }
            }
            |> Async.RunSynchronously

        member this.AddTodo todo =
            let f = fun () ->
                result {
                    let! (_, tagState) = storage |> getState<TagsAggregate, TagEvent> 
                    let tagIds = tagState.GetTags() |>> (fun x -> x.Id)

                    let! (_, categoriesState) = storage |>  getState<CategoriesAggregate, CategoryEvent>
                    let categoryIds = categoriesState.GetCategories() |>> (fun x -> x.Id)

                    let! tagIdIsValid =    
                        (todo.TagIds.IsEmpty ||
                        todo.TagIds |> List.forall (fun x -> (tagIds |> List.contains x)))
                        |> boolToResult "A tag reference contained in the todo is related to a tag that does not exist"

                    let! categoryIdIsValid =    
                        (todo.CategoryIds.IsEmpty ||
                        todo.CategoryIds |> List.forall (fun x -> (categoryIds |> List.contains x)))
                        |> boolToResult "A category reference contained in the todo is related to a category that does not exist"

                    let! _ =
                        todo
                        |> TodoCommand'.AddTodo
                        |> runCommand<TodosAggregate', TodoEvent'> storage eventBroker

                    let _ =  
                        storage 
                        |> mkSnapshotIfIntervalPassed<TodosAggregate', TodoEvent'>
                return ()
            }
            async {
                return processor.PostAndReply (fun rc -> f, rc)
            }
            |> Async.RunSynchronously

        member this.Add2Todos (todo1, todo2) =
            let f = fun () ->
                result {
                    let! (_, tagState) = storage |> getState<TagsAggregate, TagEvent>
                    let tagIds = tagState.GetTags() |>> (fun x -> x.Id)

                    let! (_, categoriesState) = storage |> getState<CategoriesAggregate, CategoryEvent>
                    let categoryIds = categoriesState.GetCategories() |>> (fun x -> x.Id)

                    let! categoryId1IsValid =    
                        (todo1.CategoryIds.IsEmpty ||
                        todo1.CategoryIds |> List.forall (fun x -> (categoryIds |> List.contains x)))
                        |> boolToResult "A category reference contained in the todo is related to a category that does not exist"

                    let! categoryId2IsValid =
                        (todo2.CategoryIds.IsEmpty ||
                        todo2.CategoryIds |> List.forall (fun x -> (categoryIds |> List.contains x)))
                        |> boolToResult "A category reference contained in the todo is related to a category that does not exist" 

                    let! tagId1IsValid =    
                        (todo1.TagIds.IsEmpty ||
                        todo1.TagIds |> List.forall (fun x -> (tagIds |> List.contains x)))
                        |> boolToResult "A tag reference contained in the todo is related to a tag that does not exist"

                    let! tagId2IsValid =    
                        (todo2.TagIds.IsEmpty ||
                        todo2.TagIds |> List.forall (fun x -> (tagIds |> List.contains x)))
                        |> boolToResult "A tag reference contained in the todo is related to a tag that does not exist"

                    let! _ =
                        (todo1, todo2)
                        |> TodoCommand'.Add2Todos
                        |> runCommand<TodosAggregate', TodoEvent'> storage eventBroker
                    let _ = 
                        storage
                        |> mkSnapshotIfIntervalPassed<TodosAggregate', TodoEvent'>
                    return ()
                }
            async {
                return processor.PostAndReply (fun rc -> f, rc)
            }
            |> Async.RunSynchronously

        member this.RemoveTodo id =
            let f = fun () ->
                result {
                    let! _ =
                        id
                        |> TodoCommand'.RemoveTodo
                        |> runCommand<TodosAggregate', TodoEvent'> storage eventBroker
                    let _ = 
                        storage
                        |> mkSnapshotIfIntervalPassed<TodosAggregate', TodoEvent'>
                    return ()
                }
            async {
                return processor.PostAndReply (fun rc -> f, rc)
            }
            |> Async.RunSynchronously

        member this.GetAllCategories() =
            async { 
                return
                    result {
                        let! (_, state) = storage |> getState<CategoriesAggregate, CategoryEvent>
                        let categories = state.GetCategories()
                        return categories
                    }
            }
            |> Async.RunSynchronously

        member this.AddCategory category =
            let f = fun () ->
                result {
                    let! _ =
                        category
                        |> CategoryCommand.AddCategory
                        |> runCommand<CategoriesAggregate, CategoryEvent> storage eventBroker
                    let _ = 
                        storage
                        |> mkSnapshotIfIntervalPassed<CategoriesAggregate, CategoryEvent>
                    return ()
                }
            async {
                return processor.PostAndReply (fun rc -> f, rc)
            }
            |> Async.RunSynchronously

        member this.RemoveCategory id =
            let f = fun () ->
                result {
                    let removeCategory = CategoryCommand.RemoveCategory id
                    let removeCategoryRef = TodoCommand'.RemoveCategoryRef id
                    let! _ = 
                        runTwoCommands<
                            CategoriesAggregate, 
                            TodosAggregate', 
                            CategoryEvent, 
                            TodoEvent'> 
                            storage eventBroker removeCategory removeCategoryRef
                    let _ = 
                        storage
                        |> mkSnapshotIfIntervalPassed<CategoriesAggregate, CategoryEvent>
                    let _ = 
                        storage
                        |> mkSnapshotIfIntervalPassed<TodosAggregate', TodoEvent'>
                    return ()
                }
            async {
                return processor.PostAndReply (fun rc -> f, rc)
            }
            |> Async.RunSynchronously

        member this.AddTag tag =
            let f = fun () ->
                result {
                    let! _ =
                        tag
                        |> AddTag
                        |> runCommand<TagsAggregate, TagEvent> storage eventBroker
                    let _ = 
                        storage
                        |> mkSnapshotIfIntervalPassed<TagsAggregate, TagEvent>
                    return ()
                }
            async {
                return processor.PostAndReply (fun rc -> f, rc)
            }
            |> Async.RunSynchronously

        member this.removeTag id =
            let f = fun() ->
                result {
                    let removeTag = TagCommand.RemoveTag id
                    let removeTagRef = TodoCommand'.RemoveTagRef id
                    let! _ = runTwoCommands<TagsAggregate, TodosAggregate', TagEvent, TodoEvent'> storage eventBroker removeTag removeTagRef
                    let _ = 
                        storage
                        |> mkSnapshotIfIntervalPassed<TagsAggregate, TagEvent>
                    let _ = 
                        storage
                        |> mkSnapshotIfIntervalPassed<TodosAggregate', TodoEvent'>
                    return ()
                }
            async {
                return processor.PostAndReply (fun rc -> f, rc)
            }
            |> Async.RunSynchronously

        member this.GetAllTags () =
            async {
                return
                    result {
                        let! (_, state) = 
                            storage |> getState<TagsAggregate, TagEvent>
                        let tags = state.GetTags()
                        return tags
                    }
            }
            |> Async.RunSynchronously

        member this.TodoReport (dateFrom: DateTime)  (dateTo: DateTime) =
            let events = storage.GetEventsInATimeInterval TodosAggregate'.Version TodosAggregate'.StorageName dateFrom dateTo |>> snd
            let deserEvents = events |>> (serializer.Deserialize >> Result.get)
            let result = { InitTime = dateFrom; EndTime = dateTo; TodoEvents = deserEvents }
            result


