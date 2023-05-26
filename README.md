# Micro_ES_FSharp_Lib
An event-sourcing micro library in FSharp based on Postgres or in memory storage. The solution consists of the library by itself, a sample project, and the sample tests.

The solution is divided in three subprojects

## Projects:

__Micro_ES_FSharp_Lib__:

- [EventSourcing.fs](Micro_ES_FSharp_Lib/EventSourcing.fs): Abstract definition of Events and Commands. Definition of the "evolve" function
- Repository.fs: get and store snapshots, run commands and store related events.
- [DbStorage.fs](Micro_ES_FSharp_Lib/DbStorage.fs) and [MemoryStorage.fs](Micro_ES_FSharp_Lib/MemoryStorage.fs): Manages persistency in Postgres or in memory. 
- [Utils.fs](Micro_ES_FSharp_Lib/Utils.fs): some common functions.
- [Cache.fs](Micro_ES_FSharp_Lib/Cache.fs). Cache events and snapshots.
- [Conf.fs](Micro_ES_FSharp_Lib/Conf.fs) lock objects for aggregates, and the interval between snapshots for each aggregate


__Micro_ES_FSharp_Lib.Sample__:


- By using __models__ (e.g. [TodoModel](Micro_ES_FSharp_Lib.Sample/models/TodosModel.fs)) you organize your data for instances as records with members and a 'Zero' (initial/empty) element.
- By using  __aggregates__ (e.g. [TodoAggregate](Micro_ES_FSharp_Lib.Sample/aggregates/Todos/Aggregate.fs)) and their members, you use models in a consistent and coordinated way, so you have accesso to single or multi-model logic. You aggregates need static member to define: a 'Zero' (initial) instance, a name and a version. 

- For each __aggregate__ you will define __events__ ([e.g. TagsEvents](Micro_ES_FSharp_Lib.Sample/aggregates/Tags/Events.fs)) that are  Discriminated Unions cases that wrap calls to aggregate members by implementing the [Process](Micro_ES_FSharp_Lib/EventSourcing.fs) interface. 
- For each __aggregate__ you will define __Commands__ (e.g. [TagCommand](Micro_ES_FSharp_Lib.Sample/aggregates/Tags/Commands.fs)) that are Discriminated Unions cases that return lists of events by implementing by the [Executable](Micro_ES_FSharp_Lib/EventSourcing.fs) interface.
- With your [Storage](Micro_ES_FSharp_Lib/DbStorage.fs) you store and retrieve __aggregates__ related events and snapshots.
- By the [Repository](Micro_ES_FSharp_Lib/Repository.fs) you can build and retrive snapshots, run the __commands__ and contextually store the __events__.
- By the [__api layer__ functions](Micro_ES_FSharp_Lib.Sample/App.fs) you expose business logic by retriving the current state of the __aggregates__ calling functions on them and/or building single or multiple commands related to one or more  __aggregates__. You will send those commands to the __repository__. 
- The [application versions records](Micro_ES_FSharp_Lib.Sample/AppVersions.fs) is an example of a way to support aggregate refactoring and data migration by specifing different versions of api calls and a data migration function. You can also specify different storage types for different versions. In this way you can test different versions and data migration.


__Micro_ES_FSharp_Lib.Sample__:
- You will test __models__, __aggregates__, __api layer__, and multiple versions (current, refacored and with different storages) of the application api. Note that refactoring may break aggregates test, and model tests but should not break api layer tests.


