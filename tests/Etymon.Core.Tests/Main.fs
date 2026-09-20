module Etymon.Core.Tests.Main

open Expecto

[<EntryPoint>]
let main argv =
    runTestsWithCLIArgs
        []
        argv
        (testList
            "Etymon.Core"
            [
                ParseTests.tests
                PathTests.tests
                ConstraintTests.tests
                ValidationTests.tests
                CheckTests.tests
                SecretTests.tests
                RefineTests.tests
            ])
