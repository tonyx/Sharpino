module sharpino.Sample.Tests
open Expecto


[<EntryPoint>]
let main argv =
    let startingTime = System.DateTime.Now
    let result = Tests.runTestsInAssemblyWithCLIArgs ([]) argv
    let endingTime = System.DateTime.Now
    let executionTime = (endingTime - startingTime).TotalMilliseconds
    printfn "\nexecution time: %A\n" executionTime
    result
