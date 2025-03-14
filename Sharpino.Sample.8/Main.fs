// For more information see https://aka.ms/fsharp-console-apps
// printfn "Hello from F#"
module Sharpino.Sample._8

open Expecto

[<EntryPoint>]
let main argv =
    Tests.runTestsInAssemblyWithCLIArgs ([]) argv
