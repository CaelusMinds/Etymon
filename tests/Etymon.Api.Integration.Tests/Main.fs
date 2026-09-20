module Etymon.Api.Integration.Tests.Main

open Expecto

[<EntryPoint>]
let main argv =
    runTestsWithCLIArgs [] argv (testList "Etymon.Api.Integration" [ IntegrationTests.tests ])
