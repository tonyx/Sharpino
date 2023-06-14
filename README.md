# Sharpino


<img src="ico/sharpino.png" alt="drawing" width="50"/>

## An event-sourcing library in F#

Sharpino is a library for event-sourcing in F#.
## Projects:

__Sharpino.Lib__:

- [Core.fs](Sharpino.Lib/Core.fs): Abstract definition of Events and Commands. Definition of the "evolve" function.
- [Repository.fs](Sharpino.Lib/Repository.fs): gets and stores snapshots, execute commands, produces and store events using the __storage__.
- [DbStorage.fs](Sharpino.Lib/DbStorage.fs) and [MemoryStorage.fs](Sharpino.Lib/MemoryStorage.fs): Manages persistency in Postgres or in memory. 
- [Cache.fs](Sharpino.Lib/Cache.fs). Cache events and snapshots.

__Sharpino.Sample__:

-  __models__ (e.g. [TodoModel](Sharpino.Sample/models/TodosModel.fs))  manage data.
-  __aggregates__ (e.g. [TodoAggregate](Micro_ES_FSharp_Lib.Sample/aggregates/Todos/Aggregate.fs)) owns models and provide member to manage models in a consistent way.

- __aggregate__ members has corresonding __events__ ([e.g. TagsEvents](Sharpino.Sample/aggregates/Tags/Events.fs)) that are Discriminated Unions cases. Event types implement the [Process](Sharpino.Lib/Core.fs) interface. 
- __aggregates__ is related to __Commands__ (e.g. [TagCommand](Sharpino.Sample/aggregates/Tags/Commands.fs)) that are Discriminated Unions cases that can return lists of events by implementing by the [Executable](Sharpino.Lib/Core.fs) interface.
- A [Storage](Sharpino.Lib/DbStorage.fs) stores and retrieves __aggregates__ events and snapshots.
- The [Repository](Sharpino.Lib/Repository.fs) can build and retrieve snapshots, run the __commands__ and contextually store the related __events__.
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
In the latter case you gets the output from expecto test runner (that I think is more readable than the output from dotnet test command, and particularly it prompts any printf command).


__Faq__: 
- Why "Sharpino"? 
    - It's a mix of Sharp and fino (italian for thin).  "sciarpino" (same pronunciation) in italian means also "little scarf". 
- Why another event-sourcing library?
    - Why not?
- Why F#?  
    - I think that an ML familiy language is a good fit for the following reasons:
        - events are immutable and building the state of the aggregates is a funcition.
        - Discriminated Unions are a good fit for events and commands.
        - It is a .net language, so you can use all the .net ecosystem.
- Can it be used in production?
    - I don'w how well it could scale at the moment because the IStorage interface has basically only the a in-memory (for development) and postgres implementation (for production) and I don't know how well can it scale using it. I'm planning to add Kafka as storage that would be more scalable, by changing the concept of IStorage to a more generic IEventBus.  
- Can it be used with other languages?
    - Many concept I used in the "sample" application are typical F#, so I would say it is not convenient rewriting them in C#. However I don't see any problem in writing a front end by using any language and technology.


[More docs (still in progress)](https://tonyx.github.io)