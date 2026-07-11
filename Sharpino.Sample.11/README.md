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

```bash
DATABASE_URL="postgres://yourusername@127.0.0.1:5432/sharpino_coursemanager?sslmode=disable"
CONNECTION_URL="Server=127.0.0.1;Database=sharpino_coursemanager;User Id=yourusername"
```

Note: you probably can just use the operaing system credential to connect to the db. 
This is true for example if you install Postgres using Homebrew on MacOS.
Alteratively, you may have to specify a proper username and password with enough permissions.
A simplere way is to use the docker pgsql configuration (as the one used in the template, wich uses the port 5434 rather than 5432)  

```bash
dbmate up
```