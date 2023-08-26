module Tests.Sharpino.Sample.CosmoDbTest

open Expecto
open System
open FSharp.Core

open Sharpino
open Sharpino.Storage
open Sharpino.Utils
open Sharpino.Sample.EventStoreApp
open Sharpino.Sample.Entities.Tags
open Sharpino.Sample.Entities.Todos
open Sharpino.Sample.Entities.Categories

open Sharpino.Sample.TagsAggregate
open Sharpino.Sample.TodosAggregate
open Sharpino.Sample.CategoriesAggregate

open Sharpino.Sample.Todos.TodoEvents
open Sharpino.Sample.Categories.CategoriesEvents
open Sharpino.Sample.Tags.TagsEvents

[<Tests>]
let comsostests =
    let accountEndpoint =  "https://localhost:8081"
    let authKeyOrResourceToken = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=="
    let todoCosmosBridge = Sharpino.CosmosDb.CosmosDbBridge<TodosAggregate, TodoEvent>(accountEndpoint, authKeyOrResourceToken)

    testList "bla" [
        ftestCase "bla" <| fun _ ->
            let todo: Todo = {Id = Guid.NewGuid();  Description = "ehi";  CategoryIds = []; TagIds = []}
            let myEvent = TodoEvent.TodoAdded todo
            let added = todoCosmosBridge.AddEvents "01" [myEvent] "_todos"
            printf "added result %A\n\n" added
            Expect.isOk added "should be ok"
            Expect.isTrue true "bla"
    ]