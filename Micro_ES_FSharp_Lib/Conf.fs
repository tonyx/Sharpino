namespace Tonyx.EventSourcing

module Conf =

    type Serialization = JsonSer | BinarySer // for future use

    let isTestEnv = true

    // let intervalBetweenSnapshots = 
    //     [
    //         ("_tags", 15)
    //         ("_todo", 15)
    //         ("_categories", 15)
    //     ] 
    //     |> Map.ofList



    // it is highly impertive that we are able to use properly dev and prod.
    // making sure that in prod the user has nor right to delete any row
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