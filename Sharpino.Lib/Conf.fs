namespace Sharpino
open System

[<Obsolete("there will no need to use conf. config by injection is better for instance for testing")>]
module Conf =
    type Serialization = JsonSer | BinarySer // for future use
    // this will go away and the difference between test and prod db will be handled differently
    let isTestEnv = true
