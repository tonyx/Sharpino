
The L2 cache is still an experimental feature that can be checked by this example.

In this setup we have a command line application that tests the L1 and L2 cache and the invalidation messages.
You wll run two command line text applications in two different terminals.

They will use the dockerized vertions of:
- PostgresSql as a event store
- Azure sql as L2 cache
- Internal memory as L1 cache
- Azure service bus emulator to exchange messages to force evicting L1 cache entries after any new event.

That means that for an aggregate X we will have
1. a stream of events on PgSql to rebuild the aggregate
2. a snapshot of the aggregate on PgSql every 100 events
3. a L2 cache entry on Azure sql with the aggregate
4. a L1 cache entry on internal memory with the aggregate
5. a message on Azure service bus to evict the L1 cache entry after any new event.

In this setup any state is retrived, in order by the following:
1. L1 cache
2. L2 cache
3. Event store (i.e. latest snapshots + following events)


To execute this example the following setup is required:

1. Create the following local files: .env with the following content:
```
port=5435
database=sharpino
userId=sharpino
password=password
DATABASE_URL="postgres://sharpino:password@127.0.0.1:5435/sharpino?sslmode=disable"
```

2. Create the following local file appSettings.json with the following content:
```
{
  "IsTestEnv": true,
  "CancellationTokenSourceExpiration": 100000,
  "EventStoreTimeout": 10000,
  "PgSqlJsonFormat": "PlainText",
  "MailBoxCommandProcessorsSize": 100,
  "DetailsCacheExpiration": 300,
  "DetailsCacheDependenciesExpiration": 301,
  "AggregateCacheExpiration": 600,
  "DistanceBetweenSnapshots": 100,

 "Cache": {
    "IgnoreIncomingBackplaneNotifications": false,
    "L2SqlCacheEnabled": true,
    "L2CacheSqlUrl": "Server=127.0.0.1,1433;Database=sharpinoCache;User Id=sa;Password=Sharpino@1234;TrustServerCertificate=True;",
    "L2CacheSqlTableName": "SharpinoL2Cache",
    "L2ServiceBusEnabled": true,
    "ServiceBusConnectionString": "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;",
    "ServiceBusTopicName": "sharpino-topic",
    "ServiceBusSubscriptionName": "sharpino-sub-2"
  },

  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

3. execute the docker setup (docker-compose up -d, or similar commands to setup and run the images according to docker-compose.yml). Note that the docker images target the mac OS arm architecture. You may need to adapt them for a Linux/Windows Docker setup.
4. The dockerized postgres db  based event-store should be ready, but you may need to wipe and restore it by the commands `dbmate drop` and `dbmate up`.
5. Make sure that the sql server contains the L2 cache table by executing the sql-cache-schema.sql script.
6. open a terminal and execute "dotnet run"
7. open a different terminal and execute "ASPNETCORE_ENVIRONMENT=Session2 dotnet run" (in unix terminal, or an equivalent in dos/powershell)
8. what is expected is that if the first terminal "rename" an object then the second terminal is able to see the object renamed because of the L1 cache invalidation message.

Notes, follow up:
1. A Redis based similar mechanism shoud be probably more appropriate. I will see later
2. Many other previous examples (based on RabbitMq) use a different approach: the command handler after storing any event will pack it into a message and so an indpendent listener works as a "state viewer" on its own in a way that is detached from any L1/L2 cache. That approach is a different thing from what we are doing here. Particualarly, the "refreshable details" feature is not investigated by that approach.
3. The most important reason for this setup is that the "only L1" cache can work only if the application has a single instance, and the data will definitely be not in sinc and any out of sync data will be unable to add event to the event store (because of an optimistic lock eventid check). One way to hande this (without inter nodes sync messages we are dealing here) is by giving any object a very short L1 cache expiration time. Imagine an app that scales by using tech stack like Azure functions that can create new instances "on the fly". In that case a L1 cache is not enough and a L2 cache is required. 

4. Feel free to improve the code and the setup. Particularly, the documentation needs some improvements in relation to  
