1) Implement dockerized version of sql azure working for MAC m1 architecture
2) Implement a dockerized version of Azure Service Bus
3) Write a copy and paste ready .env like file in the following format filling in the missing parameters  (from DATABASE_URL to SERVICE_BUS_SUBSCRIPTION_NAME) with the values from the docker containers we created:
```
port=5434
database=sharpino
userId=sharpino
password=password

DATABASE_URL= 
L2_CACHE_SQL_URL=
L2_CACHE_SQL_TABLE_NAME=

SERVICE_BUS_CONNECTION_STRING=
SERVICE_BUS_TOPIC_NAME=
SERVICE_BUS_SUBSCRIPTION_NAME=
```
4) Access to the .env file and update the values of the parameters according to the configuration we created in the previous step
5) Run the test (dotnet run) and verify that the test is passing or change any parameters in the .env file or in docker setup until the test is passing