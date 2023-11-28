namespace Sharpino.Lib.Shared
open System

module Commons =
    [<Fable.Core.Mangle>]
    type Entity =
        abstract member Id: Guid
