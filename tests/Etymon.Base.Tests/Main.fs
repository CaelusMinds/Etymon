module Etymon.Base.Tests.Main

open Expecto

[<EntryPoint>]
let main argv =
    runTestsWithCLIArgs
        []
        argv
        (testList "Etymon.Base" [ ParseTests.tests; StrTests.tests; DictEnvTests.tests; FilesTests.tests ])
