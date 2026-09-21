module Etymon.Benchmarks.Main

open BenchmarkDotNet.Running

[<EntryPoint>]
let main argv =
    BenchmarkSwitcher.FromAssembly(typeof<CodecBenchmarks.Decoding>.Assembly).Run(argv)
    |> ignore

    0
