module Etymon.Schema.Tests.Main

open Expecto

[<EntryPoint>]
let main argv =
    runTestsWithCLIArgs [] argv (testList "Etymon.Schema" [ SchemaTests.tests ])
