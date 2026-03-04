# Sharpino Template
```shell
dotnet install Sharpino.Templates
dotnet new sharpino
docker compose up -d
dotnet run (run the tests)
```

## Overview

This example is instrumented with L2_cache layer based on Azure SQL and Azure Service Bus.

Briefly: the L2 cache is a layer that caches the events in a database and a backplane (in this case Azure Service Bus).
The script to setup the L2 cache is in the sql-cache-schema.sql file.

This should make it possible to propagate the L1 cached values to the L2 cache layer and to send the sync events to the backplane. This should help when you have multiple services that need to be updated when a cache is updated to prevent data inconsistency i.e. single L1 cache values out of sync with the respective L2 cache values.

Automated tests is there to verify that the L2 cache is working as expected.
A manual test has been done to verify that the message bus receives the events. 

using port5434 for postgres docker container 
you can change it in .env
use the .env also to specify the connection string to the azure sql and azure service bus.

You should not need to setup the db manually.
However, you may do it by running the following command (requires dbmate to be installed):

```shell
    dbmate up 
```

you need a .env file like follows 
```shell
port=5434
database=sharpino
userId=sharpino
password=password
DATABASE_URL="postgres://sharpino:password@127.0.0.1:5434/sharpino?sslmode=disable"

L2_CACHE_SQL_URL=your_sql_connection_string
L2_CACHE_SQL_TABLE_NAME=your_table_name

SERVICE_BUS_CONNECTION_STRING=your_service_bus_connection_string
SERVICE_BUS_TOPIC_NAME=your_service_bus_topic_name
SERVICE_BUS_SUBSCRIPTION_NAME=your_service_bus_subscription_name

```

More info about [Sharpino](https://github.com/tonyx/Sharpino)


