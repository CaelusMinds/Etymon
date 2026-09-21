# Etymon

> *etymon* (n.) — the root word from which later forms derive.

**Define the type once. Derive everything else.**

F# developers hand-write JSON codecs, validation rules, OpenAPI schemas, test
generators and database migrations separately, then keep them in sync by
remembering to. Etymon makes one schema definition the source of truth and
derives the rest from it.

```fsharp
let ageSchema = Schema.int |> Schema.constrain (Check.intRange (Some 0) (Some 130))
```

That one declaration reaches five places, and none of them restate it:

```fsharp
Schema.fromJson personSchema payload    // rejects 200 with "age: must be between 0 and 130"
OpenApi.toJsonSchema personSchema       // "minimum": 0, "maximum": 130
Mapping.tableOf options personSchema    // CHECK ("age" >= 0 AND "age" <= 130)
Generate.valid personSchema             // only ever generates ages in range
Config.load personSchema sources        // "age: expected an integer (from environment)"
```

> **Status: pre-release.** All fifteen packages are built, and the fourteen with
> code in them are tested — **851 tests**, run on both net8.0 and net10.0,
> including end-to-end tests that drive the generated client over real HTTP
> against both a Giraffe server and a minimal-API one. Published to nuget.org as
> `0.1.0-preview.6`; the API can still change, and a preview is where that should
> happen.

**New here?** [**Why Etymon**](docs/why-etymon.md) is the case for it: the same
code written without the library and with it, side by side, for validation,
OpenAPI, SQL, test data, configuration and HTTP — including what it costs and
when not to use it.

## Packages

Install only what you need, or take the `Etymon` meta-package for all of it.

| Package | What it owns | Depends on |
| --- | --- | --- |
| **Etymon.Core** | Paths, culture-explicit parsing, the constraint vocabulary, the accumulating error model, refined types, `Secret<'T>` | FSharp.Core only |
| **Etymon.Base** | Strings, dictionaries, environment variables and file IO, returning `option` and `Result` instead of `null` and exceptions | Core |
| **Etymon.Schema** | `Schema<'T>` — one value describing encode, decode, validate and document | Core |
| **Etymon.Schema.OpenApi** | JSON Schema 2020-12 and OpenAPI 3.1 component schemas | Core, Schema |
| **Etymon.Schema.TypeScript** | TypeScript declarations from the same schemas. Types only | Core, Schema |
| **Etymon.Schema.Compatibility** | Shape snapshots and one compatibility matrix, with two policies over it: event logs and wire contracts. Never rewrites what already exists | Core, Schema |
| **Etymon.Invariants** | Rules that must always be true of a type, including the cross-field ones no single field can hold | Core |
| **Etymon.Invariants.FsCheck** | Generators that produce only values a schema accepts, plus ones it should reject | Core, Schema, Invariants, FsCheck |
| **Etymon.Config** | Configuration through a schema, reporting every problem at once with what was expected and which source supplied it | Core, Base, Schema |
| **Etymon.Schema.Sql** | Renders a Schema as SQL: a table model, a snapshot diff, and a migration script. Drafts migrations; never runs them | Core, Schema |
| **Etymon.Api** | HTTP endpoints described once: method, typed route, request, responses, typed failures. Performs no HTTP | Core, Base, Schema, Schema.OpenApi |
| **Etymon.Api.Giraffe** | Serves an endpoint as a Giraffe `HttpHandler`, composing into the routing you already have | Api, Giraffe |
| **Etymon.Api.AspNetCore** | Serves an endpoint on ASP.NET Core minimal-API routing, with no third-party web framework | Api, ASP.NET Core |
| **Etymon.Api.Client** | Calls an endpoint over HTTP from the same declaration the server is built from. No server, no web framework | Api |
| **Etymon** | Meta-package. No code; references every package that costs nothing but FSharp.Core | — |

## Which package do I need?

- **"I want smart constructors and validation errors with field paths."**
  → `Etymon.Core`. Nothing else. It has no dependency beyond FSharp.Core and
  trims to about 85 KB, so it can sit in a Blazor WebAssembly domain layer.
- **"I hand-write JSON codecs and want the error vocabulary without the magic."**
  → `Etymon.Core`. The error model deliberately does not depend on
  `Etymon.Schema`, so you can take `Path` / `ValidationError` /
  `ValidationErrors` and keep your codecs exactly as they are.
- **"I want JSON and validation from one definition."** → `Etymon.Schema`. It
  takes `Etymon.Core` and nothing else, so a schema in a Blazor WebAssembly
  payload carries no file or environment code it can never call.
- **"…and an OpenAPI document that cannot drift from it."** → add `Etymon.Schema.OpenApi`.
- **"…and property tests that only ever generate values my schema accepts."**
  → add `Etymon.Invariants.FsCheck`.
- **"I have a rule that no single field can hold — an end date after a start
  date."** → `Etymon.Invariants`.
- **"…and a database schema, reviewable in a pull request."** → add
  `Etymon.Schema.Sql` — but read the note about EF Core below first.
- **"I already use Giraffe and want to keep it."** → `Etymon.Api` +
  `Etymon.Api.Giraffe`. Endpoints become `HttpHandler`s that compose into the
  `choose` you already have; your pipeline and existing routes do not change,
  and adoption is one endpoint at a time.
- **"I want typed endpoints without Giraffe or any other F# web framework."**
  → `Etymon.Api` + `Etymon.Api.AspNetCore`. ASP.NET Core is the floor — Etymon
  is not a web server and will not become one.
- **"My events are the schema, and my tables never change."** → `Etymon.Schema.Compatibility`, policy `Events`.
  It compares event shapes across versions and refuses changes that would strand
  events already written. It never opens a connection and never rewrites an event.
- **"I need to know if changing this DTO breaks a client."** →
  `Etymon.Schema.Compatibility`, policy `Wire`. Requests can be rescued with an
  upcaster; response breaks cannot be fixed by anything you ship, and it says so
  before the change goes out.
- **"I only want to call someone else's API, or generate types for a frontend."**
  → `Etymon.Api` + `Etymon.Api.Client`, or `Etymon.Schema.TypeScript`. Neither
  needs a server at all.
- **"I want all of it."** → `Etymon`, plus an adapter if you are writing a
  server. The meta-package carries only what costs nothing but FSharp.Core, so
  three packages are deliberately left out: `Etymon.Api.Giraffe` (Giraffe),
  `Etymon.Api.AspNetCore` (ASP.NET Core) and `Etymon.Invariants.FsCheck`
  (FsCheck). A convenience that quietly adds a web framework to a console
  application is not one — and the two adapters are mutually exclusive anyway,
  so no version of the meta-package could include the right one.

## How this compares

Every package here has a specialist that beats it at its own job. That is worth
saying plainly, because the suite is not a collection of best-in-class parts —
it is one declaration reaching several places, and if you only need one of those
places you should probably use the specialist.

**JSON codecs — [Thoth.Json], [Chiron], [Fleece], [FSharp.SystemTextJson].**
Thoth's decoder combinators look a great deal like `Schema.object`, and nobody
should switch libraries for the codec alone. The one advantage independent of
the rest of Etymon is that a `Schema<'T>` is a single value rather than a
separate encoder and decoder that can disagree. Everything else — the OpenAPI
document, the SQL, the generators — is the actual reason.

**Validation — [Validus], [FsToolkit.ErrorHandling].** Both accumulate errors,
and FsToolkit is the more general library. `Etymon.Core` differs in two ways:
error paths are nested (`items[3].address.zip`) rather than flat field names,
and constraints are *data* that five other packages read. If you want
accumulating validation and nothing else, FsToolkit is a smaller dependency.

**Migrations — [EF Core].** EF Core is substantially more capable: relationships,
a migrations history table, `dotnet ef` tooling, a provider ecosystem.
**If EF owns your schema, use EF migrations.** Having two things generate DDL
means two sources of truth, which is exactly the drift Etymon exists to prevent.
`Etymon.Schema.Sql` is for stacks without an ORM — Dapper, raw ADO.NET,
F#-first — where the alternative is hand-writing DDL. It works on immutable F#
records rather than mutable entity classes, refuses to guess where EF applies a
convention, and produces SQL you review in a pull request rather than a
generated C# class.

**HTTP APIs — ASP.NET Core's built-in OpenAPI, [Giraffe], [Oxpecker],
[Fable.Remoting].** ASP.NET already generates schemas from your types by
reflecting over them, which is a shorter route to a document if a document is
all you want. Etymon's endpoints are values, so the same declaration also
produces the client and the TypeScript, and the adapters register on routing you
already have — one endpoint at a time, beside whatever else is mapped.
`Etymon.Api.AspNetCore` registers each endpoint with the metadata ASP.NET
already understands — operation id, summary, tags, the request type, and every
status the endpoint declares — so an application that already publishes a
document gets Etymon endpoints in it without describing them a second time. What
does **not** reach that document is the constraints, because ASP.NET builds its
schemas by reflecting over the CLR type and the type does not know them:
`ApiOpenApi` writes a document that carries them, and joining the two needs a
document transformer and the OpenAPI package this adapter deliberately does not
take. Fable.Remoting solves a different problem — RPC over shared F# types for a
Fable client, with no routes or verbs — so the overlap is smaller than it looks.

**Configuration — [FsConfig].** Etymon reports every problem at once, with what
was expected and which source supplied it. FsConfig stops at the first.

**TypeScript — [NSwag], [openapi-typescript].** Those go through an OpenAPI
document; Etymon emits from the schema directly, skipping the round trip. A
modest difference.

**Generators — plain FsCheck.** `Etymon.Invariants.FsCheck` generates only
values your declared constraints admit, which is the part people otherwise
hand-roll. It is a thin package and nobody should adopt the suite for it.

[Thoth.Json]: https://github.com/thoth-org/Thoth.Json
[Chiron]: https://github.com/xyncro/chiron
[Fleece]: https://github.com/fsprojects/Fleece
[FSharp.SystemTextJson]: https://github.com/Tarmil/FSharp.SystemTextJson
[Validus]: https://github.com/pimbrouwers/Validus
[FsToolkit.ErrorHandling]: https://github.com/demystifyfp/FsToolkit.ErrorHandling
[EF Core]: https://learn.microsoft.com/ef/core/
[Giraffe]: https://github.com/giraffe-fsharp/Giraffe
[Oxpecker]: https://github.com/Lanayx/Oxpecker
[Fable.Remoting]: https://github.com/Zaid-Ajaj/Fable.Remoting
[FsConfig]: https://github.com/demystifyfp/FsConfig
[NSwag]: https://github.com/RicoSuter/NSwag
[openapi-typescript]: https://github.com/openapi-ts/openapi-typescript

## What it costs

A schema is slower than a codec you wrote by hand, and pretending otherwise
would be the same kind of claim as the two stale numbers this README used to
carry. Decoding an eight-field record, on one machine, with
`bench/Etymon.Benchmarks`:

| | Time | Allocated | Validates |
| --- | ---: | ---: | :---: |
| Hand-written `JsonDocument` loop | 557 ns | 336 B | no |
| `System.Text.Json` (reflection) | 389 ns | 400 B | no |
| **Etymon** | **1,606 ns** | **1,344 B** | **yes** |

About three times the time and four times the allocation of a hand-written
codec — and the hand-written codec checks nothing. Etymon is also applying an
email format, two length bounds and a range in that measurement, and producing
every failure with a path rather than the first one.

Reproduce it yourself, which is the point of quoting it at all:

```bash
dotnet run --project bench/Etymon.Benchmarks -c Release -- --filter '*Decoding*'
```

**What has been done about it.** Paths are the machinery that turns a failure
into `lines[2].quantity`, and a naive implementation builds them while
descending into the value — paying on every successful field read for an error
that never happens. Etymon builds them as an error unwinds instead, so a
successful decode allocates nothing to describe a problem it did not find. That,
plus making `Path` a struct, took decoding from 1,820 ns and 1,984 B to the
figures above: **12% faster, 32% less allocated**, with no API change.

**What has not.** The remainder is the applicative itself — a `Result` and a
tuple per field, which is what buys error accumulation. Removing it would mean
stopping at the first bad field, and that trade is the wrong way round for the
problem this suite exists to solve.

## Input somebody else chose

If Etymon validates your API's requests, an attacker picks its input. Two
properties follow, and both are tested rather than asserted.

**Rejection is linear in the input.** Accumulating errors used to join a growing
list per element, which is quadratic: 200,000 bad array elements — an 800 KB
body — took **five minutes of CPU**, so anyone able to post to an endpoint could
take a core off it. The error collection is now a concatenation tree, joined in
constant time and flattened once. The same input takes **0.3 seconds**, and a
test fails if it goes back to quadratic.

**Deep nesting is refused, not crashed.** A recursive decoder walking a
deliberately deep value is how a stack overflow happens, and a stack overflow
cannot be caught — it takes the process down. `System.Text.Json` stops at depth
64 first, and that is where the limit belongs.

**What is still yours to bound.** Reporting every problem means a request with
10,000 bad elements produces 10,000 errors: about 5 ms and 5.9 MB. That is
linear and honest, but it is still roughly 150 times the size of the body that
caused it. If your input is attacker-controlled and unbounded, cap the response
at your boundary — `ValidationErrors.count` and `toList` are there for it.
Etymon will not silently truncate, because reporting every problem at once is
the thing it is for.

## Design commitments

These are promises the build enforces, not just documents.

- **Expected failures are values.** Anything that can fail for an ordinary reason
  returns `Validation<'T>`. Exceptions are for bugs.
- **Errors accumulate.** Four bad fields produce four errors, each with a path
  like `address.zip` or `items[3].name`. The only combinator that stops early is
  `bind`, because a dependent step has no value to work with.
- **Explicit beats derived.** Combinators are the primary path: no reflection,
  trimming- and AOT-friendly. `Schema.auto` will be a labelled convenience,
  marked `RequiresUnreferencedCode`, never the default.
- **Constraints are data.** One vocabulary in Core, read by every package that
  derives anything. A rule Etymon cannot inspect is `Opaque`: still enforced,
  still documented, never silently half-derived.
- **Ambiguity is an error, not a guess.** Where a derivation has to choose —
  most sharply in `Etymon.Schema.Sql`, where a nested record could be a foreign
  key, flattened columns or `jsonb` — Etymon refuses and asks rather than
  picking a default you would discover in production.
- **The dependency graph is checked by the build.** `Etymon.Core` referencing a
  sibling, or a shipping package picking up a test-only dependency, is a build
  error (`ETY0001`–`ETY0003`), not a code review note.

## Getting started

The fastest way to see the point is to run the sample: one `Booking` schema, then
the JSON codec, the validation errors, the OpenAPI document, the TypeScript
declarations, the PostgreSQL table and the configuration reader — none of which
restate a single rule.

```bash
dotnet run --project samples/Etymon.Sample.Derivations
```

To use it, take the meta-package or just the part you need. Previews are not
restored unless you ask for the version, so name it:

```xml
<PackageReference Include="Etymon" Version="0.1.0-preview.6" />
```

To build the repository instead:

```bash
dotnet tool restore
dotnet build
```

To run the tests — Expecto's own runner, not `dotnet test`; see
`tests/Directory.Build.props` for why:

```bash
dotnet run --project tests/Etymon.Core.Tests
```

To consume it from another solution before anything is published, pack it into a
local feed:

```bash
./eng/pack-local.ps1
```

## Documentation

- [**Why Etymon**](docs/why-etymon.md) — the same code without the library and
  with it, for every derivation, with the costs and the cases where it is the
  wrong choice.
- [**Glossary**](docs/glossary.md) — one definition per term. Schema against database
  schema, migration against runner, backward against forward. Read this before
  arguing about a design.
- [Guides](docs/guides.md) — describing a type, constraints as data, cross-field
  rules, endpoints as values, migrations, configuration.
- [Decisions](docs/decisions.md) — why things are the way they are, and what was
  ruled out.
- [Changelog](CHANGELOG.md) — every breaking change, per version, at the top.

The API reference is generated from XML doc comments with
[fsdocs](https://fsprojects.github.io/FSharp.Formatting/); every public function
in every shipping package has one.

## Keeping up

**Etymon tracks .NET.** Every package targets `net8.0` and `net10.0`, and new
targets are added as .NET ships them. `FSharp.Core` is pinned at the lowest
version asked of consumers and compiled against it, so the library cannot
accidentally use an API newer than its own floor. Every test runs on both
frameworks on Windows, macOS and Linux on every commit — twice already a bug
existed on exactly one of those and nowhere else.

**It is built against a real application.** Etymon is developed alongside Keel,
an accounting and payroll platform being built for production, which is its
first and largest consumer and is adopting the suite across its contract and
HTTP layers. Several things here exist because that consumer needed them, and
several bugs were found the same way — including a dependency that would have
put file-IO code into a browser payload, and a missing error-code mechanism that
would have made adoption a breaking change for Keel's own integrators.

## Releasing

See [RELEASING.md](RELEASING.md). Packages go to nuget.org as previews; a
release is a tag, and the push sits behind a GitHub environment.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Issues and pull requests are welcome,
particularly for the packages that have not been built yet — the design is still
cheap to change.

## Licence

[MIT](LICENSE).
