namespace Sharpino.Sample

open Sharpino
open Sharpino.Utils
open Sharpino.CommandHandler
open Sharpino.StateView

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

open Sharpino.Sample.Categories
open Sharpino.Sample.CategoriesCluster
open Sharpino.Sample.Categories.CategoriesCommands
open Sharpino.Sample.Categories.CategoriesEvents
open Sharpino.Sample.Entities.TodosReport
open Sharpino.Sample.Shared.Entities
open Sharpino.Sample.Converters
open System
open FSharpPlus
open FsToolkit.ErrorHandling
open log4net
module App =
    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    // log4net.Config.BasicConfigurator.Configure() |> ignore
    let doNothingBroker = 
        {
            notify = None
        }
    [<CurrentVersion>]
    type CurrentVersionApp(storage: IStorage, eventBroker: IEventBroker) =
        new(storage: IStorage) = CurrentVersionApp(storage, doNothingBroker)
        member this._eventBroker = eventBroker
        member this.GetAllTodos() =
            result  {
                let! (_, state) = storage |> getState<TodosCluster, TodoEvent>
                return state.GetTodos()
            }

        member this.AddTodo todo =
            lock (TodosCluster.Lock, TagsCluster.Lock) (fun () -> 
                result {
                    let! (_, tagState) = storage |> getState<TagsCluster, TagEvent> 
                    let tagIds = tagState.GetTags() |>> (fun x -> x.Id)

                    let! tagIdIsValid =    
                        (todo.TagIds.IsEmpty ||
                        todo.TagIds |> List.forall (fun x -> (tagIds |> List.contains x)))
                        |> boolToResult "A tag reference contained in the todo is related to a tag that does not exist"

                    let! _ =
                        todo
                        |> TodoCommand.AddTodo
                        |> runCommand<TodosCluster, TodoEvent> storage eventBroker
                    return ()
                }
            )

        member this.Add2Todos (todo1, todo2) =
            lock (TodosCluster.Lock, TagsCluster.Lock) (fun () -> 
                result {
                    let! (_, tagState) = storage |> getState<TagsCluster, TagEvent> 
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
                        |> runCommand<TodosCluster, TodoEvent> storage eventBroker
                    return ()
                }
            )

        member this.RemoveTodo id =
            result {
                let! _ =
                    id
                    |> TodoCommand.RemoveTodo
                    |> runCommand<TodosCluster, TodoEvent> storage eventBroker
                return ()
            }

        member this.GetAllCategories() =
            result {
                let! (_, state) = storage |> getState<TodosCluster, TodoEvent>
                return  state.GetCategories()
            }

        member this.AddCategory category =
            result {
                let! _ =
                    category
                    |> TodoCommand.AddCategory
                    |> runCommand<TodosCluster, TodoEvent> storage eventBroker
                return ()
            }

        member this.RemoveCategory id = 
            result {
                let! _ =
                    id
                    |> TodoCommand.RemoveCategory
                    |> runCommand<TodosCluster, TodoEvent> storage eventBroker
                return ()
            }

        member this.AddTag tag =
            result {
                let! _ =
                    tag
                    |> AddTag
                    |> runCommand<TagsCluster, TagEvent> storage eventBroker
                return ()
            }

        member this.RemoveTag id =
            result {
                let removeTag = TagCommand.RemoveTag id
                let removeTagRef = TodoCommand.RemoveTagRef id
                let! _ = runTwoCommands<TagsCluster, TodosCluster, TagEvent, TodoEvent> storage eventBroker removeTag removeTagRef
                return ()
            }

        member this.GetAllTags () =
            result {
                let! (_, state) = storage |> getState<TagsCluster, TagEvent>
                return state.GetTags()
            }

        member this.Migrate() =
            result {
                let! categoriesFrom = this.GetAllCategories()
                let! todosFrom = this.GetAllTodos()
                let command = CategoryCommand.AddCategories categoriesFrom
                let command2 = TodoCommand'.AddTodos todosFrom
                let! _ =
                    runTwoCommands<
                        CategoriesCluster, 
                        TodosAggregate', 
                        CategoryEvent, 
                        TodoEvent'> 
                            storage
                            eventBroker
                            command 
                            command2
                return ()
            }
        member this.TodoReport (dateFrom: DateTime)  (dateTo: DateTime) =
            let events = storage.GetEventsInATimeInterval TodosCluster.Version TodosCluster.StorageName dateFrom dateTo |>> snd
            let deserEvents = events |>> (serializer.Deserialize >> Result.get)
            { InitTime = dateFrom; EndTime = dateTo; TodoEvents = deserEvents }

    [<UpgradedVersion>]
    type UpgradedApp(storage: IStorage, eventBroker: IEventBroker) =
        new(storage: IStorage) = UpgradedApp(storage, doNothingBroker)
        member this._eventBroker = eventBroker
        member this.GetAllTodos() =
            result {
                let! (_, state) = storage |> getState<TodosAggregate', TodoEvent'>
                return state.GetTodos()
            }

        member this.AddTodo todo =
            result {
                let! (_, tagState) = storage |> getState<TagsCluster, TagEvent> 
                let tagIds = tagState.GetTags() |>> (fun x -> x.Id)

                let! (_, categoriesState) = storage |>  getState<CategoriesCluster, CategoryEvent>
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
                return ()
            }

        member this.Add2Todos (todo1, todo2) =
            result {
                let! (_, tagState) = storage |> getState<TagsCluster, TagEvent>
                let tagIds = tagState.GetTags() |>> (fun x -> x.Id)

                let! (_, categoriesState) = storage |> getState<CategoriesCluster, CategoryEvent>
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
                return () 
            }

        member this.RemoveTodo id =
            result {
                let! _ =
                    id
                    |> TodoCommand'.RemoveTodo
                    |> runCommand<TodosAggregate', TodoEvent'> storage eventBroker
                return ()
            }

        member this.GetAllCategories() =
            result {
                let! (_, state) = storage |> getState<CategoriesCluster, CategoryEvent>
                return state.GetCategories()
            }

        member this.AddCategory category =
            result {
                let! _ =
                    category
                    |> CategoryCommand.AddCategory
                    |> runCommand<CategoriesCluster, CategoryEvent> storage eventBroker
                return ()
            }

        member this.RemoveCategory id =
            result {
                let removeCategory = CategoryCommand.RemoveCategory id
                let removeCategoryRef = TodoCommand'.RemoveCategoryRef id
                let! _ =
                    runTwoCommands<
                        CategoriesCluster, 
                        TodosAggregate', 
                        CategoryEvent, 
                        TodoEvent'> 
                        storage eventBroker removeCategory removeCategoryRef
                return ()
            }

        member this.AddTag tag =
            result {
                let! _ =
                    tag
                    |> AddTag
                    |> runCommand<TagsCluster, TagEvent> storage eventBroker
                return ()
            }

        member this.removeTag id =
            result {
                let removeTag = TagCommand.RemoveTag id
                let removeTagRef = TodoCommand'.RemoveTagRef id
                let! _ = runTwoCommands<TagsCluster, TodosAggregate', TagEvent, TodoEvent'> storage eventBroker removeTag removeTagRef
                return ()
            }

        member this.GetAllTags () =
            result {
                let! (_, state) = 
                    storage |> getState<TagsCluster, TagEvent>
                let tags = state.GetTags()
                return tags
            }

        member this.TodoReport (dateFrom: DateTime)  (dateTo: DateTime) =
            let events = storage.GetEventsInATimeInterval TodosAggregate'.Version TodosAggregate'.StorageName dateFrom dateTo |>> snd
            let deserEvents = events |>> (serializer.Deserialize >> Result.get)
            { InitTime = dateFrom; EndTime = dateTo; TodoEvents = deserEvents }


