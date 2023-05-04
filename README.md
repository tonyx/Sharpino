# Micro_ES_FSharp_Lib
An event sourcing micro library in FSharp based on Postgres or in memory storage.
The solution consists on the library by itself, a sample project and the tests of the sample.

The solution is divided in three

## Single Projects:

__Micro_ES_FSharp_Lib__:

- [EventSourcing.fs](Micro_ES_FSharp_lib/EventSourcing.fs): Abstract definition of Events and Commands. Definition of the "evolve" function
- Repository.fs: get and store snapthos. Run commands, and store related events.
- [DbStorage.fs](Micro_ES_FSharp_lib/DbStorage.fs) and [MemoryStorage.fs](Micro_ES_FSharp_lib/MemoryStorage.fs): Manages persistency of Events and Snapshots.
- [Utils.fs](Micro_ES_FSharp_lib/Utils.fs): some common functions to manage serialization, Result and Railway Oriented Error management
- [Config.fs](Micro_ES_FSharp_lib/Conf.fs) define storage type, lock object for aggregates, interval between snapshots


__Micro_ES_FSharp_Lib.Sample__:

- For each branch: 

- an entry point of the applications available for external calls: [App.fs](Micro_ES_FSharp_Lib.Sample/App.fs)

- __Two Aggregates__:
- -  For each branch an aggregate a definition of Commands and events as follows: [ Todos Commands.fs](Micro_ES_FSharp_lib.Sample/aggregates/Todos/Commands.fs) and [Todos Events.fs](Micro_ES_FSharp_lib.Sample/aggregates/Todos/Events.fs)
- - Models (entities, value object, whatever is needed by the aggregate)
- - minimal scripts to define snapshots and events table for Postgres, if wanted as storage: [sqlSetup.sql](Micro_ES_FSharp_Lib.Sample/aggregates/Todos/sqlSetup.sql)

__Micro_ES_FSharp_Lib.tests__:
- the tests are grouped into tests of models, tests of aggregates and application layer tests. They use the actual configuration (Conf.fs) to decide if using memory or postgres storage.

__To be continued__: managing versioning, managing tools and suggesting practicies for facilitating aggregate refactoring and migration.


