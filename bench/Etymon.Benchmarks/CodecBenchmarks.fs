/// What a schema costs, against what it replaces.
module Etymon.Benchmarks.CodecBenchmarks

open System.Text.Json
open BenchmarkDotNet.Attributes
open Etymon
open Etymon.Benchmarks.Domain

[<MemoryDiagnoser>]
type Decoding() =

    /// The floor: no validation, no abstraction, straight at the document.
    [<Benchmark(Baseline = true)>]
    member _.ByHand() = decodeByHand customerJson

    /// Reflection, also without validation.
    [<Benchmark>]
    member _.SystemTextJson() =
        JsonSerializer.Deserialize<Customer>(customerJson, systemTextJsonOptions)

    /// The same fields, plus every rule the schema declares.
    [<Benchmark>]
    member _.Etymon() =
        Schema.fromJson customerSchema customerJson

[<MemoryDiagnoser>]
type Encoding() =

    [<Benchmark(Baseline = true)>]
    member _.SystemTextJson() =
        JsonSerializer.Serialize(sampleCustomer, systemTextJsonOptions)

    [<Benchmark>]
    member _.Etymon() =
        Schema.toJson customerSchema sampleCustomer

/// Nested and repeated: twenty lines inside an order, where per-field work is
/// multiplied and paths get deeper.
[<MemoryDiagnoser>]
type Nested() =

    [<Benchmark>]
    member _.Decode() = Schema.fromJson orderSchema orderJson

    [<Benchmark>]
    member _.Encode() = Schema.toJson orderSchema sampleOrder

/// The path nobody optimises and everybody hits: a request that is wrong.
///
/// Worth measuring separately because it is the one an attacker controls. A
/// decoder that is fast on good input and pathological on bad input is a denial
/// of service with extra steps.
[<MemoryDiagnoser>]
type Rejecting() =

    [<Benchmark>]
    member _.FourBadFields() =
        Schema.fromJson customerSchema invalidCustomerJson

    /// Ten thousand elements of the wrong type in one array.
    ///
    /// The shape of an amplification attack: a small body, a large answer. This
    /// was quadratic until the error collection stopped being a list, and the
    /// benchmark is here so that it is noticed if it becomes quadratic again.
    [<Benchmark>]
    member _.TenThousandBadElements() =
        Schema.fromJson (Schema.list Schema.int) manyBadElements
