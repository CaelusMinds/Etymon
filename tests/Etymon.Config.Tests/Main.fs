module Etymon.Config.Tests.Main

open Expecto

[<EntryPoint>]
let main argv =
    runTestsWithCLIArgs [] argv (testList "Etymon.Config" [ ConfigTests.tests ])
