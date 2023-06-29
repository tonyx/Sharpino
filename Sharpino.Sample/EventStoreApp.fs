
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
    type EventStoreApp(storage: EventStoreBridge) =
        member this.AddTag tag =
            let cmd =
                ResultCE.result {
                    let! command = 
                        tag
                        |> TagCommand.AddTag
                        |> runCommand<TagsAggregate, TagEvent> storage
                    return ()
                }
            ()
                
        member this.GetAllTags() =
            ResultCE.result {
                let state = Cache.CurrentState<TagsAggregate>.Instance.Lookup(TagsAggregate.StorageName, TagsAggregate.Zero) :?> TagsAggregate 
                let tags = state.GetTags()
                return tags
            }

        member this.GetAllTodos() =
            ResultCE.result {
                let state = Cache.CurrentState<TodosAggregate>.Instance.Lookup(TodosAggregate.StorageName, TodosAggregate.Zero) :?> TodosAggregate 
                let todos = state.GetTodos()
                return todos
            }

        member this.AddTodo todo =
            ResultCE.result {
                let tagState = Cache.CurrentState<TagsAggregate>.Instance.Lookup(TagsAggregate.StorageName, TagsAggregate.Zero) :?> TagsAggregate 
                let tagIds = tagState.GetTags() |>> fun x -> x.Id
                let! tagIdIsValid = 
                    (todo.TagIds.IsEmpty || 
                    (todo.TagIds |> List.forall (fun x -> tagIds |> List.contains x)))
                    |> boolToResult "Tag id is not valid"

                let! command = 
                    todo
                    |> TodoCommand.AddTodo
                    |> runCommand<TodosAggregate, TodoEvent> storage
                return ()
            }
