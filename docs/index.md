# Etymon

**Define the type once. Derive everything else.**

Etymon is a suite of F# libraries built on one idea: a single schema definition
should be the source of truth for JSON encoding, decoding, validation,
documentation, test data and database structure — rather than five hand-written
things you keep in sync by remembering to.

## Where to start

The shortest honest introduction is
[`samples/Etymon.Sample.Derivations`](https://github.com/CaelusMinds/Etymon/tree/main/samples/Etymon.Sample.Derivations):
one `Booking` schema, then the JSON codec, the validation errors, the OpenAPI
document, the TypeScript declarations, the PostgreSQL table and the
configuration reader, none of which restate a single rule.

```bash
dotnet run --project samples/Etymon.Sample.Derivations
```

Then the [guides](guides.html), and the reference below.

## The packages

| Package | What it owns |
| --- | --- |
| `Etymon.Core` | Paths, the constraint vocabulary, the accumulating error model, refined types, `Secret<'T>` |
| `Etymon.Base` | The .NET base library, returning `option` and `Result` instead of `null` and exceptions |
| `Etymon.Schema` | `Schema<'T>` — one value describing encode, decode, validate and document |
| `Etymon.Schema.OpenApi` | JSON Schema 2020-12 and OpenAPI 3.1 component schemas |
| `Etymon.Schema.TypeScript` | TypeScript declarations. Types only |
| `Etymon.Invariants` | Rules no single field can hold |
| `Etymon.Invariants.FsCheck` | Generators that produce only values a schema accepts |
| `Etymon.Config` | Configuration through a schema, every problem at once, with provenance |
| `Etymon.Migrations` | A relational model, snapshot diffing and SQL. Carries no database driver |
| `Etymon.Api` | HTTP endpoints described once. Performs no HTTP |
| `Etymon.Api.Giraffe` | Serves an endpoint as a Giraffe `HttpHandler` |
| `Etymon.Api.AspNetCore` | Serves an endpoint on minimal-API routing |
| `Etymon.Api.Client` | Calls an endpoint. No server, no web framework |
| `Etymon` | Meta-package: everything that costs nothing but `FSharp.Core` |

## The commitments

Everything in Etymon follows from these.

**Expected failures are values.** Anything that fails for an ordinary reason
returns `Validation<'T>`. Exceptions mean a bug, not a rejected input.

**Errors accumulate.** Four bad fields produce four errors, each with a path like
`address.zip` or `items[3].name`. The only combinator that stops early is `bind`,
because a dependent step has no value to work with.

**Explicit beats derived.** Combinators are the primary path: no reflection,
friendly to trimming and native AOT. Reflection-based derivation exists as a
labelled convenience, never as the default.

**Constraints are data.** One vocabulary, defined in Core, read by every package
that derives anything from it. A rule Etymon cannot inspect is `Opaque` —
enforced and documented, but never half-derived into something that looks right
and is not.

**Ambiguity is an error, not a guess.** Where a derivation would have to choose
between reasonable options, Etymon returns an error naming the choice rather than
picking a default you would discover in production.
