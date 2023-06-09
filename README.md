# Micro_ES_FSharp_Lib


An event-sourcing micro library in FSharp based on Postgres or in memory storage. The solution consists of the library by itself, a sample project, and the sample tests.

## Disclaimer: 
This is not an official guide to the Event-Sourcing pattern. 

## Projects:


__Micro_ES_FSharp_Lib__:

- [EventSourcing.fs](Micro_ES_FSharp_Lib/EventSourcing.fs): Abstract definition of Events and Commands. Definition of the "evolve" function
- [Repository.fs](Micro_ES_FSharp_Lib/Repository.fs): get and store the snapshots, run the commands and put in the storage the events they produce.
- [DbStorage.fs](Micro_ES_FSharp_Lib/DbStorage.fs) and [MemoryStorage.fs](Micro_ES_FSharp_Lib/MemoryStorage.fs): Manages persistency in Postgres or in memory. 
- [Utils.fs](Micro_ES_FSharp_Lib/Utils.fs): some common functions.
- [Cache.fs](Micro_ES_FSharp_Lib/Cache.fs). Cache events and snapshots.


__Micro_ES_FSharp_Lib.Sample__:


- By using __models__ (e.g. [TodoModel](Micro_ES_FSharp_Lib.Sample/models/TodosModel.fs)) you organize your basic data.
- By using  __aggregates__ (e.g. [TodoAggregate](Micro_ES_FSharp_Lib.Sample/aggregates/Todos/Aggregate.fs)) and their members, you access and change models in a consistent way. Aggregates must define the following static members: a 'Zero' (initial) instance, a StorageName and a Version. 

- For each __aggregate__ you will define __events__ ([e.g. TagsEvents](Micro_ES_FSharp_Lib.Sample/aggregates/Tags/Events.fs)) that are Discriminated Unions cases that wrap calls to aggregate members by implementing the [Process](Micro_ES_FSharp_Lib/EventSourcing.fs) interface. 
- For each __aggregate__ you will define __Commands__ (e.g. [TagCommand](Micro_ES_FSharp_Lib.Sample/aggregates/Tags/Commands.fs)) that are Discriminated Unions cases that return lists of events by implementing by the [Executable](Micro_ES_FSharp_Lib/EventSourcing.fs) interface.
- With [Storage](Micro_ES_FSharp_Lib/DbStorage.fs) you store and retrieve __aggregates'__ events and snapshots.
- By the [Repository](Micro_ES_FSharp_Lib/Repository.fs) you can build and retrieve snapshots, run the __commands__ and contextually store the __events__.
- By the [__api layer__ functions](Micro_ES_FSharp_Lib.Sample/App.fs) you expose business logic by functions that can retrieve the current state of the __aggregates__, call some of their members and/or build single or multiple commands related to one or more  __aggregates__. This layer will send those commands to the __repository__. 
- You may handle and test multiple versions of the application to handle refactoring and migrations between versions: [application versions](Micro_ES_FSharp_Lib.Sample/AppVersions.fs). 


__Micro_ES_FSharp_Lib.Sample__:
- You will test __models__, __aggregates__, __api layer__, and multiple versions (current, refactored and with different storages) of the application api. 


