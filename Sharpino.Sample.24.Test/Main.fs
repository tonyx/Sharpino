module Sharpino.Sample24.Tests.Main

open Expecto
open System

[<EntryPoint>]
let main argv =
    let startingTime = DateTime.Now
    let result = Tests.runTestsInAssemblyWithCLIArgs ([]) argv
    let endingTime = DateTime.Now
    let executionTime = (endingTime - startingTime).TotalMilliseconds
    printfn "\nexecution time: %A\n" executionTime
    result
