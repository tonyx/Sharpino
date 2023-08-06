# Sharpino


<img src="ico/sharpino.png" alt="drawing" width="50"/>


## A minimalistic event-sourcing framework for F#

## Projects:

__Sharpino.Lib__:

- [Core.fs](Sharpino.Lib/Core.fs): Abstract definition of _Events_, _Commands_ and _Undoer_ (the reverse of a command to be used if storage lacks of transaction between streams). Definition of _EvolveUnForgivingErrors_ and "normal" _Evolve_. The former rise an error if there are some events that cannot be applied to the current state of the aggregate. The latter just skip those events.
Note: in some strict setup, for instance in using Postgres or in memory repository, and in making the command be processed by the Mailboxprocessor (which is how you will see in the examples ensuring single threaded execution of command->event->store), the EvolveUnForgivingErrors would not be necessary. However is theoretically possible having inconsistent events when the order of the events is not guaranteed (e.g. in a distributed system). To increase the throughput you may actually want to process command not in single thread anymore assuming the risk of storing conflicting/unconsisting events.

- [Repository.fs](Sharpino.Lib/Repository.fs): gets and stores snapshots, execute commands, produces and store events using the __storage__.
- [LightRepository.fs](Sharpino.Lib/LightRepository.fs): gets and stores snapshots, execute commands, produces and store events using a storage that supports pub/sub model (only Eventstoredb at the moment).
- [DbStorage.fs](Sharpino.Lib/DbStorage.fs) and [MemoryStorage.fs](Sharpino.Lib/MemoryStorage.fs): Manages persistency in Postgres or in memory. 
- [EventStoreBridge.cs](Sharpino.Lib.EventStore/EventStoreBridge.cs): a C# component connecting EventSTore.
- [Cache.fs](Sharpino.Lib/Cache.fs). Cache events, snapshots and state

__Sharpino.Sample__:

A sample of an api for managing todos with tags and categories. There are two versions in the sense of two different configurations respect to the distribution of the models (collection of entities) between the aggregates. There is a strategy to test the migration between versions (aggregate refactoring) that is described in the code (See: [AppVersions.fs](Sharpino.Sample/AppVersions.fs) and [MultiVersionsTests.fs](Sharpino.Sample.Test/MultiversionsTests.fs))
.

-  __models__ (e.g. [TodoModel](Sharpino.Sample/models/TodosModel.fs)) manage entities.
-  __aggregates__ (e.g. [TodoAggregate](Micro_ES_FSharp_Lib.Sample/aggregates/Todos/Aggregate.fs)) owns a partition of the models and provide memgers to handle them. 

- __aggregate__ members has corresponding __events__ ([e.g. TagsEvents](Sharpino.Sample/aggregates/Tags/Events.fs)) that are Discriminated Unions cases. Event types implement the [Process](Sharpino.Lib/Core.fs) interface. 

- __aggregates__ is related to __Commands__ (e.g. [TagCommand](Sharpino.Sample/aggregates/Tags/Commands.fs)) that are Discriminated Unions cases that can return lists of events by implementing by the [Executable](Sharpino.Lib/Core.fs) interface.
__Commands__ defines also _undoers_ that are functions that can undo the commands to reverse action in a multiple stream operaton for storage that don't support multiple stream transactions (see _LightRepository_).
- A [Storage](Sharpino.Lib/DbStorage.fs) stores and retrieves __aggregates__, _events_ and _snapshots_.
- The [Repository](Sharpino.Lib/Repository.fs) can build and retrieve snapshots, run the __commands__ and store the related __events__.
- The [__api layer__ functions](Sharpino.Sample/App.fs) provide business logic involving one or more aggregate by accessing to their state, and by building one or more command sending them to the __repository__.
- An example of how to handle multiple versions of the application to help refactoring and migration between differnet versions: [application versions](Sharpino.Sample/AppVersions.fs). 

__Sharpino.Sample.tests__
- tests for the sample application

## How to use it
- Just use ordinary dotnet command line tools for building the solution. Particularly you can run tests of the sample application by using the following command:
```bash
dotnet test 
```
You can also run the tests by the following command from the Sharpino.Sample.Tests folder:
```bash
dotnet run
```
In the latter case you gets the output from expecto test runner (in this case the console show eventual standard ouput/printf).

By default the tests run only the in memory implementation of the storage. You can setup the postgres by using dbmate.
In the Sharpino.Sample folder you can run the following command to setup the postgres database:
```bash
dbmate -e DATABASE_URL up
```
(see the .env tosetup the DATABASE_URL environment variable to connect to the postgres database with a connection string).
If you have Eventstore the standard configuration should work. (I have tested it with Eventstore 20.10.2-alpha on M2 Apple Silicon chip under Docker).

__Faq__: 
- Why "Sharpino"? 
    - It's a mix of Sharp and fino (italian for thin).  "sciarpino" (same pronunciation) in italian means also "little scarf". 
- Why another event-sourcing library?
    - I wanted to study the subject and it ended up in a tiny little framework.
- Why F#?  
    - Any functional language from the ML familiy language in my opinion is a good fit for the following reasons:
        - Events are immutable, building the state of the aggregates is a function of those events.
        - Discriminated Unions are suitable to represent events and commands.
        - The use of the lambda expression is a nice trick for the undoers (the unders is returned as a lambda that retrieve the context for appling the undo, and returns another labmda that actually can "undo" the command).
        - It is a .net language, so you can use all the .net ecosystem.
- Can it be used in production?
    - I don'w how well it could scale at the moment because the IStorage interface has basically only the a in-memory (for development) and postgres implementation (for production) and I don't know how well can it scale using it. I have support for EventStoreDB, but it is still experimental.

- What about porting (rewriting) to other languages?
    - Many concept I used in the "sample" application are typical F#, so I would say it is not convenient rewriting them in C#. Another functional language supporting Discriminated Union woul be ok. I think that Rust, Ocaml, Erlang, Haskell... can be good candidates for an easy porting.


[More docs (still in progress)](https://tonyx.github.io)