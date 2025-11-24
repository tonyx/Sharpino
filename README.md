# Sharpino


<img src="ico/sharpino.png" alt="drawing" width="50"/>


## A little F# Event Sourcing Library

[![NuGet version (Sharpino)](https://img.shields.io/nuget/v/Sharpino.svg?style=flat-square)](https://www.nuget.org/packages/Sharpino/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## What is Event Sourcing?
- Event sourcing is a design pattern for persisting the state of an object by storing the sequence of events that have occurred on the object.
- Event sourcing fits the functional paradigm as the state is defined by an evolve function that is a pure function of the initial state and the events.

## What is Sharpino?

Sharpino is a library to support Event-Sourcing in F# based on the following principles:
- PostgresSQL-based event store to register events and snapshots.
- In-memory event store to speed up the tests.
- Optimistic lock based on event_id: checking the first available event_id position on the basis of the event_id passed by the command handler to the event store.
- Multiple streams transactions: executing multiple commands involving different aggregates as single db transactions.
- StateViewer: A non-pure function to get the current state of any aggregate or context. StateViewers probe the cache and, in case of a cache miss, look into the event store to apply the "evolve" on the latest snapshot and subsequent events.
- HistoryStateViewer: The same as the StateViewer, including also the state of an object that was softly deleted.
- Soft delete: Mark an aggregate as deleted.
- GDPR: Overwrite/clear/reset snapshots and events in case a user asks to delete their data.
- Fire events to a message queue (RabbitMQ).
- Listeners for messages will provide StateViewers as they interpted and process those messages
- Messages are fired after the events are stored.
- Messages can be of type InitialState, Events, Deletion

## Goals
- Using F# in idiomatic way for domain modelling and event sourcing in a .NET solution, particularly in the backend.
- Multilanguage environment and architecture (a template using Blazor with C# on front end and F# on backend is given).
- Avoid impedance mismatch between the domain and the database, so objects don't need to know about the database column mapping.

## Overview and terms

- Contexts: Event-sourced objects with no Id, so only one instance is around for each type.
- Aggregates: Event-sourced objects with Id (Guid).
- Multiple streams transactions: executing multiple commands involving different aggregates as single db transactions.
- Transformation members of any object of type 'A use this signature: 'A -> Result<'A, string>'.
- Events are based on D.U. and are wrappers to transformation events, i.e. processing events means calling the transformation members.
- Commands are also based on D.U. and generate lists of events and, optionally, "unders" that will return a function to produce a list of compensating events (in the future).
- Cache: Dictionary-based cache of the current state of any aggregate or context.
- Soft delete: Mark an aggregate as deleted.
- StateViewer: A non-pure function to get the current state of any aggregate or context. StateViewers probe the cache and, in case of a cache miss, look into the event store to apply the "evolve" on the latest snapshot and subsequent events.
- HistoryStateViewer: The same as the StateViewer, including also the state of an object that was softly deleted.
- GDPR: Overwrite/clear/reset snapshots and events in case a user asks to delete their data.
- EventStore is based on PostgreSQL to store events and snapshots.
- SQLTemplates: scripts to create tables for events and snapshots for any aggregate/context and format (bytea or text/JSON).
- Optimistic lock based on event_id: Checking the available position to store new events on the basis of the event_id used to execute the command and passed by the command handler to the event store (if matching fails then no events are stored).
- In-memory event store: an in-memory cache of events and snapshots that can be used to speed up the tests.
- JSON or binary serialization for events and snapshots. The serialization mechanism is up to the user. The examples included use FsPickler to serialize/deserialize events and snapshots in binary or JSON. The JSON fields are plain text fields on the DB. They could be JSON or JSONB fields (with no significant advantages - and a little overhead - as there are no query of the content of JSON fields).
- Evolving/refactoring aggregates by keeping backward snapshot read compatibility with upcasting.
- Commands and events avoid versioning or upcasting. Just adding new events is the practice to extend the functionality related to events.
- By default, the "evolve" function skips events that may produce an invalid state. There is an alternative evolve function that can't skip events that may produce invalid states.
- In regard to the previous point: Because of the optimistic lock, the Event store should __never__ store events that produce an invalid state (and if it happens it means that the optimistic lock failed, which is practically unlikely).
- Creation of any aggregate is based on generating an initial snapshot. Deletion is based on generating a new snapshot with the deleted field set to true and on the invalidation of the related cache entry.
  There may also be events associated with the creation and deletion of aggregates, but they are not needed.
- Contexts don't need creation nor deletion. They declare an initial state by a static Zero member.
- MessageSenders: can be set to NoSender or to a MessageSender that, given the name of a stream, returns a ValueTask that can be used to send messages to a message bus: examples with RabbitMQ are provided.

### Issues
- A [temporary list of issues](https://github.com/tonyx/Sharpino/issues)
## Projects
__Sharpino.Lib.Core__:

- [Core.fs](Sharpino.Lib.Core/Core.fs): Abstract definition of _Events_, _Commands_ and _Undoer_ (or compensator), definition of "evolve" function for aggregates and contexts to compute the next state given the current state and a list of event.

__Sharpino.Lib__:

- [CommandHandler.fs](Sharpino.Lib/CommandHandler.fs): Gets and stores snapshots, executes commands, and produces and stores events passing them to the __event store__.
- [PgEventStore.fs](Sharpino.Lib/PgEventStore.fs) and [MemoryStorage.fs](Sharpino.Lib/MemoryStorage.fs): Manages persistence of events in Postgres or in-memory respectively using string encoding (usually JSON)
- [PgBinaryEventStore.fs](Sharpino.Lib/PgBinaryEventStore.fs): Manages persistence of events in Postgres using binary encoding (examples are based on Fspickler external lib)
- [Cache.fs](Sharpino.Lib/Cache.fs). Caches the current state of contexts or aggregates.


## How to use it
- Sharpino samples provide dbmate template files to set up the Postgres database for the event store.
- A proper .env file must be set up with the DATABASE_URL environment variable to connect to the Postgres database with a connection string.
- If the db is properly set then you can run all the samples by:
```bash
runTests.sh
```
- Equivalent .bat windows script would be trival to implement from the .sh
- You can run single examples by locating the project (or test project) of any example and running it having set the .env file in the root of the project.
- Note: in this way you need to make sure the postgres based event store is up and running.
- Otherwise, you may have to dig into the examples and exclude any postgres based examples (by commenting out at most one line in the test files) allowing in memory eventstore test only.
- Some examples can use the combination of postgres and rabbitmq by the following command (in the proper subproject directory)
- Check the .fsproj file of the project to see if it uses rabbitmq or not.
```bash
    dotnet run --configuration:rabbitmq
```

- Rabbitmq must be up and running to run the examples that use it.
- Rabbitmq can be launched in a different window by using the following command:
```bash
    rabbitmq-server
```

 
## How to contribute
Please read the [CONTRIBUTING.md](CONTRIBUTING.md) for all the information about how to contribute to the project.

Or you can run the test in the single directories.

__Faq__ and __trivia__:
- Why the name "Sharpino"?
    - It's a mix of "Sharp" (as the '#' of  C# or F#) and fino (Italian for "thin").  "sciarpino" (same pronunciation) in Italian means also "little scarf".
- Why another event-sourcing library?
    - I wanted to study the topic, I also use it in a project for fun and hopefully profit (not listed: ask me for reference in case).
- Why F#?
    - Any functional language of the ML family language in my opinion is a good fit for event-sourcing for the following reasons:
        - Events are immutable, building the state of the context is a function of those events.
        - Discriminated Unions are suitable to represent events and commands.
        - The use of the lambda expression is a nice trick for the undoers (compensator events, former "Saga-like").
        - It is a .net language, so you can use everything in the .net ecosystem (including C# libraries).
- How to use it
    - add the nuget package Sharpino to your project.
    - note: if you gets in a setup errors like  "A function labeled with the 'EntryPointAttribute' attribute must be the last declaration" then you may fix by adding this line in the .fsproj file:
    ```xml
    <GenerateProgramFile>false</GenerateProgramFile>
    ```
- How does it compare with other functional event-sourcing libraries?
    - I can't be exaustive, but few comparisons in basic examples are the following respect to the [Equinox](https://github.com/jet/equinox) library.
        - [Counter in Sharpino](https://github.com/tonyx/SharpinoCounter3)
        - [Counter in Equinox](https://github.com/jet/equinox/blob/master/samples/Tutorial/Counter.fsx)
        - [Invoices in Sharpino](https://github.com/tonyx/sharpinoinvoices)
        - [Invoices in Equinox](https://github.com/nordfjord/minimal-equinox/tree/main)

      ```
- Is there any template for a "full stack" walking skeleton?
    - For a walking skeleton using C# on the front end (via blazor):  [Sharpino.Blazor.Template](https://github.com/tonyx/sharpinoBlazor)
    - The numbered examples provided in the library can be used as a starting point. I'd suggest to use the latest ones.
- How to handle "projections"?
    - It is possible to use "details" as a way to handle the composition of information coming from aggregates and events. The examples provided prefer to use directly the aggregates state (which is efficient by using the cache) s state (which is efficient by using the cache).
    - Events can be queried directly to retrieve any projections (i.e. "ephemeral views"), however the user may need to do some work to be efficient.
    - More complex projections (i.e. "materialized views") may need some work to be efficient in the same way the aggregates are.
- Why caring about cross streams transactions and cross aggregates invariants (instead of just ruling them out)?
    - New business rules may imply new invariants that can escape the constraints of the current structure of aggregates anyway. 

## Acknowledgements

A heartfelt thank you to  [Jetbrains](https://www.jetbrains.com) who have generously provided free licenses to support this project.

## Upcasting techniques.

In this section, I will describe the upcasting techniques that any application may use to allow read snapshots in old format.
Goal: using upcast techniques to be[StateView.fs](Sharpino.Lib/StateView.fs) able to read the old (serialized) version of typeX into a new version of it.
1. The following premise must be true: If you clone any TypeX into a new one with only a different name (example: TypeX001), then your serialization technique/library must be able to read any stored serialized instance of typeX and get the equivalent instance of TypeX001, so it will be able to indifferently have TypeX and TypeX001 as the target for deserialization (some libraries may allow this out of the box, some other may need some extra config/tuning and/or specific converters).
2. Now you can make some changes to TypeX that make it different from the old TypeX/TypeX001 (example: add new property) making sure that there exists a proper logical conversion (or better: "isomorphic immersion" if you like algebraic terms) from the old TypeX (i.e. TypeX001) into the new TypeX.
3. Define the Upcast function/instance member form TypeX001 that actually implements that conversion from an instance of the old typeX to an instance of the new typeX.
4. Define a "fallback" action in the deserialization related to the new TypeX so that it can, in case of failure because of encountering an old TypeX/TypeX001, apply the deserialization obtaining a typeX001 instance and use its Upcastor to get, finally, the equivalent instance of TypeX.

- Now you can deploy the new version and in case the code tries to read an old TypeX/TypeX001 it must be able to correctly interpret it as the new TypeX by adopting the following steps in deserialization of the existing snapshot:
- try to read and deserialize it as TypeX
- if Ok then Ok
- if it fails try to read it as TypeX001 and then upcast to TypeX

5. If it is not expensive, transform any snapshot of old typeX/TypeX001 into the new TypeX in one shot: make a massive upfront aggregate upcast and re-snapshot: retrieve all the existing current state of aggregates of old TypeX/TypeX001 (that will do upcast under the neath) and generate and store snapshots for all of them so that those snapshot will surely respect the format of the new TypeX. After doing it, assume that the fallback action of reading old versions and then upcasting will never be necessary again and that part of the code can be simply deleted from TypeX.
6. If you decided not to do the previous step 5 or if there is the possibility that you'll need to downgrade the new TypeX again to the previous TypeX001 (which would mean creating a "downcastor" making essentially the reverse of the Upcast process described), then keep the older typeX (or TypeX001) for a while so you will still be able to upcast "on the fly" any older typeX and you will also are prepared to eventually downgrade/downcast again. Note that keeping the TypeX001 around for a long time means that a further upgrade may complicate things as you may have to go deeper in having more older versions in the form of TypeX002, with a more complicated and error-prone recursive chain of fallback/upcast among older versions. So rather you will prefer to doing the full step 7 to make sure that the upgrade will affect all the snapshots.
7. Last but not least. Having events that depend strictly on the old type X format could be a problem because you don't know if that may imply the necessity to change/upcast also the events, or just test the hypothesis that events based on typeX (say Event.Update (x: Type/X)) can be correctly parsed if TypeX changes. If not, then just don't use TypeX as an argument for whatever event.

## News/Updates
Note/reminder/warning: in sharpinoSettings.json the PgSqlJsonFormat should be PlainText, and the fields containing serialized data (snapshots and events) must be text.  
Other configuration, using PgJson for instance and JSON or JSONB fields and different serializer than fsPickler, are ok as long as you test carefully by doing low level operations on the eventstore e.g. store and retrieve events and snapshot bypassing the command handler and the cache.
The reason is that the cache will avoid the re-read and deserialize on db, and that means that if it fails then you may not realize it (not immediately) and even in many tests.
However: postgres JSON types are not necessary and will probably cause an overhead as the db will try to parse them, whereas text fields are not parsed at all.

- Version 4.5.0: First step of introducing more async task based methods (with optional cancellationToken) on EventStore
- Version 4.4.9: Replaced ConcurrentDictionary based cache with MemoryCache
- Version 4.4.7: fix a problem of indexes in aggregateCache that was unoticed and harmless (until 4.4.6).
- Version 4.4.6 (DEPRECATED. need fix): avoid an unnecessary access to last snapshot event id to get last aggregateSnapshot
- Added an article on mediumi[F# Domain Model with Event Sourcing vs C# with Entity Framework] (https://medium.com/@tonyx1/f-domain-model-with-event-sourcing-vs-c-with-entity-framework-ff870ce5c48c)
- Version 4.4.4: added bulk object initializations
- Version 4.4.3: added support for net10.0
- Version 4.4.2: mkAggregateSnapshot is reintroduced (was dropped in 4.4.1)
- Version 4.4.1: fixed [Avoid db call to get lastEventId before probing the cache](https://github.com/tonyx/Sharpino/issues/45). In the getAggregateFreshState the lastEventId is computed within the events involved in the "evolve" from last snapshot and not in a second step.
- Version 4.4.0: added MessageSenders (replacing partially the old IEventBroker) to send events to a message bus after they have been stored. Some Rabbitmq examples are provided. (warning. There is no backward compatibility as the MessageSenders replaces IEventBroker)
- Version 4.3.4: added more info in some error messages
- Version 4.3.3: fix md parameter
- Version 4.3.2: updated dependencies, fixed date error in pgBinaryEventStore
- Version 4.3.1: reintroduced concurrent dictionary aggregate cache
- Version 4.3.0: Aggregate Cache is type independent. Added a way to "preExecute" any type of commands and then send them to the command handler. So now executing an arbitrary number of command of any type is allowed (see example 10). Note: some adjustments in passing metadata to "delete" commands will make them non-backward compatible (just add metadata to the command to fix).
- Version 4.2.3: concurrent dictionary aggregates cache
- Version 4.2.1: added a variant of delete with aggregateCommand
- Version 4.2.0: fixed again the delete's (tested only on an external application not included in the examples, sorry)
- Version 4.1.8: some fixes on new features
- Version 4.1.7: added an alternative to getAggregateFreshStater (getHistoryAggregateFreshState) that includes historical (i.e. deleted) aggregate and skip caching. No example or test provided (hack).
- Version 4.1.6: added runDelete with aggregateCommand (see sample 9 for a use case)
- Version 4.1.5: fixed dependencies declared in manifest/nuspec file
- Version 4.1.4: added soft delete with predicate (usually predicate is: counter references must be zero). Needs at applicative level increment counter each time a reference is created and decrement it when the reference is removed (see sample 9). Warning: deletion is not an event! Is just a state of the latest snapshot of an aggregate. After carefully evaluated the pros and cons I decided in this way (hint: any independent stream evolving does not care if the reference of an external id does actually exist or not. Getting the state of any aggregate depends primarily on the latest snapshot. If that last snapshot is deleted then it is as if it doesn't exist anymore as long as also the caches is aware of this deletion i.e. it is invalidated).
- an example of integration with Blazor: https://github.com/tonyx/sharpinoBlazor (a summary, in Italian, made by Gemini A. I.: https://g.co/gemini/share/528e98bd6dd8)
- Version 4.1.3: fixed some SQL issues of new functions introduced in 4.1.1/4.1.2 (involving only new stuff)
- Version 4.1.2: deprecated getFilteredAggregateStatesInATimeInterval, added getFilteredAggregateStatesInATimeInterval2, getAggregateStatesInATimeInterval, getAllAggregateStates
- Version 4.1.1: event store:added GetAggregateIds and GetAggregateIdsInATimeInterval to event
- Version 4.1.0: removed the saga-ish runCommands (as the "forceRun" equivalent versions of runCommands are enough)
- Version 4.0.2: introduces Stateview.getFilteredAggregateStatesInATimeInterval
- Version 4.0.0: same as 3.10.6, just restarting numeration.
- Version 3.10.6: added runInitAndNAggregateCommandsMd on command handler (accepts an initial state of a new aggregate of a certain type and N aggregate commands related to a different type type providing _distinct_ aggregateIds) - it is has been tested on a private application. Feel free to add tests on examples (Sample 8 may be a good fit for it).
- Version 3.10.5: skip the mailboxprocessor in running commands. Removing duplicate code in Pg based eventstore implementations. Fixed a bug of runThreeNAggregregateCommands in handling indexes (will take a closer look for the next release).
- Version 3.10.3: in some cases forceRunTwo/ThreeNAggregateCommands skip caching.
- Current version 3.10.2: added runInit that just create initial instance of an aggregate. I will use it to substitute runInitAndCommand to avoid "expansion" of the aggregate state in the cache. The use of MailboxProcesor for commands is based on a compile time constant as will be removed in the future.
- (instead of stream level lock which ).
- Added Example 8 related to the [transport-tycoon domain](https://github.com/trustbit/exercises/blob/master/transport-tycoon-1.md). It is a simple example of a transport company that manages vehicles and routes.
- blogged [Sharpino Internals. Inside a functional event-sourcing-library, part 4](https://medium.com/@tonyx1/sharpino-internals-inside-a-functional-event-sourcing-library-part-4-284a9fe6372a)
- Added Sharpino.Sample.7 which shows two equals solutions based on JSON and BINARY serialization respectively.
- blogged [Sharpino Internals. Inside a functional event-sourcing-library, part 3](https://medium.com/@tonyx1/sharpino-internals-inside-a-functional-event-sourcing-library-part-3-c4a9edc81467)
- Current version 3.0.9
- Version 3.0.6: forceRunThreeNAggregateCommands has been improved (aggregates involved in more than one command uses a state that is the result of the previous command in the same transaction)
- Version 3.0.5: forceRunNAggregateCommands has been improved (aggregates involved in more than one command uses a state that is the result of the previous command in the same transaction)
- Version 3.0.4: forceRunTwoNAggregateCommands has been improved (aggregates involved in more than one command uses a state that is the result of the previous command in the same transaction)
- Version 3.0.3: Fixed bug in multiple events writing in Postgres eventstore (a local branch of an incoming feature can reproduce this bub)
- Version 3.0.2: StateCache substituted by StateCache2. Access cached contexts don't need checking lastEventId to the eventstore for comparison.
- Version 3.0.0: some cache improvements, supporting net9.0 and net8.0 (ditched net7.0)
- Version 2.7.7: The md field is mandatory for any event table: Any run(Aggregate)Commands are redirecting to the equivalent 'run(Aggregate)CommandsMd' that accepts metadata as a string.
- Version 2.7.5: Can view history of events related to a set of aggregates in a time interval (StateView.getFilteredMultipleAggregateEventsInATimeInterval)
- Version 2.7.4: A (quick)fix allows adding compensating events in pgEventstore in saga-ish that were rejected because of strict eventId control
- Live example is here: [restaurant management system](https://orderssystem.azurewebsites.net) tech stack: Blazor as Front end, Sharpino as backend, Postgres as event store, Azure as hosting.

- Version 2.7.2: Support metadata field in db (any command has the correspondent commandMd that accepts any string as metadata). Those metadata can be used for debugging purposes. To use them any event table in the db needs a new nullable text field called "md".
  New db functions are also needed. See the functions like "insert_md{Version}{AggregateStorageName}_events_and_return_id" in the sql scripts in the SqlTemplate dir doing a proper substitution in {Version} and {Format} and {AggregateStorageName}. Similar function is in the ContextTemplate.sql.
- Version 2.7.1: Bug fix
- Version 2.7.0: Fix bug in Saga-ish multi-command and added some tests for it.
- Version 2.6.8: Remove EventStoreDb and starting removing Kafka (for future rewrite or replacement).
- Version 2.6.7: Optimize snapshotting by using the in-memory cached value to avoid multiple reads of the same aggregate.
- Version 2.6.6: Can create new snapshots for aggregates that have no events yet (can happen when you want to do massive upcast/snapshot for any aggregate)
- Version 2.6.4: the mkAggregateSnapshots and mkSnapshots are now public in commandhandler so that they can be used in the user application to create snapshots of the aggregates and contexts. This is userful after an aggregate refactoring update so that any application can do upcast of all aggregates and then store them as snapshots (and then foreget about the need to keep upcast logic active i.e. can get rid of any older version upcast chain).
- Version 2.6.3: Stateview added  ```getFilteredAggregateSnapshotsInATimeInterval```which returns a list of snapshots of a specific type/version of aggregate in a time interval filtered by some criteria no matter if any context contains references to those aggregates, so you can retrieve aggregates even if no context has references to them (for instance "inactive users").
- Version 2.6.2: CommandHandler, PgEventStore and PgBinaryEventstore expose as setLogger (newLogger: ILogger) based on the ILogger interface replacing Log4net. You can then pass that value after retrieving it from the DI container (straightforward in a .net core/asp.net app).
- Version 2.6.0: Added a function for the GDPR in command handler able to virtuallty delete snapshots and events, i.e. replace any event with an events that returns an empty version of the state and also replace any snapshot with the voided/empty version of that state (and also fill the cache with that empty value).
- Version 2.5.9: Added the possibility via StateView to retrieve the initial state/initial snapshot of any aggregate to allow retrieving the data that the users claims. So when users unsubscribe to any app then they have the rights to get any data. This is possibile by getting the initial states and any following event. I think it will be ok to give the user a json of the  initial snapshots and any events via an anonymous record and then let the use download that JSON.
- Version 2.5.8: Added query for aggregate events in a time interval. StateView/Readmodel can use it passing a predicate to filter events (example: Query all the payment events). Aggregate should not keep those list ob objects to avoid unlimited grow.
- A short pdf: [why do we need upcastors for aggregates and not for events](https://drive.google.com/file/d/1DKx8IXqakc14qjQbrymzwHAJQEbhxZGq/view?usp=share_link) (sorry for typos)
- Blog post: [Upcasting aggregates in Sharpino](https://medium.com/@tonyx1/upcast-to-read-aggregates-in-an-older-format-in-a-sharpino-based-solution-839b807265f9)
- Blog post: comparing the example of the "Counter" app in Equnox and in Sharpino https://medium.com/@tonyx1/equinox-vs-sharpino-comparing-the-counter-example-0e2bd6e9bbf2
- Version 2.5.7 added mixtures of saga-like multi-commands and saga-less multi-aggregate commands (not ideal at all, but useful for some use cases that I found that I will describe later, I hope)
- Blogged [About Sharpino. An Event Shourcing library](https://medium.com/@tonyx1/about-sharpino-an-f-event-sourcing-library-dbadb4282ab9)
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
Note: I try to avoid "undoer" that are meant to issue compensating events when two transactions are running in parallel and one fails
and for any reason we decided to not use the cross aggregate transaction function in command handlers.
The "undoers" are complex but still: they are in general not necessary (or if you want to suggest a way to simplify them it's fine).

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
Basically you may wan to write json fields into text fields  for various reasons
(on my side I experienced that an external library may require further tuning to properly work with jsonb fields in Postgres, so in that case a quick fix is just using text fields).
Remember that we don't necessarily need Json fields as at the moment we just do serialize/deserialize and not querying on the json fields (at the moment).

_old stuff deleted_

More documentation [(Sharpino gitbook)](https://tonyx.github.io)

<a href="https://www.buymeacoffee.com/Now7pmK92m" target="_blank"><img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Buy Me A Coffee" style="height: 60px !important;width: 217px !important;" ></a>

