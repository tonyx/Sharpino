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
    type CurrentVersionApp(storage: IStorage) =
        member this.GetAllTodos() =
            result  {
                let! (_, state) = storage |> getState<TodosAggregate, TodoEvent>
                return state.GetTodos()
            }

        member this.AddTodo todo =
            lock (TodosAggregate.Lock, TagsAggregate.Lock) (fun () -> 
                result {
                    let! (_, tagState) = storage |> getState<TagsAggregate, TagEvent> 
                    let tagIds = tagState.GetTags() |>> (fun x -> x.Id)

                    let! tagIdIsValid =    
                        (todo.TagIds.IsEmpty ||
                        todo.TagIds |> List.forall (fun x -> (tagIds |> List.contains x)))
                        |> boolToResult "A tag reference contained in the todo is related to a tag that does not exist"

                    return! 
                        todo
                        |> TodoCommand.AddTodo
                        |> runCommand<TodosAggregate, TodoEvent> storage
                }
            )

        member this.Add2Todos (todo1, todo2) =
            lock (TodosAggregate.Lock) (fun () -> 
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

                    return! 
                        (todo1, todo2)
                        |> TodoCommand.Add2Todos
                        |> runCommand<TodosAggregate, TodoEvent> storage
                }
            )

        member this.RemoveTodo id =
            result {
                return!
                    id
                    |> TodoCommand.RemoveTodo
                    |> runCommand<TodosAggregate, TodoEvent> storage
            }

        member this.GetAllCategories() =
            result {
                let! (_, state) = storage |> getState<TodosAggregate, TodoEvent>
                return  state.GetCategories()
            }

        member this.AddCategory category =
            result {
                return!
                    category
                    |> TodoCommand.AddCategory
                    |> runCommand<TodosAggregate, TodoEvent> storage
            }

        member this.RemoveCategory id = 
            result {
                return!
                    id
                    |> TodoCommand.RemoveCategory
                    |> runCommand<TodosAggregate, TodoEvent> storage
            }

        member this.AddTag tag =
            result {
                return!
                    tag
                    |> AddTag
                    |> runCommand<TagsAggregate, TagEvent> storage
            }

        member this.RemoveTag id =
            result {
                let removeTag = TagCommand.RemoveTag id
                let removeTagRef = TodoCommand.RemoveTagRef id
                return!
                    runTwoCommands<TagsAggregate, TodosAggregate, TagEvent, TodoEvent> storage removeTag removeTagRef
            }

        member this.GetAllTags () =
            result {
                let! (_, state) = storage |> getState<TagsAggregate, TagEvent>
                return state.GetTags()
            }

        member this.Migrate() =
            result {
                let! categoriesFrom = this.GetAllCategories()
                let! todosFrom = this.GetAllTodos()
                let command = CategoryCommand.AddCategories categoriesFrom
                let command2 = TodoCommand'.AddTodos todosFrom
                return!  
                    runTwoCommands<
                        CategoriesAggregate, 
                        TodosAggregate', 
                        CategoryEvent, 
                        TodoEvent'> 
                            storage
                            command 
                            command2
            }
        member this.TodoReport (dateFrom: DateTime)  (dateTo: DateTime) =
            let events = storage.GetEventsInATimeInterval TodosAggregate.Version TodosAggregate.StorageName dateFrom dateTo |>> snd
            let deserEvents = events |>> (serializer.Deserialize >> Result.get)
            { InitTime = dateFrom; EndTime = dateTo; TodoEvents = deserEvents }

    [<UpgradedVersion>]
    type UpgradedApp(storage: IStorage) =
        member this.GetAllTodos() =
            result {
                let! (_, state) = storage |> getState<TodosAggregate', TodoEvent'>
                return state.GetTodos()
            }

        member this.AddTodo todo =
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

                return! 
                    todo
                    |> TodoCommand'.AddTodo
                    |> runCommand<TodosAggregate', TodoEvent'> storage
            }

        member this.Add2Todos (todo1, todo2) =
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
                    |> runCommand<TodosAggregate', TodoEvent'> storage
                return ()
            }

        member this.RemoveTodo id =
            result {
                return! 
                    id
                    |> TodoCommand'.RemoveTodo
                    |> runCommand<TodosAggregate', TodoEvent'> storage
            }

        member this.GetAllCategories() =
            result {
                let! (_, state) = storage |> getState<CategoriesAggregate, CategoryEvent>
                return state.GetCategories()
            }

        member this.AddCategory category =
            result {
                return!
                    category
                    |> CategoryCommand.AddCategory
                    |> runCommand<CategoriesAggregate, CategoryEvent> storage
            }

        member this.RemoveCategory id =
            result {
                let removeCategory = CategoryCommand.RemoveCategory id
                let removeCategoryRef = TodoCommand'.RemoveCategoryRef id
                return!  
                    runTwoCommands<
                        CategoriesAggregate, 
                        TodosAggregate', 
                        CategoryEvent, 
                        TodoEvent'> 
                        storage removeCategory removeCategoryRef
            }

        member this.AddTag tag =
            result {
                return! 
                    tag
                    |> AddTag
                    |> runCommand<TagsAggregate, TagEvent> storage
            }

        member this.removeTag id =
            result {
                let removeTag = TagCommand.RemoveTag id
                let removeTagRef = TodoCommand'.RemoveTagRef id
                return! runTwoCommands<TagsAggregate, TodosAggregate', TagEvent, TodoEvent'> storage removeTag removeTagRef
            }

        member this.GetAllTags () =
            result {
                let! (_, state) = 
                    storage |> getState<TagsAggregate, TagEvent>
                let tags = state.GetTags()
                return tags
            }

        member this.TodoReport (dateFrom: DateTime)  (dateTo: DateTime) =
            let events = storage.GetEventsInATimeInterval TodosAggregate'.Version TodosAggregate'.StorageName dateFrom dateTo |>> snd
            let deserEvents = events |>> (serializer.Deserialize >> Result.get)
            { InitTime = dateFrom; EndTime = dateTo; TodoEvents = deserEvents }


