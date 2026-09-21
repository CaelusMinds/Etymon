module Etymon.Invariants.Tests.Main

open Expecto

[<EntryPoint>]
let main argv =
    runTestsWithCLIArgs
        []
        argv
        (testList "Etymon.Invariants" [ InvariantTests.tests; GenerateTests.tests; RoundTripProperties.tests ])
