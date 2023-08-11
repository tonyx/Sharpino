
namespace Sharpino.Sample

open Sharpino
open Sharpino.Utils
open Sharpino.LightRepository

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

module EventStoreApp =
    open Sharpino.Lib.EvStore
    open Sharpino.Sample.Tags.TagCommands
    type EventStoreApp(storage: EventStoreBridge) =
        member this.AddTag tag =
            let f = fun() ->
                ResultCE.result {
                    return!
                        tag
                        |> TagCommand.AddTag
                        |> runCommand<TagsAggregate, TagEvent> storage
                }
            async {
                return lightProcessor.PostAndReply (fun rc -> f, rc)
            }
            |> Async.RunSynchronously
                
        member this.GetAllTags() =
            ResultCE.result {
                // FOCUS
                // let state = Cache.CurrentState<TagsAggregate>.Instance.Lookup(TagsAggregate.StorageName, TagsAggregate.Zero) :?> TagsAggregate 
                let (id, stateX ) = Cache.CurrentStateRef<TagsAggregate>.Instance.Lookup(TagsAggregate.StorageName, (0 |> uint64, TagsAggregate.Zero)) // :?> (uint64 * TagsAggregate)
                let state' = stateX :?> TagsAggregate

                // let tags = state.GetTags()

                let tags = state'.GetTags()


                return tags
            }

        member this.GetAllTodos() =
            ResultCE.result {
                // let state = Cache.CurrentState<TodosAggregate>.Instance.Lookup(TodosAggregate.StorageName, TodosAggregate.Zero) :?> TodosAggregate 

                let (id, stateX ) = Cache.CurrentStateRef<TodosAggregate>.Instance.Lookup(TodosAggregate.StorageName, (0 |> uint64, TodosAggregate.Zero)) // :?> (uint64 * TagsAggregate)
                let state' = stateX :?> TodosAggregate

                // let todos = state.GetTodos()
                let todos = state'.GetTodos()

                return todos
            }

        member this.AddTodo todo =
            let f = fun() ->
                ResultCE.result {
                    // FOCUS
                    // let tagState = Cache.CurrentState<TagsAggregate>.Instance.Lookup(TagsAggregate.StorageName, TagsAggregate.Zero) :?> TagsAggregate 

                    let (id, stateX ) = Cache.CurrentStateRef<TagsAggregate>.Instance.Lookup(TagsAggregate.StorageName, (0 |> uint64, TagsAggregate.Zero)) // :?> (uint64 * TagsAggregate)
                    let tagState' = stateX :?> TagsAggregate

                    // let tagIds = tagState.GetTags() |>> fun x -> x.Id

                    let tagIds = tagState'.GetTags() |>> fun x -> x.Id

                    let! tagIdIsValid = 
                        (todo.TagIds.IsEmpty || 
                        (todo.TagIds |> List.forall (fun x -> tagIds |> List.contains x)))
                        |> boolToResult "Tag id is not valid"

                    return!
                        todo
                        |> TodoCommand.AddTodo
                        |> runCommand<TodosAggregate, TodoEvent> storage
                }
            async {
                return lightProcessor.PostAndReply (fun rc -> f, rc)
            }
            |> Async.RunSynchronously

        member this.RemoveTodo id =
            let f = fun() ->
                ResultCE.result {
                    return!
                        id
                        |> TodoCommand.RemoveTodo
                        |> runCommand<TodosAggregate, TodoEvent> storage
                }
            async { 
                return lightProcessor.PostAndReply (fun rc -> f, rc)
            }   
            |> Async.RunSynchronously


        member this.AddCategory category =
            let f = fun() ->
                ResultCE.result {
                    return!
                        category
                        |> TodoCommand.AddCategory
                        |> runCommand<TodosAggregate, TodoEvent> storage
                }
            async {
                return lightProcessor.PostAndReply (fun rc -> f, rc)
            }
            |> Async.RunSynchronously

        member this.GetAllCategories() =
            ResultCE.result {
                // FOCUS
                // let state = Cache.CurrentState<TodosAggregate>.Instance.Lookup(TodosAggregate.StorageName, TodosAggregate.Zero) :?> TodosAggregate 
                let (id, stateX ) = Cache.CurrentStateRef<TodosAggregate>.Instance.Lookup(TodosAggregate.StorageName, (0 |> uint64, TodosAggregate.Zero)) // :?> (uint64 * TagsAggregate)
                let state' = stateX :?> TodosAggregate

                // let categories = state.GetCategories()
                let categories = state'.GetCategories()

                return categories
            }

        member this.RemoveCategory id =
            let f = fun() ->
                ResultCE.result {
                    return! 
                        id
                        |> TodoCommand.RemoveCategory
                        |> runCommand<TodosAggregate, TodoEvent> storage
                }
            async { 
                return lightProcessor.PostAndReply (fun rc -> f, rc)
            }   
            |> Async.RunSynchronously

        member this.RemoveTag id =
            let f = fun() ->
                ResultCE.result {
                    let removeTag = TagCommand.RemoveTag id
                    let removeTagRef = TodoCommand.RemoveTagRef id
                    return! runTwoCommands<TagsAggregate, TodosAggregate, TagEvent, TodoEvent> storage removeTag removeTagRef
                }
            async { 
                return lightProcessor.PostAndReply (fun rc -> f, rc)
            }   
            |> Async.RunSynchronously
        member this.RemoveTagFakingErrorOnSecondCommand id =
            let f = fun() ->
                ResultCE.result {
                    let removeTag = TagCommand.RemoveTag id
                    let removeTagRef = TodoCommand.RemoveTagRef id
                    return! runTwoCommandsWithFailure_USE_IT_ONLY_TO_TEST_THE_UNDO<TagsAggregate, TodosAggregate, TagEvent, TodoEvent> storage removeTag removeTagRef
                }
            async { 
                return lightProcessor.PostAndReply (fun rc -> f, rc)
            }   
            |> Async.RunSynchronously


        member this.Add2Todos (todo1, todo2) =
            let f = fun() ->
                ResultCE.result {
                    // FOCUS
                    // let tagState = Cache.CurrentState<TagsAggregate>.Instance.Lookup(TagsAggregate.StorageName, TagsAggregate.Zero) :?> TagsAggregate
                    let (id, stateX ) = Cache.CurrentStateRef<TagsAggregate>.Instance.Lookup(TagsAggregate.StorageName, (0 |> uint64, TagsAggregate.Zero)) // :?> (uint64 * TagsAggregate)
                    let tagState' = stateX :?> TagsAggregate
                    let tagIds = 
                        // tagState.GetTags() 
                        tagState'.GetTags() 
                        |> List.map (fun x -> x.Id)
                    let! tagId1IsValid =
                        (todo1.TagIds.IsEmpty || 
                        (todo1.TagIds |> List.forall (fun x -> tagIds |> List.contains x)))
                        |> boolToResult "Tag id is not valid"
                    let! tagId2IsValid =
                        (todo2.TagIds.IsEmpty || 
                        (todo2.TagIds |> List.forall (fun x -> tagIds |> List.contains x)))
                        |> boolToResult "Tag id is not valid"

                    return! 
                        (todo1, todo2)
                        |> TodoCommand.Add2Todos
                        |> runCommand<TodosAggregate, TodoEvent> storage
                }
            async {
                return lightProcessor.PostAndReply (fun rc -> f, rc)
            }
            |> Async.RunSynchronously