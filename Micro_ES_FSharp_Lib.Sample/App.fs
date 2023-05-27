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
open FsToolkit.ErrorHandling
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Quotations.Patterns
open Microsoft.FSharp.Quotations.DerivedPatterns

module App =
    type App(storage: IStorage) =

        member this.getAllTodos() =
            ResultCE.result  {
                let! (_, state) = getState<TodosAggregate, TodoEvent>(storage)
                let todos = state.GetTodos()
                return todos
            }
        member this.getAllTodos'() =
            ResultCE.result {
                let! (_, state) = getState<TodosAggregate',TodoEvents.TodoEvent'>(storage)
                let todos = state.GetTodos()
                return todos
            }

        member this.experimentalAddTodo todo =
            lock (TodosAggregate.LockObj, TagsAggregate.LockObj) <| fun () ->
                ResultCE.result {
                    let! (_, tagState) = getState<TagsAggregate, TagEvent>(storage)
                    let tagIds = tagState.GetTags() |>> (fun x -> x.Id)

                    // todo refefences no tags or todo references only valid tags
                    let ``todo references no tags or todo refrences only valid tags`` = 
                            <@ 
                                todo.TagIds.IsEmpty || 
                                todo.TagIds 
                                |> List.forall (fun x -> (tagIds |> List.contains x))
                            @>

                    let! tagIdIsValid =    
                        (todo.TagIds.IsEmpty ||
                        todo.TagIds |> List.forall (fun x -> (tagIds |> List.contains x)))
                        |> boolToResult "A tag reference contained is in the todo is related to a tag that does not exist"

                    let! _ =
                        (``todo references no tags or todo refrences only valid tags``
                            , todo)
                        |> TodoCommand.ExperimentalAddTodo 
                        |> (runCommand<TodosAggregate, TodoEvent> storage)
                    // let _ =  mkSnapshotIfInterval<TodosAggregate, TodoEvent> (storage)
                return ()
            }


        member this.addTodo todo =
            lock (TodosAggregate.LockObj, TagsAggregate.LockObj) <| fun () ->

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


        member this.addTodo' todo =
            lock (TodosAggregate'.LockObj, CategoriesAggregate.LockObj, TagsAggregate.LockObj) <| fun () ->
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

        member this.add2Todos (todo1, todo2) =
            lock (TodosAggregate.LockObj, TagsAggregate.LockObj) <| fun () ->
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
        member this.add2Todos' (todo1, todo2) =
            lock (TodosAggregate'.LockObj, TagsAggregate.LockObj, CategoriesAggregate.LockObj) <| fun () ->
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

        member this.removeTodo id =
            ResultCE.result {
                let! _ =
                    id
                    |> TodoCommand.RemoveTodo
                    |> (runCommand<TodosAggregate, TodoEvent> storage)
                let _ = mkSnapshotIfInterval<TodosAggregate, TodoEvent> storage
                return ()
            }
        member this.removeTodo' id =
            ResultCE.result {
                let! _ =
                    id
                    |> TodoCommand'.RemoveTodo
                    |> (runCommand<TodosAggregate.TodosAggregate', TodoEvents.TodoEvent'> storage)
                let _ = mkSnapshotIfInterval<TodosAggregate', TodoEvent'> storage
                return ()
            }

        member this.getAllCategories() =
            ResultCE.result {
                let! (_, state) = getState<TodosAggregate, TodoEvent> storage
                let categories = state.GetCategories()
                return categories
            }

        member this.getAllCategories'() =
            ResultCE.result {
                let! (_, state) = getState<CategoriesAggregate, CategoryEvent> storage
                let categories = state.GetCategories()
                return categories
            }

        member this.addCategory category =
            ResultCE.result {
                let! _ =
                    category
                    |> TodoCommand.AddCategory
                    |> (runCommand<TodosAggregate, TodoEvent> storage)
                let _ = mkSnapshotIfInterval<TodosAggregate, TodoEvent> storage
                return ()
            }

        member this.addCategory' category =
            ResultCE.result {
                let! _ =
                    category
                    |> CategoriesCommands.CategoryCommand.AddCategory
                    |> (runCommand<CategoriesAggregate.CategoriesAggregate, CategoriesEvents.CategoryEvent> storage)
                let _ = mkSnapshotIfInterval<CategoriesAggregate.CategoriesAggregate, CategoriesEvents.CategoryEvent> storage
                return ()
            }

        member this.removeCategory id = 
            ResultCE.result {
                let! _ =
                    id
                    |> TodoCommand.RemoveCategory
                    |> (runCommand<TodosAggregate, TodoEvent> storage)
                let _ = mkSnapshotIfInterval<TodosAggregate, TodoEvent> storage
                return ()
            }

        member this.removeCategory' id =
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

        member this.addTag tag =
            ResultCE.result {
                let! _ =
                    tag
                    |> AddTag
                    |> (runCommand<TagsAggregate, TagEvent> storage)
                let _ = (mkSnapshotIfInterval<TagsAggregate, TagEvent> storage)
                return ()
            }

        member this.removeTag id =
            ResultCE.result {
                let removeTag = TagCommand.RemoveTag id
                let removeTagRef = TodoCommand.RemoveTagRef id
                let! _ = runTwoCommands<TagsAggregate, TodosAggregate, TagEvent, TodoEvent> storage removeTag removeTagRef
                let _ = mkSnapshotIfInterval<TagsAggregate, TagEvent> storage
                let _ = mkSnapshotIfInterval<TodosAggregate, TodoEvent> storage
                return ()
            }
        member this.removeTag' id =
            ResultCE.result {
                let removeTag = TagCommand.RemoveTag id
                let removeTagRef = TodoCommand'.RemoveTagRef id
                let! _ = runTwoCommands<TagsAggregate, TodosAggregate', TagEvent, TodoEvent'> storage removeTag removeTagRef
                let _ = mkSnapshotIfInterval<TagsAggregate, TagEvent> storage
                let _ = mkSnapshotIfInterval<TodosAggregate', TodoEvent'> storage
                return ()
            }

        member this.getAllTags () =
            ResultCE.result {
                let! (_, state) = getState<TagsAggregate, TagEvent> storage
                let tags = state.GetTags()
                return tags
            }

        member this.migrate() =
            ResultCE.result {
                let! categoriesFrom = this.getAllCategories()
                let! todosFrom = this.getAllTodos()
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