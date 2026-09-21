/// What it costs to be able to say where a problem was.
///
/// Every field read builds a path, on the success path, for an error that
/// usually never happens. That is the price of `items[3].address.zip` instead of
/// "invalid request", and it should be known rather than assumed.
module Etymon.Benchmarks.PathBenchmarks

open BenchmarkDotNet.Attributes
open Etymon

[<MemoryDiagnoser>]
type Paths() =

    let deep =
        Path.root
        |> Path.field "order"
        |> Path.field "lines"
        |> Path.index 3
        |> Path.field "address"

    /// One step, which is what a field read does.
    [<Benchmark>]
    member _.PushOne() = Path.field "zip" Path.root

    /// Five steps, which is what a nested collection read does.
    [<Benchmark>]
    member _.PushFive() =
        Path.root
        |> Path.field "order"
        |> Path.field "lines"
        |> Path.index 3
        |> Path.field "address"
        |> Path.field "zip"

    /// Only ever done when reporting, so it is allowed to be the slow one.
    [<Benchmark>]
    member _.Render() = Path.toString deep
