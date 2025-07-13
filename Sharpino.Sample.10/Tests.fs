namespace Sharpino.Sample._10.Tests
open System
open DotNetEnv
open Expecto
open Sharpino
open Sharpino.CommandHandler
open Sharpino.PgBinaryStore
open Sharpino.PgStorage
open Sharpino.Sample._10
open Sharpino.Sample._10.CounterApi
open Sharpino.Sample._10.Models
open Sharpino.Sample._10.Models.Account
open Sharpino.Sample._10.Models.AccountEvents
open Sharpino.Sample._10.Models.Counter
open Sharpino.Sample._10.Models.Counter
open Sharpino.Sample._10.Models.Events
open Sharpino.Storage
open Sharpino.TestUtils
open Sharpino.Cache

module Tests =
    Env.Load()
    let password = Environment.GetEnvironmentVariable("password")
    let connection =
        "Server=127.0.0.1;"+
        "Database=sharpino_sample_10;" +
        "User Id=safe;"+
        $"Password={password}"
    
    let setUp (eventStore: IEventStore<byte[]>) =
        eventStore.Reset Counter.Version Counter.StorageName
        eventStore.ResetAggregateStream Counter.Version Counter.StorageName
        eventStore.Reset Account.Version Account.StorageName
        eventStore.ResetAggregateStream Account.Version Account.StorageName
        AggregateCache2.Instance.Clear ()
    [<Tests>]
    let tests =
        testList "Sharpino.Sample._10.Tests" [
            testCase "create and retrieve a counter" <| fun _ ->
                let eventStore = PgBinaryStore connection
                setUp eventStore
                let api = CounterApi(eventStore, doNothingBroker, getAggregateStorageFreshStateViewer<Counter, CounterEvents, byte[]> eventStore, getAggregateStorageFreshStateViewer<Account, AccountEvents, byte[]> eventStore)
                let counter = Counter.Initial
                let createCounter = api.CreateCounter counter
                Expect.isOk createCounter "should be ok"
                let retrievedCounter = api.GetCounter counter.Id
                Expect.isOk retrievedCounter "should be ok"
                let counterValue = retrievedCounter.OkValue
                Expect.equal counterValue.Id counter.Id "should be equal"
            
            testCase "increment a counter and retrieve it" <| fun _ ->
                let eventStore = PgBinaryStore connection
                setUp eventStore
                let api = CounterApi(eventStore, doNothingBroker, getAggregateStorageFreshStateViewer<Counter, CounterEvents, byte[]> eventStore, getAggregateStorageFreshStateViewer<Account, AccountEvents, byte[]> eventStore)
                let counter = Counter.Initial
                let createCounter = api.CreateCounter counter
                Expect.isOk createCounter "should be ok"
                let retrievedCounter = api.GetCounter counter.Id
                Expect.isOk retrievedCounter "should be ok"
                let counterValue = retrievedCounter.OkValue
                Expect.equal counterValue.Id counter.Id "should be equal"
                let incrementCounter = api.IncrementCounter counter.Id
                Expect.isOk incrementCounter "should be ok"
                let retrievedCounter = api.GetCounter counter.Id
                Expect.isOk retrievedCounter "should be ok"
                let counterValue = retrievedCounter.OkValue
                Expect.equal counterValue.Id counter.Id "should be equal"
                Expect.equal counterValue.Value 1 "should be equal"
            
            testCase "increment many counters and retrieve them"   <| fun _ ->
                let eventStore = PgBinaryStore connection
                setUp eventStore
                let api = CounterApi(eventStore, doNothingBroker, getAggregateStorageFreshStateViewer<Counter, CounterEvents, byte[]> eventStore, getAggregateStorageFreshStateViewer<Account, AccountEvents, byte[]> eventStore)
                let counters =
                    [
                        Counter.Initial; Counter.Initial; Counter.Initial; Counter.Initial
                    ]
                let createCounters =
                    counters
                    |> List.map (fun c -> api.CreateCounter c)
                Expect.isTrue (createCounters |> List.forall (fun x -> x.IsOk)) "should be all Ok"
                
                let incrementCounters =
                    api.IncrementManyCounters (counters |> List.map _.Id)
                
                Expect.isTrue (incrementCounters.IsOk) "should be Ok"
                
                let retrievedCounters =
                    counters
                    |> List.map (fun c -> api.GetCounter c.Id)
                
                Expect.isTrue (retrievedCounters |> List.forall (fun x -> x.IsOk)) "should be all Ok"
                
                let retrievedCounters' =
                    retrievedCounters
                    |> List.map _.OkValue
                
                Expect.isTrue (retrievedCounters' |> List.forall (fun x -> x.Value = 1)) "should be all 1"
                
            testCase "create counters and accounts and execute commands on them in sparse order (type independent way of running any command)"   <| fun _ ->
                // given
                let eventStore = PgBinaryStore connection
                setUp eventStore
                let api = CounterApi(eventStore, doNothingBroker, getAggregateStorageFreshStateViewer<Counter, CounterEvents, byte[]> eventStore, getAggregateStorageFreshStateViewer<Account, AccountEvents, byte[]> eventStore)
                let counters =
                    [
                        Counter.Initial; Counter.Initial; Counter.Initial; Counter.Initial
                    ]
                let accounts =
                    [
                        Account.MkAccount ("a1", 1); Account.MkAccount ("a2", 1); Account.MkAccount ("a3", 1); Account.MkAccount ("a4", 1)
                    ]
                let createCounters =
                    counters
                    |> List.map (fun c -> api.CreateCounter c)
                Expect.isTrue (createCounters |> List.forall _.IsOk) "should be all Ok"
                
                let createAccounts =
                    accounts
                    |> List.map (fun c -> api.CreateAccount c)
                Expect.isTrue (createAccounts |> List.forall _.IsOk) "should be all Ok"
             
                let counterIds =  (counters  |> List.map _.Id)
                let accountIds =  (accounts  |> List.map _.Id)
                
                // when
                let tryIncrementCountersAndAccounts =
                    api.IncrementCountersAndAccounts (counterIds, accountIds)
                Expect.isOk tryIncrementCountersAndAccounts "should be Ok"
                
                // then
                let retrievedCounters =
                    counters
                    |> List.map (fun c -> api.GetCounter c.Id)
                
                Expect.isTrue (retrievedCounters |> List.forall (fun x -> x.IsOk)) "should be all Ok"
                
                let retrievedCounters' =
                    retrievedCounters
                    |> List.map _.OkValue
                
                Expect.isTrue (retrievedCounters' |> List.forall (fun x -> x.Value = 1)) "should be all 2"
                
                let retrievedAccounts =
                    accounts
                    |> List.map (fun c -> api.GetAccount c.Id)
                
                Expect.isTrue (retrievedAccounts |> List.forall (fun x -> x.IsOk)) "should be all Ok"
                
                let okAmountsOfRetrievedAccounts =
                    retrievedAccounts |> List.map (fun x -> x.OkValue) |> List.map (fun x -> x.Amount)
               
                Expect.allEqual okAmountsOfRetrievedAccounts 2 "should be all 2"
                Expect.isTrue true "true"
                
                Expect.isTrue (retrievedCounters |> List.forall (fun x -> x.IsOk)) "should be all Ok"
                
                let retrievedCounters =
                    retrievedCounters
                    |> List.map _.OkValue
                
                Expect.isTrue (retrievedCounters |> List.forall (fun x -> x.Value = 1)) "should be all 2"
        ]
