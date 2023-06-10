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

[More docs (still in progress)](https://tonyx.github.io)