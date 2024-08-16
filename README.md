# Sharpino


<img src="ico/sharpino.png" alt="drawing" width="50"/>


## A little F# Event Sourcing Library

[![NuGet version (Sharpino)](https://img.shields.io/nuget/v/Sharpino.svg?style=flat-square)](https://www.nuget.org/packages/Sharpino/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## What is it?

Support for Event-sourcing in F#.

## Features
- Supports in memory and Postgres event store. (EvenstoreDb is not supported anymore)
- Support publishing events to Apache Kafka Event (unstable. Don't use it)
- Example application with tests including Kafka subscriber. (unstable at the moment)
- Contexts represent sets of collections of entities (e.g. a collection of todos, a collection of tags, a collection of categories, etc.) associated with events.
- Aggregates are the same as contexts with many instances identified by Id (Guid).
- A specific technique helps refactoring (migration) between different versions of the application.


## Projects
__Sharpino.Lib.Core__:

- [Core.fs](Sharpino.Lib.Core/Core.fs): Abstract definition of _Events_, _Commands_ and _Undoer_ (the reverse of a command to be used if the used event store lacks transaction between streams). Definition of _EvolveUnForgivingErrors_ and "normal" _Evolve_. The former raises an error in processing invalid events.  The latter just skip those events. Some other types and functions are defined here. In a "SAFE stack" like project we may "safely" include the Core in a shared project (Fable Remoting style) in the sense that Fable can transpile it to JavaScript.

__Sharpino.Lib__:

- [CommandHandler.fs](Sharpino.Lib/CommandHandler.fs): Gets and stores snapshots, executes commands, and produces and stores events using the __event store__.
- [LightCommandHandler.fs](Sharpino.Lib/LightCommandHandler.fs): it is unmaintained. Meant to  gets and stores snapshots, executes commands, produces and stores events using an event store that supports publish/subscribe ( EventStoreDB).
- [PgEventStore.fs](Sharpino.Lib/PgEventStore.fs) and [MemoryStorage.fs](Sharpino.Lib/MemoryStorage.fs): Manages persistence of events in Postgres or in-memory respectively.
- [Cache.fs](Sharpino.Lib/Cache.fs). Caches the current state of contexts or aggregates. 

__Sharpino.Sample__:
You need a user called 'safe' with password 'safe' in your Postgres (if you want to use Postgres as Eventstore).

It is an example of a library for managing todos with tags and categories. There are two versions in the sense of two different configurations concerning the distribution of the models (collection of entities) between the contexts. There is a strategy to test the migration between versions (contexts refactoring) that is described in the code (See: [AppVersions.fs](Sharpino.Sample/AppVersions.fs) and [MultiVersionsTests.fs](Sharpino.Sample.Test/MultiVersionsTests.fs) )
.

One way to write any application is to include the library from nuget and then write your own:
- aggregates and contexts
- events and commands
- api layer.
- testing

Any example follows the same pattern.

It could be convenient to write the api layer so that it can refer to any eventstore (Postgres, or  in-memory) and an actual event broker (Kafka) or a neutral ("doNothing") event broker.
There are some facilities in test to run them in a parametrized way respect to the actual instance of the api layer (with the actual db based eventstore or just the in-memory for example).

Warning: some examples refers to previous version of the library as in the nuget repository.
Check the .fsproj to make sure if you want to use them as a blueprint for your experiments/applications aligned with
the latest version of the library.
 


-  __contexts__ (e.g. [TodosContext](Sharpino.Sample/Domain/Todos/Context.fs)) controls a partition of the collection of the entities and provides members to handle them. 

- __contexts__ members have corresponding __events__ ([e.g. TagsEvents](Sharpino.Sample/clusters/Tags/Events.fs)) that are Discriminated Unions cases. Event types implement the [Process](Sharpino.Lib/Core.fs) interface. 

- __contexts__ are related to __Commands__ (e.g. [TagCommand](Sharpino.Sample/clusters/Tags/Commands.fs)) that are Discriminated Unions cases that can return lists of events by implementing the [Executable](Sharpino.Lib/Core.fs) interface.

__Commands__ defines also _undoers_ are functions that can undo the commands to reverse action in a multiple-stream operation for storage that doesn't support multiple-stream transactions (see _LightCommandHandler_).
- A [Storage](Sharpino.Lib/DbStorage.fs) stores and retrieves _events_ and _snapshots_.
- The [__api layer__ functions](Sharpino.Sample/App.fs) provide business logic involving one or more clusters by accessing their state, and by building one or more commands and sending them to the __CommandHandler__.
- An example of how to handle multiple versions of the application to help refactoring and migration between different versions: [application versions](Sharpino.Sample/AppVersions.fs). 

- __aggregates__ works as context and I can handle multiple instances of an aggregate identified by Id (Guid). The example is in the sample application 4 (see below).

__Sharpino.Sample.tests__
- tests for the sample application
[Sharpino.Lib.fsproj](Sharpino.Lib%2FSharpino.Lib.fsproj)
__Sharpino.Sample.Kafka__
- scripts to set up Kafka topics corresponding to the contexts of the sample application.


## How to use it
- You can run the sample application as a rest service by running the following command from Sharpino.Sample folder:
```bash
dotnet run
```

- You can run the client Fable/Elmish sample application by running the following command from the Sharpino.Sample.Client folder:
```bash
npm install
npm start
```

- Just use ordinary dotnet command line tools for building the solution. Particularly you can run tests of the sample application by using the following command:
```bash
dotnet test 
```
You can also run the tests by the following command from  Sharpino.Sample.Tests folder:
```bash
dotnet run
```
In the latter case, you get the output from _Expecto_ test runner (in this case the console shows eventual standard output/printf).

By default, the tests run only the in-memory implementation of the storage. You can set up the Postgres tables and db by using dbmate.
In the Sharpino.Sample folder you can run the following command to set up the Postgres database:
```bash
dbmate -e up
```
(see the .env to set up the DATABASE_URL environment variable to connect to the Postgres database with a connection string).
If you have Eventstore the standard configuration should work. (I have tested it with Eventstore 20.10.2-alpha on M2 Apple Silicon chip under Docker).

## SAMPLE 4 
Sample 4 uses SAFE stack (Fable/Elmish). The aggregates are rows of seats that I can book. A skeleton of Elmish client is provided.

## SAMPLE 5
Sample 5 will just replace SAMPLE 4. uses SAFE stack (Fable/Elmish). Use the common SAFE stack basic operation for running and testing (under the directory Sharpino.Sample.5)
Please just look at the domain, and don't care that much about the u.i. (Elmish part). 


## Tests on eventstoredb. EventStroreDb is not mantained that much at the moment

The following line needs to stay commented out.

```Fsharp
        // (AppVersions.evSApp,                    AppVersions.evSApp,                 fun () -> () |> Result.Ok)
```


# Sample application 2 is a problem of booking seats in two rows of five seats. 
1. Booking seats among multiple rows (where those rows are aggregates) in an event-sourcing way.
2. Booking seats in a single row by two concurrent commands that singularly do not violate any invariant rule and yet the final state is potentially invalid.

## Problem 1

I have two rows of seats related to two different streams of events. Each row has 5 seats. I want to book a seat in row 1 and a seat in row 2. I want to do this in a single transaction so that if just one of the claimed seats is already booked then the entire multiple-row transaction fails and no seats are booked at all.
### Questions:
1) can you do this in a transactional way?
   Answer: yes because in-memory and Postgres event-store implementation as single sources of truth are transactional. The runTwoCommands in the Command Handler is transactional.
2) Can it handle more rows?
   up to tre. (runThreeCommands in the Command Handler)
3) Is Apache Kafka integration included in this example?
   No.

## Problem 2
There is an invariant rule that says that no booking can end up in leaving the only middle seat free in a row.
This invariant rule must be preserved even if two concurrent transactions try to book the two left seats and the two right seats independently so violating (together) this invariant.


## Sample application 4
The domain Sample application 4 is the same of the Sample 2 and uses aggregates to be able to create an arbitrary number of seat rows.
Invariants can be represented by quoted expressions so that, ideally, this may allow us to move toward a DSL (Example: "no booking can end up in leaving the only middle seat free in a row").

__Faq__: 
- Why the name "Sharpino"? 
    - It's a mix of "Sharp" (as the '#' of  C# or F#) and fino (Italian for "thin").  "sciarpino" (same pronunciation) in Italian means also "little scarf". 
- Why another event-sourcing library?
    - I wanted to study the subject and it ended up in a tiny little framework.
- Why F#?  
    - Any functional language of the ML family language in my opinion is a good fit for the following reasons:
        - Events are immutable, building the state of the context is a function of those events.
        - Discriminated Unions are suitable to represent events and commands.
        - The use of the lambda expression is a nice trick for the undoers (the _under_ is returned as a lambda that retrieves the context for applying the undo and returns another lambda that actually can "undo" the command).
        - It is a .net language, so you can use everything in the .net ecosystem (including C# libraries).
- How to use it
    - add the nuget package Sharpino to your project. 
    - note: on my side, when I added Sharpino as a library into a web app, then I had to add the following line to the web app project file to avoid a false error (the error was "A function labeled with the 'EntryPointAttribute' attribute must be the last declaration")
    ```xml
    <GenerateProgramFile>false</GenerateProgramFile>
    ```

## Useful info:
Examples 4 and 5 are using the SAFE stack. To run the tests use the common SAFE way (``dotnet run`` and ``dotnet run -- RunTests`` from their root dir )

## help wanted:
- Rewrite from scratch the Kafka integration making it work as is supposed to (i.e. Kafka "viewers" can be passed to application instances as in the examples)
- Adapt the examples to the new version of the library (2.0.0)
- Write more examples (porting examples from Equinox would be fine)

## News
- Version 2.5.4 added _runInitAndTwoAggregateCommands_ that creates a new aggregate snapshot and run two commands in a single transaction.
- Version 2.5.3 added _runSagaThreeNAggregateCommands_ this is needed when transaction cannot be simultaneous for instance when it needs to involve the same aggregate in multiple commands.
  (A short example will come but here is an idea, pretending the aggregate types can be two, and not three: A1, A2, A3, A3 needs to merge into An: I cannot run the "indpendent" saga-free version of running 
 multiple commands (pairs) because I should repeat the id of An many times which is invalid, so I run the saga version that executes the single "merge" i.e. merge A1 into An, then merge A2 into An etc...: if somethings goes wrong I have accuulted the "future undoers" that may rollback the eventually suffessful merges)

- A "porting" of an example from Equinox https://github.com/tonyx/sharpinoinvoices
- Version 2.5.2. add the runThreeNAggregateCommands (means being able to run simultaneusly n-ples of commands related to three different kind of aggregates)!
- Kafka status: No update. Use the only database version of the events and the "doNothing" broker for (not) publishing.
- Version 4.5.0 changed the signature of any command in user application. Commands  and AggregateCommands return also the new computed state and not only the related events. Example:
```fsharp
                | UpdateName name -> 
                    dish.UpdateName name
                    |> Result.map (fun x -> (x, [NameUpdated name]))

```
Any application needs a little rewrite in the command part (vim macros may be helpful).

In this way the commandhandler takes advantage of it to be able to memoize the state in the cache, so that virtually
the state will never be processed and at any state the cache will always be ready for the current state
(unless the system restarts, and in that case the state will be
taken by reading the last snapshot and processing the events from that point on).
 
- Version 2.4.2: Added a constraints that forbids using the same aggregate for multiple commands in the same transaction. The various version of RunMultiCommands are not ready to guarantee that they can always work in a consistent way when this happens.
- Disable Kafka on notification and subscribtion as well. Just use the "donothingbroker" until I go back on this and fix it.
This is a sample of the doNothingBroker: 
```fsharp
    let doNothingBroker =
        {
            notify = None
            notifyAggregate = None
        }

```
- Version 2.4.0: for aggregate commands use the AggregateCommand<..> interface instead of Aggregate<..>
The undoer has changed its signature.

Usually the way we run commands against multiple aggregate doesn't require undoer, however it may happen.
Plus: I am planning to use the undoer in the future for the proper user level undo/redo feature.

An example of the undoer for an aggregate is in the following module.


```fsharp
module CartCommands =
    type CartCommands =
    | AddGood of Guid * int
    | RemoveGood of Guid
        interface AggregateCommand<Cart, CartEvents> with
            member this.Execute (cart: Cart) =
                match this with
                | AddGood (goodRef, quantity) -> 
                    cart.AddGood (goodRef, quantity)
                    |> Result.map (fun s -> (s, [GoodAdded (goodRef, quantity)]))
                | RemoveGood goodRef ->
                    cart.RemoveGood goodRef
                    |> Result.map (fun s -> (s, [GoodRemoved goodRef]))
            member this.Undoer = 
                match this with
                | AddGood (goodRef, _) -> 
                    Some 
                        (fun (cart: Cart) (viewer: AggregateViewer<Cart>) ->
                            result {
                                let! (i, _) = viewer (cart.Id) 
                                return
                                    fun () ->
                                        result {
                                            let! (j, state) = viewer (cart.Id)
                                            let! isGreater = 
                                                (j >= i)
                                                |> Result.ofBool (sprintf "execution undo state '%d' must be after the undo command state '%d'" j i)
                                            let result =
                                                state.RemoveGood goodRef
                                                |> Result.map (fun _ -> [GoodRemoved goodRef])
                                            return! result
                                        }
                                }
                        )
                | RemoveGood goodRef ->
                    Some
                        (fun (cart: Cart) (viewer: AggregateViewer<Cart>) ->
                            result {
                                let! (i, state) = viewer (cart.Id) 
                                let! goodQuantity = state.GetGoodAndQuantity goodRef
                                return
                                    fun () ->
                                        result {
                                            let! (j, state) = viewer (cart.Id)
                                            let! isGreater = 
                                                // this check depends also on the number of events generated by the command (i.e. the j >= (i+1) if command generates 2 event)
                                                (j >= i)
                                                |> Result.ofBool (sprintf "execution undo state '%d' must be after the undo command state '%d'" j i)
                                            let result =
                                                state.AddGood (goodRef, goodQuantity)
                                                |> Result.map (fun _ -> [GoodAdded (goodRef, goodQuantity)])
                                            return! result
                                        }
                                }
                        )
 ```


- WARNING!!! Version 2.2.9 is DEPRECATED. Fixing it.
- Version 2.2.9: introduced timeout in connection with postgres as eventstore. Plus more error control. New parameter in sharpinoSeettings.json needed:
```json
{
    "LockType":{"Case":"Optimistic"},
    "RefreshTimeout": 100,
    "CacheAggregateSize": 100,
    "PgSqlJsonFormat":{"Case":"PlainText"},
    "MailBoxCommandProcessorsSize": 100,
    "EventStoreTimeout": 100
}
```
- Version 2.2.8: renamed the config from appSettings.json to sharpinoSettings.json. An example of the config file is as follows:
```json
{
    "LockType":{"Case":"Optimistic"},
    "RefreshTimeout": 100,
    "CacheAggregateSize": 100,
    "PgSqlJsonFormat":{"Case":"PlainText"},
    "MailBoxCommandProcessorsSize": 100
}
```

Example of line in your .fsproj or .csproj file:
```xml
  <ItemGroup>
    <None Include="sharpinoSettings.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

 
- Changes to the classic Blazor counter app to use Sharpino in the backend: https://github.com/tonyx/blazorCounterSharpino.git
- Version 2.2.6: runCommands work in threads for aggregates and context using mailboxprocessors for aggregates (the number of those active mailboxprocessors can be limited in config)
- Version 2.2.5: fix runCommand eventbroker notification.
- Version 2.2.4: some changes in runCommand: no need to pass state and aggregateViewer as it will just use the ones based on the eventstore (source of truth). Supporting also net7.0. The "core" gets rid of TailCall attribute not compatible with net7.0. 
There is the possibility that including Sharpino.Core must be explicitly included.
For an example of app that has been upagraded to the newest version of library see 
[shopping cart](https://github.com/tonyx/shoppingCartWithSharpino.git)

- Version 2.1.3: added local fork of [FsKafka](https://github.com/jet/FsKafka)  (with library dependencies updated) to be able to use it in the project.
- Version 2.1.0: going to remove newtonsoft, introduced FsPickler, FsKafka, changed kafka publisher way (binary and textencoding). Removed Kafkareceiver. Preparing to replace it with one based on FSKafka
- I am porting the examples to use the newer version (2.0.6). The porting of the first example(Sharpino.Sample) is incomplete (At the moment I disabled the "migrate between version" function in that example).
- version 2.0.7: a fix in runThreeCommand. CommandHandler will just use fresh data ignoring the viewer that has been passed. 

- version 2.0.6: 
    - eventstore checks the eventId of the state that produces any event before adding them. That will ensure that events are added in the right order and cannot fail (so the optimistic lock stateId will
    be superfluous and will be dropped). Serialization can be binary or JSON (see appSettings.json). The default is binary. The samples use Fspickler to serialize/deserialize events and snapshots. There is no use of ISerializer interface which was by default newtonsoft. Therefore the user needs to provide a serializer (pickler is ok for binary and for json as well). Pickler will not work with jsonb fields in Postgres as the indexes change their order in jsonb and pickler doesn't want it,  so they must be text.
    Kafka is still not working on the read part. The write part is ok even though any read test has been dropped for incompatibility and will be rewritten.
- version 2.0.3: changes from "now()" to utcNow() format in eventstores (Postgres and inMemory) and Sql template scripts.
- published version 2.0.0 supporting binary serialization for events and snapshots on Postgres.
Note: the current examples provided are still referencing the previous 1.6.6 version. 
[Here is an example compatible with 2.0.0. with binary serialization](https://github.com/tonyx/shoppingCartWithSharpino.git)

- added some videos (in Italian) about the library and the sample applications.
https://youtu.be/OQKD5uluFPc
https://youtu.be/ToZ_I_xRA-g
https://youtu.be/WtGEQqznPnQ
https://youtu.be/j2XoLkCt31c



- added a few new examples (can be used for dojos)
[pub system](https://github.com/tonyx/sharpinoDojoPubSystem)
- version 1.6.6: can use plain text instead of JSON data type for database (see scripts in SqlTemplate dir). The appSettings has a new settings for it:
```json
{
    "LockType":{"Case":"Optimistic"},
    "RefreshTimeout": 100,
    "CacheAggregateSize": 100,
    "PgSqlJsonFormat":{"Case":"PlainText"},
    "MailBoxCommandProcessorsSize": 100
}
```

The other option is:
```
    "PgSqlJsonFormat":{"Case":"PgJson"}
```    
the tables should be coherent: use PlainText when json fields are of type text and PgJson when they are of type json or jsonb.
Why bother? In this example I use FsPickler to serialize/deserialize https://github.com/tonyx/shoppingCartWithSharpino
It won't work with jsonb fields because it needs the same order of fields in the json string whereas json/jsonb fields are stored in a way that doesn't preserve the order of fields.

- version 1.6.0: starting removing kafka for aggregates (will be replaced somehow). Use eventstore (postgres) based state viewers instead.
New sample: started an example of Restaurant/Pub management. (Sample 6) 
- Version 1.5.8: fix in adding events with stateId when adding more events (only the first stateId matters in adding many events, so the rest are new generated on the fly)
- Version 1.5.7: 
- Added _runInitAndCommand_ that creates a new aggregate and a command context in a single transaction.
- Changed the signature of runAggregate and runNAggregate (simplified the viewer passed as parameter avoiding a labmda)
- Version 1.5.5:
- fixed a key problem in dictionary keys in memory based eventstore (MemoryStorage). Note it is supposed to be used only for dev and testing.
- _WARNING_: Kafka publishing is ok but __Kafka client integration needs heavy refactoring and fixing, particularly about aggregate viewer__.T That means that any program that tries to build the state using  _KafkaStateViewer_ may have some inefficiencies of even errors.
Just use the storage base state viewers for now (or build your own state viewers by subscribing to the Kafka topic and building the state locally).
I am ready to refactor now because I have an elmish sample app for (manual) testing. An example of hot try it is by taking a look in the src/Server/server.fs in Sample4 (commented code with different ways to instantiate the "bookingsystem" sample app)
- Added SqlTempate dir with template examples for creating table relate to events and snapshots for aggregates and contexts.
- Addes sample4 witch is almost the same as sample3 but using SAFE stack (Fable/Elmish) as envelope (going to ditch sample3 because 
it is going to be messy)
- Version 1.5.4 Replaces version 1.5.3 (that was deprecated )
    - Aggregate snapshots added (in addition to contexts snapshots)

- Version 1.5.3 (don't use it: it is deprecated: missing packages)
 
- Version 1.5.1: - 
  - Added a new configuration file named appSettings.json in the root of the project with the following content:
```json
  {
      "LockType":{"Case":"Optimistic"},
      "RefreshTimeout": 100,
      "CacheAggregateSize": 100
  }
```
- Moreover, the specific variant of optimitic lock (classic or more permissive) can be changed by code (evenstore method).
The "more permissive" optmistic lock will skip aggregate state version control so it allows that events generated in the same moment can be stored and published. Nevertheless if they end up in a violation of the invariant rule the core will skip them anyway.
__The more permissive optimistic lock cannot ensure that multiple aggregate transactions are handled properly__: if I book seats from multiple rows it is theoretically possible that only seats from one row are booked.

- See the sql script of sample 4: it includes the steps related to change "on the fly" (by application) the db level constraints that inhibit adding two events with the same aggregate stateid.
- (in the same stream) in the event store.
- Current version 1.5.0: - Kafka integration for fine-grained aggregates (identified by Id) is included. 
- Version 1.4.8: streams of events can relate to proper aggregate identified by id (and not only context). I can run commands for an arbitrary number of aggregates of some specific type. See Sample4 (booking seats of rows where rows are aggregates of a context which is a stadium). Integration with Kafka for those "fine" aggregates identified by Id is not included in this version.

- Booking seat example: https://github.com/tonyx/seatsLockWithSharpinoExample (it shows some scalability issues that will be fixed in future releases)
- Version 1.4.7: contains sample app that builds state of contexts by using Kafka subscriber (receives and processes events to build locally the state of those contexts, despite can still access the "souce of truth", which is the db/event store, when something goes wrong in processing its state, i.e. out of sync events).
- Version 1.4.6: fix bug in Postgres AddEvents 
- Version 1.4.5: Upgrade to net8.0
- Info: in adding new features I may risk breaking backward compatibility. At the moment I am handling in this simple way: if I change the signature of a function I add a new function with the new signature and I deprecate the old one. I will keep the old one for a while. I will remove it only if I am sure that nobody is using it.
- Version: 1.4.4 Postgres tables of events need a new column: kafkaoffset of type BigInt. It is used to store the offset/position of the event in the Kafka topic.
See the new four last alter_ Db script in Sharpino.Sample app. This feature is __Not backward compatible__: You need your equivalent script to update the tables of your stream of events.
(Error handling can be improved in writing/reading Kafka event info there).
Those data will be used in the future to feed the "kafkaViewer" on initialization. _Note_: kafkaoffset/kafkatopic fields on db in future versions will be unused.


- From Version 1.4.1 CommandHandler changed: runCommand requires a further parameter: todoViewer of type (stateViewer: unit -> Result<EventId * 'A, string>). It can be obtained by the CommandHandler module itself.getStorageStateViewera<'A, 'E> (for database event-store based state viewer.)
- [new blog post](https://medium.com/@tonyx1/a-little-f-event-sourcing-library-part-ii-84e0130752f3)
- Version 1.4.1: little change in Kafka consumer. Can use DeliveryResults to optimize tests. Note: Kafka consumer is still in progress.
- Version 1.4.0: runCommand instead of Result<unit, string> returns, under result, info about event-store created IDs (Postgres based) of new events and eventually Kafka Delivery result (if Kafka is configured). 
- Version 1.3.9: Repository interface changed (using Result type when it is needed). Note: the new Repository interface (and implementation) is __not compatible__ with the one introduced in Version 1.3.8!
 
- Version 1.3.8: can use a new Repository type instead of lists (even though they are still implemented as plain lists at the moment) to handle collections of entities. Note repository is only an interface with only a plain list implementation.
- Version 1.3.5: the library is split into two nuget packages: Sharpino.Core and Sharpino.Lib. the Sharpino.Core can be included in a Shared project in the Fable Remoting style. The collections of the entities used in the Sharpino.Sample are not lists anymore but use Repository data type (which at the moment uses plain lists anyway). 

- Version 1.3.4 there is the possibility to choose a pessimistic lock (or not) in command processing. Needed a configuration file named appSettings.json in the root of the project with the following content:
 __don't use this because the configuration is changed in version 1.5.1__

- this entry is ignored as the lock is always optimistic.
```json

    "SharpinoConfig": {
        "PessimisticLock": false // or true
    }

``` 

More documentation (a little bit  out of date. Will fix it soon) [(Sharpino gitbook)](https://tonyx.github.io)

<a href="https://www.buymeacoffee.com/Now7pmK92m" target="_blank"><img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Buy Me A Coffee" style="height: 60px !important;width: 217px !important;" ></a>

