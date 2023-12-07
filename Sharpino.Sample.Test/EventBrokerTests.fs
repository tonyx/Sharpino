
module Tests.Sharpino.Sample.EventBrokerTests

open Expecto
open System
open FSharp.Core

open Sharpino
open Tests.Sharpino.Sample.MultiVersionsTests
open Sharpino.ApplicationInstance
open Sharpino.Sample
open Sharpino.Sample.TodosCluster
open Sharpino.Sample.TagsCluster
open Sharpino.Sample.Todos
open Sharpino.Sample.Entities.Categories
open Sharpino.Sample.Entities.Todos
open Sharpino.Sample.Entities.Tags
open Sharpino.Sample.Tags
open Sharpino.Sample.Tags.TagsEvents
open Sharpino.Sample.Shared.Entities
open Sharpino.Utils
open Sharpino.EventSourcing.Sample
open Sharpino.EventSourcing.Sample.AppVersions
open Sharpino.Sample.Todos.TodoEvents
open Tests.Sharpino.Shared
open Sharpino.KafkaReceiver

open Sharpino.TestUtils
open Sharpino.KafkaReceiver
open System.Threading
open FsToolkit.ErrorHandling
open Sharpino.KafkaBroker
open Sharpino.Storage
open log4net

[<Tests>]
let eventBrokerStateBuildingTests =

    testList "build state by querying the event broker" [
        // let todoEventBrokerState = mkEventBrokerStateKeeper<TodosCluster, TodoEvents.TodoEvent> "localhost:9092" "sharpinoTestClient"

        testCase "when there are no event you make refresh and the state is still the intitial one - Ok" <| fun _ ->
            let todoEventBrokerState = mkEventBrokerStateKeeper<TodosCluster, TodoEvents.TodoEvent> "localhost:9092" "sharpinoTestClient" (ApplicationInstance.Instance.GetGuid())
            let sut = currentVersionPgWithKafkaApp
            sut._reset()
            todoEventBrokerState.Refresh(100)
            let state = todoEventBrokerState.GetState ()
            Expect.equal state TodosCluster.Zero "should be equal"

        testCase "create an event and read the state from the event broker view - Ok" <| fun _ ->
            let todoEventBrokerState = mkEventBrokerStateKeeper<TodosCluster, TodoEvents.TodoEvent> "localhost:9092" "sharpinoTestClient" (ApplicationInstance.Instance.GetGuid())
            let sut = currentVersionPgWithKafkaApp
            sut._reset()
            let todo = mkTodo (Guid.NewGuid()) "testasdf" [] []
            let added = sut.addTodo todo
            Expect.isOk added "should be ok"
            todoEventBrokerState.Refresh(10000)
            let state = todoEventBrokerState.GetState ()
            let todos = state.GetTodos()
            Expect.equal todos [todo] "should be equal"



///////


        testCase "raise an event so the eventBrokerState will be able to build the state accordingly - Ok " <| fun _ ->
            let todoEventBrokerState = mkEventBrokerStateKeeper<TodosCluster, TodoEvents.TodoEvent> "localhost:9092" "sharpinoTestClient" (ApplicationInstance.Instance.GetGuid())
            let sut = currentVersionPgWithKafkaApp
            sut._reset()
            let todo = mkTodo (Guid.NewGuid()) "testasdfQ" [] []
            let added = sut.addTodo todo
            Expect.isOk added "should be ok"
            let state = todoEventBrokerState.UpdateState(ApplicationInstance.Instance.GetGuid(), 10000)
            let newState = todoEventBrokerState.GetState()
            // let state = todoEventBrokerState.GetState (ApplicationInstance.Instance.GetGuid())
            let todos = newState.GetTodos()
            Expect.equal todos [todo] "true"



        // ftestCase "add a todo and read the state from the event broker view - Ok" <| fun _ ->
        //     let todoEventBrokerState = mkEventBrokerStateKeeper<TodosCluster, TodoEvents.TodoEvent> "localhost:9092" "sharpinoTestClient"
        //     let sut = currentVersionPgWithKafkaApp
        //     sut._reset()
        //     let todo = mkTodo (Guid.NewGuid()) "testasdfQ" [] []
        //     let added = sut.addTodo todo
        //     Expect.isOk added "should be ok"
        //     let state = todoEventBrokerState.GetState(ApplicationInstance.Instance.GetGuid(), 10000)
        //     let todos = state.GetTodos()
        //     Expect.equal todos [todo] "true"

        testCase "add many todos and read the state from the event broker view - Ok" <| fun _ ->
            let todoEventBrokerState = mkEventBrokerStateKeeper<TodosCluster, TodoEvents.TodoEvent> "localhost:9092" "sharpinoTestClient" (ApplicationInstance.Instance.GetGuid())
            let sut = currentVersionPgWithKafkaApp
            sut._reset()
            let todo = mkTodo (Guid.NewGuid()) "testasdfQg" [] []
            let todo2 = mkTodo (Guid.NewGuid()) "testasdfQQ" [] []
            let added = sut.addTodo todo
            Expect.isOk added "should be ok"
            let added2 = sut.addTodo todo2
            Expect.isOk added2 "should be ok"
            let state = todoEventBrokerState.UpdateState(ApplicationInstance.Instance.GetGuid(), 10000)
            let todos = state.GetTodos()
            Expect.equal (todos |> Set.ofList) ([todo; todo2] |> Set.ofList) "should be equal"

    ]
    |> testSequenced