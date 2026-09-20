module Etymon.Std.Tests.Main

open Expecto

[<EntryPoint>]
let main argv =
    runTestsWithCLIArgs
        []
        argv
        (testList "Etymon.Std" [ ParseTests.tests; StrTests.tests; DictEnvTests.tests; FilesTests.tests ])
