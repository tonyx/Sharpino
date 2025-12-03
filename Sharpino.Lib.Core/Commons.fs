namespace Sharpino.Lib.Core
open System

// pseudo-Repository based on list. It is barely used. Probably will be dismissed
module Commons =
    type Entity =
        abstract member Id: Guid
