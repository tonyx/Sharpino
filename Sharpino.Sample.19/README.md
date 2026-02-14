# Sharpino Template
```shell
dotnet install Sharpino.Templates
dotnet new sharpino
docker compose up -d
dotnet run (run the tests)
```

## Overview
using port5434 for postgres docker container 
you can change it in .env

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

```

More info about [Sharpino](https://github.com/tonyx/Sharpino)


