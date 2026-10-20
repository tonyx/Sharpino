# Sharpino Template

## Overview

This example uses the local db postgres installation (no docker)


User the following env file:
```
port=5432
database=sharpino
userId=sharpino
password=sharpino
DATABASE_URL="postgres://sharpino:sharpino@127.0.0.1:5432/sharpino?sslmode=disable"
```


```shell
    dbmate up 
```
run
```shell
    dotnet run
```

More info about [Sharpino](https://github.com/tonyx/Sharpino)


