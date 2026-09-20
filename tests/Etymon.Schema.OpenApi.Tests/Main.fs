module Etymon.Schema.OpenApi.Tests.Main

open Expecto

[<EntryPoint>]
let main argv =
    runTestsWithCLIArgs [] argv (testList "Etymon.Schema.OpenApi" [ OpenApiTests.tests ])
