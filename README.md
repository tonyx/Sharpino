# Sharpino


<img src="ico/sharpino.png" alt="drawing" width="50"/>


## A minimalistic event-sourcing framework for F#

## Projects:

__Sharpino.Lib__:

- [Core.fs](Sharpino.Lib/Core.fs): Abstract definition of Events and Commands. Definition of the "evolve" function.
- [Repository.fs](Sharpino.Lib/Repository.fs): gets and stores snapshots, execute commands, produces and store events using the __storage__.
- [DbStorage.fs](Sharpino.Lib/DbStorage.fs) and [MemoryStorage.fs](Sharpino.Lib/MemoryStorage.fs): Manages persistency in Postgres or in memory. 
- [Cache.fs](Sharpino.Lib/Cache.fs). Cache events and snapshots.

__Sharpino.Sample__:

-  __models__ (e.g. [TodoModel](Sharpino.Sample/models/TodosModel.fs))  manage entities.
-  __aggregates__ (e.g. [TodoAggregate](Micro_ES_FSharp_Lib.Sample/aggregates/Todos/Aggregate.fs)) owns a partition of the models and provide memgers to handle them. 

- __aggregate__ members has corresponding __events__ ([e.g. TagsEvents](Sharpino.Sample/aggregates/Tags/Events.fs)) that are Discriminated Unions cases. Event types implement the [Process](Sharpino.Lib/Core.fs) interface. 

- __aggregates__ is related to __Commands__ (e.g. [TagCommand](Sharpino.Sample/aggregates/Tags/Commands.fs)) that are Discriminated Unions cases that can return lists of events by implementing by the [Executable](Sharpino.Lib/Core.fs) interface.
__Commands__ defines also undoers that are functions that can undo the command by returning a list of events.
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
(see the .env tosetup the DATABASE_URL environment variable to connect to the postgres database with a connection string)

__Faq__: 
- Why "Sharpino"? 
    - It's a mix of Sharp and fino (italian for thin).  "sciarpino" (same pronunciation) in italian means also "little scarf". 
- Why another event-sourcing library?
    - I wanted to study the subject and it ended up in a tiny little framework.
- Why F#?  
    - Any functional language from the ML familiy language in my opinion is a good fit for the following reasons:
        - events are immutable and building the state of the aggregates is a funcition.
        - Discriminated Unions are suitable to represent events and commands.
        - It is a .net language, so you can use all the .net ecosystem.
- Can it be used in production?
    - I don'w how well it could scale at the moment because the IStorage interface has basically only the a in-memory (for development) and postgres implementation (for production) and I don't know how well can it scale using it. There is now support for EventStoreDB, still experimental.

- Can it be used with other languages?
    - Many concept I used in the "sample" application are typical F#, so I would say it is not convenient rewriting them in C#. Another functional language supporting Discriminated Union woul be ok. No problem in in writing a front end by using any language and technology.


[More docs (still in progress)](https://tonyx.github.io)