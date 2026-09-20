module Etymon.Migrations.Tests.Main

open Expecto

[<EntryPoint>]
let main argv =
    runTestsWithCLIArgs [] argv (testList "Etymon.Migrations" [ MappingTests.tests; ScriptTests.tests ])
