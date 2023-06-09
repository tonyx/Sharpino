module Micro_ES_FSharp_Lib.Test
open Expecto

[<EntryPoint>]
let main argv =
    let startingTime = System.DateTime.Now
    let result = Tests.runTestsInAssembly defaultConfig argv
    let endingTime = System.DateTime.Now
    let executionTime = (endingTime - startingTime).TotalMilliseconds
    printfn "\nexecution time: %A\n" executionTime
    result
