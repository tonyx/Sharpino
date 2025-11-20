## Sharpino Example 11

An example of using FSharp.SystemTextJson (borrowing the example 9) instead of FsPickler

This example is also used as a reference for the medium post: [F# Domain Model with Event Sourcing vs C# with Entity Framework](https://medium.com/@tonyx1/f-domain-model-with-event-sourcing-vs-c-with-entity-framework-ff870ce5c48c)

by running 
```` dotnet run````
you get the results of the performance test only on the local postgres db (any version)

by running 
```
dotnet run --configuration:rabbitmq

```

you get the results of the performance tests on the local db and rabbitmq 

Please create a user safe with password safeuserpassword (or any other you prefer)
Please setup a .env file with the following content:

```
DATABASE_URL="postgres://postgresusername:postgrespassword@127.0.0.1:5432/sharpino_coursemanager?sslmode=disable"
password=safeuserpassword
```

