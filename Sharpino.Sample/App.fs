namespace Sharpino.Sample
open Sharpino
open Sharpino.Core
open Sharpino.Utils
open Sharpino.CommandHandler
open Sharpino.StateView
open Sharpino.Definitions

open Sharpino.Sample
open Sharpino.Storage
open Sharpino.Sample.Todos
open Sharpino.Sample.TodosContext
open Sharpino.Sample.Todos.TodoEvents
open Sharpino.Sample.Todos.TodoCommands
open Sharpino.Sample.Entities.Todos

open Sharpino.Sample.TagsContext
open Sharpino.Sample.Tags.TagsEvents
open Sharpino.Sample.Tags.TagCommands
open Sharpino.Sample.Entities.Tags

open Sharpino.Sample.Categories
open Sharpino.Sample.CategoriesContext
open Sharpino.Sample.Categories.CategoriesCommands
open Sharpino.Sample.Categories.CategoriesEvents
open Sharpino.Sample.Entities.TodosReport
open Sharpino.Sample.Shared.Entities
open Sharpino.Sample.Converters
open Sharpino.StateView
open System
open FSharpPlus
open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open log4net
module App =

    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    // log4net.Config.BasicConfigurator.Configure() |> ignore
    let doNothingBroker = 
        {
            notify = None
            notifyAggregate =  None
        }
    [<CurrentVersion>]
    type CurrentVersionApp
        (storage: IEventStore<string>, eventBroker: IEventBroker<string>) =
        let todosStateViewer =
            getStorageFreshStateViewer<TodosContext, TodoEvent, string> storage
            
        let tagsStateViewer =
            getStorageFreshStateViewer<TagsContext, TagEvent, string> storage

        let categoryStateViewer =
            getStorageFreshStateViewer<CategoriesContext, CategoryEvent, string> storage

        let upgradeTodoStateViewer =
            getStorageFreshStateViewer<TodosContextUpgraded, TodoEvent', string> storage

        new(storage: IEventStore<string>) = CurrentVersionApp(storage, doNothingBroker)
        member this._eventBroker = eventBroker

        // must be storage based
        member this.PingTodo() =
            result {
                let! result =
                    TodoCommand.Ping ()
                    |> runCommand<TodosContext, TodoEvent, string> storage eventBroker 
                return result
            }
        member this.PingTag() =
            result {
                let! result =
                    TagCommand.Ping()
                    |> runCommand<TagsContext, TagEvent, string> storage eventBroker
                return result
            }
        member this.PingCategory() =
            // same as todo ping
            result {
                let! result =
                    TodoCommand.Ping()
                    |> runCommand<TodosContext, TodoEvent, string> storage eventBroker
                return result
            }

        member this.GetAllTodos() =
            result  {
                let! (_, state) = todosStateViewer ()
                return state.GetTodos()
            }

        member this.AddTodo todo =
            // lock (TodosContext.Lock, TagsContext.Lock) (fun () -> 
                result {
                    let! (_, tagState) = tagsStateViewer ()
                    let tagIds = tagState.GetTags() |>> (fun x -> x.Id)

                    let! tagIdIsValid =    
                        (todo.TagIds.IsEmpty ||
                        todo.TagIds |> List.forall (fun x -> (tagIds |> List.contains x)))
                        |> Result.ofBool "A tag reference contained in the todo is related to a tag that does not exist"

                    let! result =
                        todo
                        |> TodoCommand.AddTodo
                        |> runCommand<TodosContext, TodoEvent, string> storage eventBroker
                    return result
                }
            // )

        member this.Add2Todos (todo1, todo2) =
            lock (TodosContext.Lock, TagsContext.Lock) (fun () -> 
                result {
                    let! (_, tagState) = tagsStateViewer ()
                    let tagIds = tagState.GetTags() |>> (fun x -> x.Id)

                    let! tagId1IsValid =  
                        (todo1.TagIds.IsEmpty ||
                        todo1.TagIds |> List.forall (fun x -> (tagIds |> List.contains x)))
                        |> Result.ofBool "A tag reference contained in the todo is related to a tag that does not exist"

                    let! tagId2IsValid =    
                        (todo2.TagIds.IsEmpty ||
                        todo2.TagIds |> List.forall (fun x -> (tagIds |> List.contains x)))
                        |> Result.ofBool "A tag reference contained in the todo is related to a tag that does not exist"

                    let! result =
                        (todo1, todo2)
                        |> TodoCommand.Add2Todos
                        |> runCommand<TodosContext, TodoEvent, string> storage eventBroker
                    return result
                }
            )

        member this.RemoveTodo id =
            result {
                let! result =
                    id
                    |> TodoCommand.RemoveTodo
                    |> runCommand<TodosContext, TodoEvent, string> storage eventBroker
                return result 
            }
        member this.GetAllCategories() =
            result {
                let! (_, state) = todosStateViewer ()
                return  state.GetCategories()
            }

        member this.AddCategory category =
            result {
                let! result =
                    category
                    |> TodoCommand.AddCategory
                    |> runCommand<TodosContext, TodoEvent, string> storage eventBroker
                return result
            }

        member this.RemoveCategory id = 
            result {
                let! result =
                    id
                    |> TodoCommand.RemoveCategory
                    |> runCommand<TodosContext, TodoEvent, string> storage eventBroker
                return result 
            }

        member this.AddTag tag =
            result {
                let! result =
                    tag
                    |> AddTag
                    |> runCommand<TagsContext, TagEvent, string> storage eventBroker
                return result 
            }

        member this.RemoveTag id =
            result {
                let removeTag = TagCommand.RemoveTag id
                let removeTagRef = TodoCommand.RemoveTagRef id
                let! result = runTwoCommands<TagsContext, TodosContext, TagEvent, TodoEvent, string> storage eventBroker removeTag removeTagRef
                return result
            }

        member this.GetAllTags () =
            result {
                let! (_, state) = tagsStateViewer () 
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
                        CategoriesContext, 
                        TodosContextUpgraded, 
                        CategoryEvent, 
                        TodoEvent',
                        string> 
                            storage
                            eventBroker
                            command 
                            command2
                return ()
            }

        member this.TodoReport (dateFrom: DateTime)  (dateTo: DateTime) =
            // let events = storage.GetEventsInATimeInterval TodosContext.Version TodosContext.StorageName dateFrom dateTo |>> snd
            result {
                let! events = storage.GetEventsInATimeInterval TodosContext.Version TodosContext.StorageName dateFrom dateTo
                let events = events |>> snd
                // let! events = storage.GetEventsInATimeInterval TodosContext.Version TodosContext.StorageName dateFrom dateTo >>= snd
                let! events'' = 
                    events |> List.traverseResultM (fun x -> TodoEvents.TodoEvent.Deserialize x)
                return 
                    { TodosEvents.InitTime = dateFrom; EndTime = dateTo; TodoEvents = events'' }
            }

    [<UpgradedVersion>]
    type UpgradedApp(storage: IEventStore<string>, eventBroker: IEventBroker<string>) =
        let todosStateViewer =
            getStorageFreshStateViewer<TodosContextUpgraded, TodoEvent', string> storage
        let tagsStateViewer =
            getStorageFreshStateViewer<TagsContext, TagEvent, string> storage

        let categoryStateViewer =
            getStorageFreshStateViewer<CategoriesContext, CategoryEvent, string> storage

        new(storage: IEventStore<string>) = UpgradedApp(storage, doNothingBroker)

        member this._eventBroker = eventBroker

        member this.GetAllTodos() =
            result {
                let! (_, state) = todosStateViewer ()
                return state.GetTodos()
            }

        member this.PingTodo() =
            let storageBasedTodoViewer = getStorageFreshStateViewer<TodosContextUpgraded, TodoEvent', string> storage
            result {
                let! result =
                    TodoCommand'.Ping()
                    |> runCommand<TodosContextUpgraded, TodoEvent', string> storage eventBroker
                return result
            }

        member this.PingTag() =
            result {
                let! result =
                    TagCommand.Ping()
                    |> runCommand<TagsContext, TagEvent, string> storage eventBroker
                return result
            }
        member this.PingCategory() =
            // same as todo ping
            result {
                let! result =
                    CategoryCommand.Ping()
                    |> runCommand<CategoriesContext, CategoryEvent, string> storage eventBroker
                return result
            }

        member this.AddTodo todo =
            result {
                let! (_, tagState) = tagsStateViewer ()
                let tagIds = tagState.GetTags() |>> (fun x -> x.Id)

                let! (_, categoriesState) =  categoryStateViewer () // getState<CategoriesCluster, CategoryEvent>
                let categoryIds = categoriesState.GetCategories() |>> (fun x -> x.Id)

                let! tagIdIsValid =    
                    (todo.TagIds.IsEmpty ||
                    todo.TagIds |> List.forall (fun x -> (tagIds |> List.contains x)))
                    |> Result.ofBool "A tag reference contained in the todo is related to a tag that does not exist"

                let! categoryIdIsValid =    
                    (todo.CategoryIds.IsEmpty ||
                    todo.CategoryIds |> List.forall (fun x -> (categoryIds |> List.contains x)))
                    |> Result.ofBool "A category reference contained in the todo is related to a category that does not exist"

                let! result =
                    todo
                    |> TodoCommand'.AddTodo
                    |> runCommand<TodosContextUpgraded, TodoEvent', string> storage eventBroker
                return result
            }

        member this.Add2Todos (todo1, todo2) =
            result {
                let! (_, tagState) = tagsStateViewer ()
                let tagIds = tagState.GetTags() |>> (fun x -> x.Id)

                let! (_, categoriesState) =  categoryStateViewer ()
                let categoryIds = categoriesState.GetCategories() |>> (fun x -> x.Id)

                let! categoryId1IsValid =    
                    (todo1.CategoryIds.IsEmpty ||
                    todo1.CategoryIds |> List.forall (fun x -> (categoryIds |> List.contains x)))
                    |> Result.ofBool "A category reference contained in the todo is related to a category that does not exist"

                let! categoryId2IsValid =
                    (todo2.CategoryIds.IsEmpty ||
                    todo2.CategoryIds |> List.forall (fun x -> (categoryIds |> List.contains x)))
                    |> Result.ofBool "A category reference contained in the todo is related to a category that does not exist" 

                let! tagId1IsValid =    
                    (todo1.TagIds.IsEmpty ||
                    todo1.TagIds |> List.forall (fun x -> (tagIds |> List.contains x)))
                    |> Result.ofBool "A tag reference contained in the todo is related to a tag that does not exist"

                let! tagId2IsValid =    
                    (todo2.TagIds.IsEmpty ||
                    todo2.TagIds |> List.forall (fun x -> (tagIds |> List.contains x)))
                    |> Result.ofBool "A tag reference contained in the todo is related to a tag that does not exist"

                let! result =
                    (todo1, todo2)
                    |> TodoCommand'.Add2Todos
                    |> runCommand<TodosContextUpgraded, TodoEvent', string> storage eventBroker
                return result
            }

        member this.RemoveTodo id =
            result {
                let! result =
                    id
                    |> TodoCommand'.RemoveTodo
                    |> runCommand<TodosContextUpgraded, TodoEvent', string> storage eventBroker
                return result
            }

        member this.GetAllCategories() =
            result {
                let! (_, state) = categoryStateViewer ()
                return state.GetCategories()
            }

        member this.AddCategory category =
            result {
                let! result =
                    category
                    |> CategoryCommand.AddCategory
                    |> runCommand<CategoriesContext, CategoryEvent, string> storage eventBroker
                return result 
            }

        member this.RemoveCategory id =
            result {
                let removeCategory = CategoryCommand.RemoveCategory id
                let removeCategoryRef = TodoCommand'.RemoveCategoryRef id
                let! result =
                    runTwoCommands<
                        CategoriesContext, 
                        TodosContextUpgraded, 
                        CategoryEvent, 
                        TodoEvent',
                        string> 
                        storage eventBroker removeCategory removeCategoryRef
                return result 
            }

        member this.AddTag tag =
            result {
                let! result =
                    tag
                    |> AddTag
                    |> runCommand<TagsContext, TagEvent, string> storage eventBroker
                return result 
            }

        member this.removeTag id =
            result {
                let removeTag = TagCommand.RemoveTag id
                let removeTagRef = TodoCommand'.RemoveTagRef id
                let! result = 
                    runTwoCommands<TagsContext, TodosContextUpgraded, TagEvent, TodoEvent', string> 
                        storage eventBroker removeTag removeTagRef
                return result
            }

        member this.GetAllTags () =
            result {
                let! (_, state) = 
                    tagsStateViewer ()
                let tags = state.GetTags()
                return tags
            }

        member this.TodoReport (dateFrom: DateTime)  (dateTo: DateTime) =
            // let events = storage.GetEventsInATimeInterval TodosContextUpgraded.Version TodosContextUpgraded.StorageName dateFrom dateTo |>> snd
            result {
                let! events = storage.GetEventsInATimeInterval TodosContextUpgraded.Version TodosContextUpgraded.StorageName dateFrom dateTo 
                let events = events |>> snd
                let! events'' = 
                    events |> List.traverseResultM (fun x -> TodoEvents.TodoEvent'.Deserialize x)
                return 
                    { InitTime = dateFrom; EndTime = dateTo; TodoEvents = events'' }
            }


