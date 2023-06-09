namespace Sharpino.EventSourcing

module Conf =

    type Serialization = JsonSer | BinarySer // for future use

    let isTestEnv = true

    let connectionString =
        if isTestEnv then
            "Server=127.0.0.1;"+
            "Database=es_01;" +
            "User Id=safe;"+
            "Password=safe;"
        else
            "Server=127.0.0.1;"+
            "Database=*****;" +
            "User Id=*****;"+
            "Password=*****;"

    let cacheSize = 100 