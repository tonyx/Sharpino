
module Tests.Tonyx.EventSourcing.Sample.MigrationTests

open Expecto
open System
open FSharp.Core

open Tonyx.EventSourcing.Sample.TodosAggregate
open Tonyx.EventSourcing.Sample.Todos.Models.CategoriesModel
open Tonyx.EventSourcing.Sample.Todos.Models.TodosModel
open Tonyx.EventSourcing.Sample.TagsAggregate
open Tonyx.EventSourcing.Sample.Tags.Models.TagsModel
open Tonyx.EventSourcing.Utils
open Tonyx.EventSourcing.Sample_02.CategoriesAggregate
open Tonyx.EventSourcing.Sample
open Tonyx.EventSourcing

let db = Repository.storage

let setUp() =
    db.Reset "_01" TodosAggregate.StorageName 
    Cache.EventCache<TodosAggregate>.Instance.Clear()
    Cache.SnapCache<TodosAggregate>.Instance.Clear()

    db.Reset "_01" TagsAggregate.StorageName
    Cache.EventCache<TagsAggregate>.Instance.Clear()
    Cache.SnapCache<TagsAggregate>.Instance.Clear()

    db.Reset "_02" TagsAggregate.StorageName
    Cache.EventCache<TagsAggregate>.Instance.Clear()
    Cache.SnapCache<TagsAggregate>.Instance.Clear()

    db.Reset "_02" CategoriesAggregate.StorageName
    Cache.EventCache<CategoriesAggregate>.Instance.Clear()
    Cache.SnapCache<CategoriesAggregate>.Instance.Clear()

[<Tests>]
let migrateTests =
    ptestList "migrate tests" [
        testCase "can migrate category models from todo aggregare 01 to the new proper category aggregate 02 - Ok" <| fun _ ->
            setUp()

            let category = { Id = Guid.NewGuid(); Name = "test"}
            let category2 = { Id = Guid.NewGuid(); Name = "test2"}
            let category3 = { Id = Guid.NewGuid(); Name = "test3"}
            let added =
                ceResult {
                    let! _ = App.addCategory category
                    let! _ = App.addCategory category2
                    let! _ = App.addCategory category3
                    return ()
                }
            let migrated = App.migrate()
            Expect.isOk migrated "should be ok"

            let actualMigrated = App.getAllCategories'() |> Result.get        
            Expect.equal (actualMigrated |> List.length) 3 "should be equal"

            let expected = [category; category2; category3] |> Set.ofList
            Expect.equal (actualMigrated |> Set.ofList) expected "should be equal"
    ]
 