namespace Tonyx.EventSourcing

module Conf =
    type StorageType = Postgres | Memory

    let storageType = Memory

    let isTestEnv = true

    // the key of each element is the name of the storage - storageName in the root type of each aggregate
    let syncobjects = 
        [
            ("_tags", new obj()) 
            ("_todo", new obj())
        ] 
        |> Map.ofList

    // Update those two entries with the name of the root type of each aggregate.
    let intervalBetweenSnapshots = 
        [
            ("_tags", 5)
            ("_todo", 5)
        ] 
        |> Map.ofList

    // it is highly impertive that we are able to use properly dev and prod.
    // making sure that in prod the user has nor right to delete any row
    let connectionString =
        if isTestEnv then
            "Server=127.0.0.1;"+
            "Database=safe2;" +
            "User Id=safe;"+
            "Password=safe;"
        else
            "Server=127.0.0.1;"+
            "Database=*****;" +
            "User Id=*****;"+
            "Password=*****;"

    let cacheSize = 13