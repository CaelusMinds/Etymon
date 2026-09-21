module Etymon.Schema.Sql.Tests.Main

open Expecto

[<EntryPoint>]
let main argv =
    runTestsWithCLIArgs [] argv (testList "Etymon.Schema.Sql" [ MappingTests.tests; ScriptTests.tests ])
