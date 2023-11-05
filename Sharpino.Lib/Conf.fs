namespace Sharpino

module Conf =

    type Serialization = JsonSer | BinarySer // for future use

    // this will go away and the difference between test and prod db will be handled differently
    let isTestEnv = true

    let cacheSize = 10 