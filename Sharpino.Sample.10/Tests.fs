namespace Sharpino.Sample._10.Tests
open System
open DotNetEnv
open Expecto
open Microsoft.Extensions.Hosting
open Sharpino
open Sharpino.CommandHandler
open Sharpino.EventBroker
open Sharpino.PgBinaryStore
open Sharpino.PgStorage
open Sharpino.Sample._10
open Sharpino.Sample._10.CounterApi
open Sharpino.Sample._10.Models
open Sharpino.Sample._10.Models.Account
open Sharpino.Sample._10.Models.Consumer.AccountConsumer
open Sharpino.Sample._10.Models.AccountEvents
open Sharpino.Sample._10.Models.Counter
open Sharpino.Sample._10.Models.Counter
open Sharpino.Sample._10.Models.Consumer.CounterConsumer
open Sharpino.Sample._10.Models.Events
open Sharpino.Storage
open Sharpino.TestUtils
open Sharpino.Cache
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Sharpino.RabbitMq
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks


module Tests =
    
    Env.Load()
    let password = Environment.GetEnvironmentVariable("password")
    
    let emptyMessageSenders: StreamName -> MessageSender =
        fun _ ->
            fun _ ->
                ValueTask.CompletedTask
    let connection =
        "Server=127.0.0.1;"+
        "Database=sharpino_sample_10;" +
        "User Id=safe;"+
        $"Password={password}"
    let hostBuilder =
        Host.CreateDefaultBuilder()
            .ConfigureServices (fun (services: IServiceCollection) ->
                services.AddSingleton<RabbitMqReceiver>() |> ignore
                services.AddHostedService<CounterConsumer>() |> ignore
                services.AddHostedService<AccountConsumer>() |> ignore
                ()
        )
   
    // #if RABBITMQ 
    let host = hostBuilder.Build()
    let hostTask = host.StartAsync()
    
    let services = host.Services
    let counterConsumer =
        services.GetServices<IHostedService>()
        |> Seq.find (fun s -> s.GetType() = typeof<CounterConsumer>)
        :?> CounterConsumer
   
    counterConsumer.SetFallbackAggregateStateRetriever (getAggregateStorageFreshStateViewer<Counter, CounterEvents, byte[]> (PgBinaryStore connection))
    
    let accountConsumer =
        services.GetServices<IHostedService>()
        |> Seq.find (fun s -> s.GetType() = typeof<AccountConsumer>)
        :?> AccountConsumer
        
    accountConsumer.SetFallbackAggregateStateRetriever (getAggregateStorageFreshStateViewer<Account, AccountEvents, byte[]> (PgBinaryStore connection))
    let rabbitMqCounterStateViewer = counterConsumer.GetAggregateState
    let rabbitMqAccountStateViewer = accountConsumer.GetAggregateState
    
    let aggregateMessageSenders = System.Collections.Generic.Dictionary<string, MessageSender>()
    
    let counterMessageSender =
        let streamName = Counter.Version + Counter.StorageName
        mkMessageSender "127.0.0.1"streamName
        |> Result.get
   
    let accountMessageSender =
        let streamName = Account.Version + Account.StorageName
        mkMessageSender "127.0.0.1"streamName
        |> Result.get
    
    aggregateMessageSenders.Add (Counter.Version + Counter.StorageName, counterMessageSender)
    aggregateMessageSenders.Add (Account.Version + Account.StorageName, accountMessageSender)
    
    let messageSenders =
        fun queueName ->
            let sender = aggregateMessageSenders.TryGetValue queueName
            match sender with
            | true, sender -> sender
            | false, _ -> failwith (sprintf "queue not found: %s" queueName)
        
    // #endif
    
    let setUp (eventStore: IEventStore<byte[]>) =
        eventStore.Reset Counter.Version Counter.StorageName
        eventStore.ResetAggregateStream Counter.Version Counter.StorageName
        eventStore.Reset Account.Version Account.StorageName
        eventStore.ResetAggregateStream Account.Version Account.StorageName
        AggregateCache2.Instance.Clear ()
  
    let eventStore = PgBinaryStore connection
    
    #if RABBITMQ
    let counterApi = fun () -> CounterApi (eventStore, messageSenders, rabbitMqCounterStateViewer , rabbitMqAccountStateViewer)
    let timeout = 1000
    #else
    let counterApi = fun () -> CounterApi (eventStore, emptyMessageSenders, getAggregateStorageFreshStateViewer<Counter, CounterEvents, byte[]> eventStore, getAggregateStorageFreshStateViewer<Account, AccountEvents, byte[]> eventStore)
    let timeout = 0
    #endif
        
    [<Tests>]
    let tests =
        testList "Sharpino.Sample._10.Tests" [
            testCase "create and retrieve a counter" <| fun _ ->
                let eventStore = PgBinaryStore connection
                setUp eventStore
                let api = counterApi ()
                let counter = Counter.Initial
                let createCounter = api.CreateCounter counter
                Expect.isOk createCounter "should be ok"
                Async.Sleep timeout |> Async.RunSynchronously
                let retrievedCounter = api.GetCounter counter.Id
                Expect.isOk retrievedCounter "should be ok"
                let counterValue = retrievedCounter.OkValue
                Expect.equal counterValue.Id counter.Id "should be equal"
            
            testCase "increment a counter and retrieve it" <| fun _ ->
                let eventStore = PgBinaryStore connection
                setUp eventStore
                let api = counterApi ()
                let counter = Counter.Initial
                let createCounter = api.CreateCounter counter
                Expect.isOk createCounter "should be ok"
                Async.Sleep timeout |> Async.RunSynchronously
                let retrievedCounter = api.GetCounter counter.Id
                Expect.isOk retrievedCounter "should be ok"
                let counterValue = retrievedCounter.OkValue
                Expect.equal counterValue.Id counter.Id "should be equal"
                let incrementCounter = api.IncrementCounter counter.Id
                Expect.isOk incrementCounter "should be ok"
                Async.Sleep timeout |> Async.RunSynchronously
                let retrievedCounter = api.GetCounter counter.Id
                Expect.isOk retrievedCounter "should be ok"
                let counterValue = retrievedCounter.OkValue
                Expect.equal counterValue.Id counter.Id "should be equal"
                Expect.equal counterValue.Value 1 "should be equal"
            
            testCase "increment many counters and retrieve them"   <| fun _ ->
                let eventStore = PgBinaryStore connection
                setUp eventStore
                let api = counterApi ()
                let counters =
                    [
                        Counter.Initial; Counter.Initial; Counter.Initial; Counter.Initial
                    ]
                let createCounters =
                    counters
                    |> List.map (fun c -> api.CreateCounter c)
                Expect.isTrue (createCounters |> List.forall (fun x -> x.IsOk)) "should be all Ok"
                
                Async.Sleep timeout |> Async.RunSynchronously
                let incrementCounters =
                    api.IncrementManyCounters (counters |> List.map _.Id)
                
                Expect.isTrue (incrementCounters.IsOk) "should be Ok"
                
                Async.Sleep timeout |> Async.RunSynchronously
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
                let api = counterApi ()
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
             
                let counterIds =  (counters |> List.map _.Id)
                let accountIds =  (accounts |> List.map _.Id)
                
                // when
                Async.Sleep timeout |> Async.RunSynchronously
                let tryIncrementCountersAndAccounts =
                    api.IncrementCountersAndAccounts (counterIds, accountIds)
                Expect.isOk tryIncrementCountersAndAccounts "should be Ok"
                
                // then
                Async.Sleep timeout |> Async.RunSynchronously
                let retrievedCounters =
                    counters
                    |> List.map (fun c -> api.GetCounter c.Id)
                
                Expect.isTrue (retrievedCounters |> List.forall (fun x -> x.IsOk)) "should be all Ok"
                
                let retrievedCounters' =
                    retrievedCounters
                    |> List.map _.OkValue
                
                Expect.isTrue (retrievedCounters' |> List.forall (fun x -> x.Value = 1)) "should be all 2"
                
                Async.Sleep timeout |> Async.RunSynchronously
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
