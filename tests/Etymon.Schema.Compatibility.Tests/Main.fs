module Etymon.Schema.Compatibility.Tests.Main

open Expecto

[<EntryPoint>]
let main argv =
    runTestsWithCLIArgs
        []
        argv
        (testList
            "Etymon.Schema.Compatibility"
            [
                ShapeTests.tests
                SnapshotTests.tests
                CompatibilityTests.tests
                WireTests.tests
            ])
