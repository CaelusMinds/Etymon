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

> **Status: pre-release.** Eleven of the thirteen packages are built and tested —
> **691 tests**, each run on both net8.0 and net10.0. Still to come:
> `Etymon.Api.AspNetCore`, `Etymon.Api.Client`, the meta-package, the samples and
> the documentation site. Nothing is on NuGet yet.

## Packages

Install only what you need, or take the `Etymon` meta-package for all of it.

| Package | What it owns | Depends on |
| --- | --- | --- |
| **Etymon.Core** | Paths, the constraint vocabulary, the accumulating error model, refined types, `Secret<'T>` | FSharp.Core only |
| **Etymon.Base** | The .NET base library, returning `option` and `Result` instead of `null` and exceptions | FSharp.Core only |
| **Etymon.Schema** | `Schema<'T>` — one value describing encode, decode, validate and document | Core, Base |
| **Etymon.Schema.OpenApi** | JSON Schema 2020-12 and OpenAPI 3.1 component schemas | Core, Schema |
| **Etymon.Schema.TypeScript** | TypeScript declarations from the same schemas. Types only | Core, Schema |
| **Etymon.Invariants** | Rules that must always be true of a type, including the cross-field ones no single field can hold | Core |
| **Etymon.Invariants.FsCheck** | Generators that produce only values a schema accepts, plus ones it should reject | Core, Schema, Invariants, FsCheck |
| **Etymon.Config** | Configuration through a schema, reporting every problem at once with what was expected and which source supplied it | Core, Base, Schema |
| **Etymon.Migrations** | A relational model derived from a schema, snapshot diffing and SQL generation. Carries no database driver | Core, Schema |
| **Etymon.Api** | HTTP endpoints described once: method, typed route, request, responses, typed failures. Performs no HTTP | Core, Base, Schema, Schema.OpenApi |
| **Etymon.Api.Giraffe** | Serves an endpoint as a Giraffe `HttpHandler`, composing into the routing you already have | Api, Giraffe |
| **Etymon.Api.AspNetCore** | The server adapter, feeding ASP.NET Core's own OpenAPI pipeline rather than replacing it | Api |
| **Etymon.Api.Client** | A typed `HttpClient` client | Api |
| **Etymon** | Meta-package. No code; references everything except the ASP.NET adapter | — |

## Which package do I need?

- **"I want smart constructors and validation errors with field paths."**
  → `Etymon.Core`. Nothing else. It has no dependency beyond FSharp.Core and
  trims to about 47 KB, so it can sit in a Blazor WebAssembly domain layer.
- **"I hand-write JSON codecs and want the error vocabulary without the magic."**
  → `Etymon.Core`. The error model deliberately does not depend on
  `Etymon.Schema`, so you can take `Path` / `ValidationError` /
  `ValidationErrors` and keep your codecs exactly as they are.
- **"I want JSON and validation from one definition."** → `Etymon.Schema`.
- **"…and an OpenAPI document that cannot drift from it."** → add `Etymon.Schema.OpenApi`.
- **"…and property tests that only ever generate values my schema accepts."**
  → add `Etymon.Invariants.FsCheck`.
- **"I have a rule that no single field can hold — an end date after a start
  date."** → `Etymon.Invariants`.
- **"…and a database schema, reviewable in a pull request."** → add
  `Etymon.Migrations` — but read the note about EF Core below first.
- **"I already use Giraffe and want to keep it."** → `Etymon.Api` +
  `Etymon.Api.Giraffe`. Endpoints become `HttpHandler`s that compose into the
  `choose` you already have; your pipeline and existing routes do not change,
  and adoption is one endpoint at a time.
- **"I want typed endpoints without Giraffe or any other F# web framework."**
  → `Etymon.Api` + `Etymon.Api.AspNetCore`. ASP.NET Core is the floor — Etymon
  is not a web server and will not become one.
- **"I only want to call someone else's API, or generate types for a frontend."**
  → `Etymon.Api` + `Etymon.Api.Client`, or `Etymon.Schema.TypeScript`. Neither
  needs a server at all.
- **"I want all of it."** → `Etymon`, plus an adapter if you are writing a
  server. The meta-package leaves the ASP.NET adapter out on purpose: a
  framework reference is viral, and it would break every console app and library
  that installed the meta-package.

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
`Etymon.Migrations` is for stacks without an ORM — Dapper, raw ADO.NET,
F#-first — where the alternative is hand-writing DDL. It works on immutable F#
records rather than mutable entity classes, refuses to guess where EF applies a
convention, and produces SQL you review in a pull request rather than a
generated C# class.

**HTTP APIs — ASP.NET Core's built-in OpenAPI, [Giraffe], [Oxpecker],
[Fable.Remoting].** ASP.NET already generates schemas from your types.
`Etymon.Api.AspNetCore` is therefore planned to *feed* that pipeline rather than
replace it, so it can be adopted one endpoint at a time. Fable.Remoting solves a
different problem — RPC over shared F# types for a Fable client, with no routes
or verbs — so the overlap is smaller than it looks.

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
  most sharply in `Etymon.Migrations`, where a nested record could be a foreign
  key, flattened columns or `jsonb` — Etymon refuses and asks rather than
  picking a default you would discover in production.
- **The dependency graph is checked by the build.** `Etymon.Core` referencing a
  sibling, or a shipping package picking up a test-only dependency, is a build
  error (`ETY0001`–`ETY0003`), not a code review note.

## Getting started

Etymon is not on NuGet yet. To build it:

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

Guides live in [`docs/`](docs/) and the API reference is generated from XML doc
comments with [fsdocs](https://fsprojects.github.io/FSharp.Formatting/).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Issues and pull requests are welcome,
particularly for the packages that have not been built yet — the design is still
cheap to change.

## Licence

[MIT](LICENSE).
