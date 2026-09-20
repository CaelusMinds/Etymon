module Etymon.Schema.TypeScript.Tests.Main

open Expecto

[<EntryPoint>]
let main argv =
    runTestsWithCLIArgs [] argv (testList "Etymon.Schema.TypeScript" [ TypeScriptTests.tests ])
