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

More info about [Sharpino](https://github.com/tonyx/Sharpino)


