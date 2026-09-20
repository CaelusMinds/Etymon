module Etymon.Base.Tests.Main

open Expecto

[<EntryPoint>]
let main argv =
    runTestsWithCLIArgs [] argv (testList "Etymon.Base" [ StrTests.tests; DictEnvTests.tests; FilesTests.tests ])
