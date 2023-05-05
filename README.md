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
- [Conf.fs](Micro_ES_FSharp_Lib/Conf.fs)  define storage type, lock object for aggregates, and the interval between snapshots for each aggregate


__Micro_ES_FSharp_Lib.Sample__:


- an entry point of the applications available for external calls: [App.fs](Micro_ES_FSharp_Lib.Sample/App.fs)

- __Two Aggregates__:
- -  For each branch an aggregate and a definition of Commands and events as follows: [ Todos Commands.fs](Micro_ES_FSharp_Lib.Sample/aggregates/Todos/Commands.fs) and [Todos Events.fs](Micro_ES_FSharp_Lib.Sample/aggregates/Todos/Events.fs)
- -  Models (entities, value objects... whatever is needed by the aggregate)
- - minimal scripts to define snapshots and events table for Postgres, if wanted as storage: [sqlSetup.sql](Micro_ES_FSharp_Lib.Sample/aggregates/Todos/sqlSetup.sql)

__Micro_ES_FSharp_Lib.tests__:
- the tests are grouped into tests of models, tests of aggregates and application layer tests. They use the actual configuration (Conf.fs) to decide the storage (memory/db).

__To be continued__: managing versioning, managing tools and suggesting practicies for facilitating aggregate refactoring and migration.


