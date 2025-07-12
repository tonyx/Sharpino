module Sharpino.Sample._10.Tests
open Expecto
open Sharpino.CommandHandler
open Sharpino.TestUtils

[<Tests>]
let tests =
    testList "Sharpino.Sample._10.Tests" [
        testCase "test" <| fun _ -> 
            Expect.isFalse false "should be true"    
    ]
