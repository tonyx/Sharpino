# Sharpino


<img src="ico/sharpino.png" alt="drawing" width="50"/>


## A little F# event-sourcing library

[![NuGet version (Sharpino)](https://img.shields.io/nuget/v/Sharpino.svg?style=flat-square)](https://www.nuget.org/packages/Sharpino/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## What is it?

Support for Event-sourcing in F#.
No sensible data (GDPR) support.

## Features
- Support in memory and Postgres storage. Support Eventstoredb (only for the LightCommandHandler).
- Support publishing events to Kafka.
- Example application with tests including Kafka subscriber.
- Contexts represent sets of collections of entities (e.g. a collection of todos, a collection of tags, a collection of categories, etc.) associated with events
- A specific practice to refactor context and test context refactoring


## Projects


__Sharpino.Lib.Core__:

- [Core.fs](Sharpino.Lib.Core/Core.fs): Abstract definition of _Events_, _Commands_ and _Undoer_ (the reverse of a command to be used if storage lacks transaction between streams). Definition of _EvolveUnForgivingErrors_ and "normal" _Evolve_. The former raises an error if there are some events that cannot be applied to the current state of the context. The latter just skip those events.

__Sharpino.Lib__:

- [CommandHandler.fs](Sharpino.Lib/CommandHandler.fs): gets and stores snapshots, execute commands, and produces and store events using the __event store__.
- [LightCommandHandler.fs](Sharpino.Lib/LightCommandHandler.fs): gets and stores snapshots, execute commands, produces and store events using an event store that supports pub/sub model ( Eventstoredb ).
- [DbStorage.fs](Sharpino.Lib/PgEventStore.fs) and [MemoryStorage.fs](Sharpino.Lib/MemoryStorage.fs): Manages persistency in Postgres or in-memory. 
- [Cache.fs](Sharpino.Lib/Cache.fs). Cache current state.


__Sharpino.Sample__
You need a user called 'safe' with password 'safe' in your Postgres (if you want to use Postgres as Eventstore).

It is an example of a library for managing todos with tags and categories. There are two versions in the sense of two different configurations concerning the distribution of the models (collection of entities) between the contexts. There is a strategy to test the migration between versions (contexts refactoring) that is described in the code (See: [AppVersions.fs](Sharpino.Sample/AppVersions.fs) and [MultiVersionsTests.fs](Sharpino.Sample.Test/MultiVersionsTests.fs) )
.

-  __contexts__ (e.g. [TodosContext](Sharpino.Sample/Domain/Todos/Context.fs)) controls a partition of the collection of the entities and provide members to handle them. 

- __contexts__ members have corresponding __events__ ([e.g. TagsEvents](Sharpino.Sample/clusters/Tags/Events.fs)) that are Discriminated Unions cases. Event types implement the [Process](Sharpino.Lib/Core.fs) interface. 

- __contexts__ are related to __Commands__ (e.g. [TagCommand](Sharpino.Sample/clusters/Tags/Commands.fs)) that are Discriminated Unions cases that can return lists of events by implementing the [Executable](Sharpino.Lib/Core.fs) interface.

__Commands__ defines also _undoers_ are functions that can undo the commands to reverse action in a multiple-stream operation for storage that doesn't support multiple-stream transactions (see _LightCommandHandler_).
- A [Storage](Sharpino.Lib/DbStorage.fs) stores and retrieves _events_ and _snapshots_.
- The [__api layer__ functions](Sharpino.Sample/App.fs) provide business logic involving one or more clusters by accessing their state, and by building one or more commands and sending them to the __CommandHandler__.
- An example of how to handle multiple versions of the application to help refactoring and migration between different versions: [application versions](Sharpino.Sample/AppVersions.fs). 

__Sharpino.Sample.tests__
- tests for the sample application

__Sharpino.Sample.Kafka__
- scripts to setup a Kafka topics corresponding to the contexts of the sample application

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

## Tests on eventstoredb
If eventstore is running on docker you may want to run tests on it as follows:
by making the eventstore tests not pending (search for "eventstore tests" and change ptestList to ftestList)
or by uncommenting the following line on the file [MultiversionsTests.fs](Sharpino.Sample.Test/MultiversionsTests.fs):
```Fsharp
        // (AppVersions.evSApp,                    AppVersions.evSApp,                 fun () -> () |> Result.Ok)
```

__Faq__: 
- Why "Sharpino"? 
    - It's a mix of Sharp and fino (Italian for "thin").  "sciarpino" (same pronunciation) in Italian means also "little scarf". 
- Why another event-sourcing library?
    - I wanted to study the subject and it ended up in a tiny little framework.
- Why F#?  
    - Any functional language from the ML family language in my opinion is a good fit for the following reasons:
        - Events are immutable, building the state of the context is a function of those events.
        - Discriminated Unions are suitable to represent events and commands.
        - The use of the lambda expression is a nice trick for the undoers (the _under_ is returned as a lambda that retrieves the context for applying the undo and returns another lambda that actually can "undo" the command).
        - It is a .net language, so you can use everything in the .net ecosystem (including C# libraries).
- How to use it
    - add the nuget package Sharpino to your project (current version 1.4.1)
    - note: on my side, when I added Sharpino as a library into a web app, then I had to add the following line to the web app project file to avoid a false error (the error was "A function labeled with the 'EntryPointAttribute' attribute must be the last declaration")
    ```xml
    <GenerateProgramFile>false</GenerateProgramFile>
    ```
## News: 
- From Version 1.4.1 CommandHandler changed: runCommand requires a further parameter: todoViewer of type (stateViewer: unit -> Result<EventId * 'A, string>). It can be obtained by the CommandHandler module itself.getStorageStateViewera<'A, 'E> (for stored based state viewer. The one for Broker based is coming soon)

- [new blog post](https://medium.com/@tonyx1/a-little-f-event-sourcing-library-part-ii-84e0130752f3)
- Version 1.4.1: little change in Kafka consumer. Can use DeliveryResults to optimize tests
- Version 1.4.0: runCommand instead of Result<unit, string> returns, under result, info about event-store created IDs (Postgres based) of new events and eventually Kafka Delivery result (if Kafka is configured). 
- Version 1.3.9: Repository interface changed (using Result type when it is needed). Note: the new Repository interface (and implementation) is __not compatible__ with the one introduced in Version 1.3.8!
- Version 1.3.8: can use a new Repository type instead of lists (even though they are still implemented as plain lists at the moment) to handle collections of entities.
- Version 1.3.5: the library is split into two nuget packages: Sharpino.Core and Sharpino.Lib. the Sharpino.Core can be included in a Shared project in the Fable Remoting style. The collections of the entities used in the Sharpino.Sample are not lists anymore but use Repository data type (which at the moment uses plain lists anyway). 

- Version 1.3.4 there is the possibility to choose a pessimistic lock (or not) in command processing. Needed a configuration file named appSettings.json in the root of the project with the following content:
```json

    "SharpinoConfig": {
        "PessimisticLock": false // or true
    }

``` 

More documentation (a little bit  out of date. Will fix it soon) [(Sharpino gitbook)](https://tonyx.github.io)

<a href="https://www.buymeacoffee.com/Now7pmK92m" target="_blank"><img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Buy Me A Coffee" style="height: 60px !important;width: 217px !important;" ></a>

