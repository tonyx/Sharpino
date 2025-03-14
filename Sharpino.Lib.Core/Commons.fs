namespace Sharpino.Lib.Core
open System

// this should disappear
module Commons =
    [<Fable.Core.Mangle>]
    type Entity =
        abstract member Id: Guid
