# Micro_ES_FSharp_Lib
An event-sourcing micro library in FSharp based on Postgres or in memory storage. The solution consists of the library by itself, a sample project, and the sample tests.

The solution is divided in three subprojects

## Single Projects:

__Micro_ES_FSharp_Lib__:

- [EventSourcing.fs](Micro_ES_FSharp_Lib/EventSourcing.fs): Abstract definition of Events and Commands. Definition of the "evolve" function
- Repository.fs: get and store snapshots, run commands and store related events.
- [DbStorage.fs](Micro_ES_FSharp_Lib/DbStorage.fs) and [MemoryStorage.fs](Micro_ES_FSharp_Lib/MemoryStorage.fs): Manages persistency in Postgres or in memory. 
- [Utils.fs](Micro_ES_FSharp_Lib/Utils.fs): some common functions to manage serialization and results.
- [Cache.fs](Micro_ES_FSharp_Lib/Cache.fs). Cache events and snapshots.
- [Conf.fs](Micro_ES_FSharp_Lib/Conf.fs)  lock object for aggregates, and the interval between snapshots for each aggregate


__Micro_ES_FSharp_Lib.Sample__:


- The __models__ are plain data usually organized as records with members and a 'zero' (initial/empty) instance
- An __aggregates__ is  a subset of models, exposes single or multi-model logic, has to declare  a 'Zero' (initial) instance, a name and a version by proper static members.
- For each __aggregate__ we define __Events__ that are a Discriminated Unions cases that wrap calls to aggregate members by the _Process_ interface
- For each __aggregate__ we define __Commands__ that are Discriminated Unions cases that return lists of events by implementing by the 'Executable' interface
- The __storage__ stores and retrieve events and snapshots of the aggregates.
- The __Repository__ builds and retrive snapshots, run and store the commands. 
- The application functions build single or multiple commands related to single or multiple aggregates and send them to the repository. 
- The applications versions define the application api in different version by different storage types. We can use them to organize aggregate refactoring and test different versions and aggregate refactoring by parametrized tests


