# Sharpino


<img src="ico/sharpino.png" alt="drawing" width="50"/>

An event-sourcing micro library in FSharp based on Postgres or in memory storage. The solution consists of the library by itself, a sample project, and the sample tests.

## Disclaimer: 
This is not an official guide to the Event-Sourcing pattern. 

## Projects:

__Micro_ES_FSharp_Lib__:

- [EventSourcing.fs](Sharpino.Lib/Core.fs): Abstract definition of Events and Commands. Definition of the "evolve" function
- [Repository.fs](Sharpino.Lib/Repository.fs): get and store the snapshots, run the commands and put in the storage the events they produce.
- [DbStorage.fs](Sharpino.Lib/DbStorage.fs) and [MemoryStorage.fs](Sharpino.Lib/MemoryStorage.fs): Manages persistency in Postgres or in memory. 
- [Cache.fs](Sharpino.Lib/Cache.fs). Cache events and snapshots.


__Micro_ES_FSharp_Lib.Sample__:


- By using __models__ (e.g. [TodoModel](Sharpino.Sample/models/TodosModel.fs)) you organize your basic data.
- By using  __aggregates__ (e.g. [TodoAggregate](Micro_ES_FSharp_Lib.Sample/aggregates/Todos/Aggregate.fs)) and their members, you access and change models in a consistent way. Aggregates must define the following static members: a 'Zero' (initial) instance, a StorageName and a Version. 

- For each __aggregate__ you will define __events__ ([e.g. TagsEvents](Sharpino.Sample/aggregates/Tags/Events.fs)) that are Discriminated Unions cases that wrap calls to aggregate members by implementing the [Process](Sharpino.Lib/Core.fs) interface. 
- For each __aggregate__ you will define __Commands__ (e.g. [TagCommand](Sharpino.Sample/aggregates/Tags/Commands.fs)) that are Discriminated Unions cases that return lists of events by implementing by the [Executable](Sharpino.Lib/Core.fs) interface.
- With [Storage](Sharpino.Lib/DbStorage.fs) you store and retrieve __aggregates'__ events and snapshots.
- By the [Repository](Sharpino.Lib/Repository.fs) you can build and retrieve snapshots, run the __commands__ and contextually store the __events__.
- By the [__api layer__ functions](Sharpino.Sample/App.fs) you expose business logic by functions that can retrieve the current state of the __aggregates__, call some of their members and/or build single or multiple commands related to one or more  __aggregates__. This layer will send those commands to the __repository__. 
- You may handle and test multiple versions of the application to handle refactoring and migrations between versions: [application versions](Sharpino.Sample/AppVersions.fs). 


__Micro_ES_FSharp_Lib.Sample__:
- You will test __models__, __aggregates__, __api layer__, and multiple versions (current, refactored and with different storages) of the application api. 


