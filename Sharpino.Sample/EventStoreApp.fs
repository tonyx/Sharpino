
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
            ResultCE.result {
                let! command = 
                    tag
                    |> TagCommand.AddTag
                    |> runCommand<TagsAggregate, TagEvent> storage
                return ()
            }
        member this.GetAllTags() =
            ResultCE.result {
                let! (_, state) = getState<TagsAggregate, TagEvent> storage
                let tags = state.GetTags()
                return tags
            }
