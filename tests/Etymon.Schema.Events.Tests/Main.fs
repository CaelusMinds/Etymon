module Etymon.Schema.Events.Tests.Main

open Expecto

[<EntryPoint>]
let main argv =
    runTestsWithCLIArgs [] argv (testList "Etymon.Schema.Events" [ SnapshotTests.tests; CompatibilityTests.tests ])
