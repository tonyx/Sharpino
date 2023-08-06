namespace Sharpino

module Conf =

    type Serialization = JsonSer | BinarySer // for future use

    let isTestEnv = true

    let cacheSize = 100 
    let stateCacheSize = 10 
    let eventStoreConnection = "esdb://localhost:2113?tls=false"