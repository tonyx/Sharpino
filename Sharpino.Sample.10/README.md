Configure your .env as follows:

```bash
DATABASE_URL="postgres://yourusername@127.0.0.1:5432/sharpino_sample_10?sslmode=disable"
CONNECTION_STRING="Server=127.0.0.1;Database=sharpino_sample_10;User Id=yourusername"
password=safe
```

Note: you probably can just use the operaing system credential to connect to the db. 
This is true for example if you install Postgres using Homebrew on MacOS.
Alteratively, you may have to specify a proper username and password with enough permissions.
A simplere way is to use the docker pgsql configuration (as the one used in the template, wich uses the port 5434 rather than 5432)  

Run dbmate to set up the database:
```bash
dbmate up
```