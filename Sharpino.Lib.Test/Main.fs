// For more information see https://aka.ms/fsharp-console-apps
module Sharpino.Lib.Test.Main
open Expecto

[<EntryPoint>]
let main argv =
    Tests.runTestsInAssemblyWithCLIArgs ([]) argv

