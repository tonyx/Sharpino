
namespace Sharpino.Sample

open Sharpino
open Sharpino.Utils

open Sharpino.Sample
open Sharpino.Storage
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
open System
open FSharpPlus
open FsToolkit.ErrorHandling
module AppRefStorage =
    open Sharpino.RepositoryRef

    type CurrentVersionAppRef(storage: IStorageRefactor) =
        member this.GetAllTodos() =
            printf "get all todos 100\n"
            async {
                return
                    ResultCE.result  {
                        printf "get all todos 110\n"
                        let! (_, state) = storage |> getState<TodosAggregate, TodoEvent>
                        printf "get all todos 120\n"
                        let todos = state.GetTodos()
                        printf "get all todos 130\n"
                        return todos
                    }
            }
            |> Async.RunSynchronously

        member this.AddTodo todo =
            let f = fun () ->
                ResultCE.result {
                    let! (_, tagState) = storage |> getState<TagsAggregate, TagEvent> 
                    let tagIds = tagState.GetTags() |>> (fun x -> x.Id)

                    let! tagIdIsValid =    
                        (todo.TagIds.IsEmpty ||
                        todo.TagIds |> List.forall (fun x -> (tagIds |> List.contains x)))
                        |> boolToResult "A tag reference contained in the todo is related to a tag that does not exist"

                    let! _ =
                        todo
                        |> TodoCommand.AddTodo
                        |> runCommand<TodosAggregate, TodoEvent> storage
                    let _ = 
                        storage
                        |> mkSnapshotIfInterval<TodosAggregate, TodoEvent>
                return ()
            }
            async {
                return processor.PostAndReply (fun rc -> f, rc)
            }
            |> Async.RunSynchronously

        member this.Add2Todos (todo1, todo2) =
            let f = fun () ->
                ResultCE.result {
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
                        |> runCommand<TodosAggregate, TodoEvent> storage
                    let _ =  
                        storage
                        |> mkSnapshotIfInterval<TodosAggregate, TodoEvent>
                    return ()
                }
            async {
                return processor.PostAndReply (fun rc -> f, rc)
            } 
            |> Async.RunSynchronously

        member this.RemoveTodo id =
            let f = fun () ->
                ResultCE.result {
                    let! _ =
                        id
                        |> TodoCommand.RemoveTodo
                        |> runCommand<TodosAggregate, TodoEvent> storage
                    let _ = 
                        storage
                        |> mkSnapshotIfInterval<TodosAggregate, TodoEvent>
                    return ()
                }
            async {
                return processor.PostAndReply (fun rc -> f, rc)
            } |> Async.RunSynchronously
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
        member this.AddCategory category =
            let f = fun () ->
                ResultCE.result {
                    let! _ =
                        category
                        |> TodoCommand.AddCategory
                        |> runCommand<TodosAggregate, TodoEvent> storage
                    let _ = 
                        storage
                        |> mkSnapshotIfInterval<TodosAggregate, TodoEvent>
                    return ()
                }
            async {
                return processor.PostAndReply (fun rc -> f, rc)
            }
            |> Async.RunSynchronously

        member this.RemoveCategory id = 
            let f = fun () ->
                ResultCE.result {
                    let! _ =
                        id
                        |> TodoCommand.RemoveCategory
                        |> runCommand<TodosAggregate, TodoEvent> storage
                    let _ = 
                        storage 
                        |> mkSnapshotIfInterval<TodosAggregate, TodoEvent> 
                    return ()
                }
            async {
                return processor.PostAndReply (fun rc -> f, rc)
            } 
            |> Async.RunSynchronously
        member this.AddTag tag =
            let f = fun() ->
                ResultCE.result {
                    let! _ =
                        tag
                        |> AddTag
                        |> runCommand<TagsAggregate, TagEvent> storage
                    let _ =  
                        storage 
                        |> mkSnapshotIfInterval<TagsAggregate, TagEvent> 
                    return ()
                }
            async {
                return processor.PostAndReply (fun rc -> f, rc)
            } 
            |> Async.RunSynchronously

        member this.RemoveTag id =
            let f = fun () ->
                ResultCE.result {
                    let removeTag = TagCommand.RemoveTag id
                    let removeTagRef = TodoCommand.RemoveTagRef id
                    let! _ = runTwoCommands<TagsAggregate, TodosAggregate, TagEvent, TodoEvent> storage removeTag removeTagRef
                    let _ = 
                        storage
                        |> mkSnapshotIfInterval<TagsAggregate, TagEvent>
                    let _ = 
                        storage
                        |> mkSnapshotIfInterval<TodosAggregate, TodoEvent>
                    return ()
                }
            async {
                return processor.PostAndReply (fun rc -> f, rc)
            }
            |> Async.RunSynchronously

        member this.GetAllTags () =
            async {
                return
                    ResultCE.result {
                        let! (_, state) = storage |> getState<TagsAggregate, TagEvent>
                        let tags = state.GetTags()
                        return tags
                    }
            }
            |> Async.RunSynchronously