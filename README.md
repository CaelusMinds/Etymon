# Etymon

> *etymon* (n.) — the root word from which later forms derive.

**Define the type once. Derive everything else.**

F# developers hand-write JSON codecs, validation rules, OpenAPI schemas, test
generators and database migrations separately, then keep them in sync by
remembering to. Etymon makes one schema definition the source of truth and
derives the rest from it.

```fsharp
let personSchema =
    Schema.object "Person" {
        let! name = Schema.required "name" NonEmpty.schema (fun p -> p.Name)
        and! age = Schema.required "age" ageSchema (fun p -> p.Age)
        and! email = Schema.optional "email" Email.schema (fun p -> p.Email)
        return { Name = name; Age = age; Email = email }
    }

personSchema |> Schema.toJson person          // encode
personSchema |> Schema.fromJson raw           // decode, with every error at once
personSchema |> OpenApi.toJsonSchema          // JSON Schema 2020-12
personSchema |> Arb.valid                     // an FsCheck generator of valid values
personSchema |> Migrations.tableOf options    // a relational table model
```

> **Status: pre-release.** Five of nine phases are complete: `Etymon.Core`,
> `Etymon.Base`, `Etymon.Schema`, `Etymon.Schema.OpenApi`, `Etymon.Invariants`
> and `Etymon.Invariants.FsCheck` are built and tested (594 tests, net8.0 and
> net10.0). Everything else in the table below is planned, not shipped. Nothing
> is on NuGet yet.

## Packages

Install only what you need, or take the `Etymon` meta-package for all of it.

| Package | What it owns | Depends on |
| --- | --- | --- |
| **Etymon.Core** | Paths, the constraint vocabulary, the accumulating error model, refined types, `Secret<'T>` | FSharp.Core only |
| **Etymon.Base** | The .NET base library, returning `option` and `Result` instead of `null` and exceptions | FSharp.Core only |
| **Etymon.Schema** | `Schema<'T>` — one value describing encode, decode, validate and document. JSON via `System.Text.Json` | Core |
| **Etymon.Schema.OpenApi** | JSON Schema 2020-12 and OpenAPI 3.1 component schemas | Core, Schema |
| **Etymon.Schema.TypeScript** | TypeScript type declarations from the same schemas | Core, Schema |
| **Etymon.Invariants** | Rules that must always be true of a type, including the cross-field ones no single field can hold | Core |
| **Etymon.Invariants.FsCheck** | Generators that produce only values a schema accepts, plus ones it should reject | Core, Schema, Invariants, FsCheck |
| **Etymon.Config** | Configuration through a schema, reporting every problem at once with what was expected and which source supplied it | Core, Base, Schema |
| **Etymon.Migrations** | A relational model derived from a schema, snapshot diffing, and SQL generation | Core, Schema, Invariants |
| **Etymon.Api** | HTTP endpoints defined once: routes, requests, responses, typed errors | Core, Schema, Schema.OpenApi |
| **Etymon.Api.AspNetCore** | The server adapter | Api |
| **Etymon.Api.Client** | A typed `HttpClient` client | Api |
| **Etymon** | Meta-package. No code; references everything except the ASP.NET adapter | — |

## Which package do I need?

- **"I want smart constructors and validation errors with field paths."**
  → `Etymon.Core`. Nothing else. It has no dependency beyond FSharp.Core and is
  small enough to sit in a Blazor WebAssembly domain layer.
- **"I hand-write JSON codecs and want the error vocabulary without the magic."**
  → `Etymon.Core`. The error model deliberately does not depend on `Etymon.Schema`,
  so you can take `Path` / `ValidationError` / `ValidationErrors` and keep your
  codecs exactly as they are.
- **"I want JSON and validation from one definition."** → `Etymon.Schema`.
- **"…and an OpenAPI document that cannot drift from it."** → add `Etymon.Schema.OpenApi`.
- **"…and property tests that only ever generate values my schema accepts."**
  → add `Etymon.Invariants.FsCheck`.
- **"I have a rule that no single field can hold — an end date after a start
  date."** → `Etymon.Invariants`.
- **"…and a database schema, reviewable in a pull request."** → add `Etymon.Migrations`.
- **"I want all of it."** → `Etymon`, plus `Etymon.Api.AspNetCore` if you are
  writing a server. The meta-package leaves the ASP.NET adapter out on purpose:
  a framework reference is viral, and it would break every console app and
  library that installed the meta-package.

## Design commitments

These are the promises the build enforces, not just documents.

- **Expected failures are values.** Anything that can fail for an ordinary reason
  returns `Validation<'T>`. Exceptions are for bugs.
- **Errors accumulate.** Four bad fields produce four errors, each with a path
  like `address.zip` or `items[3].name`. The only combinator that stops early is
  `bind`, because a dependent step has no value to work with.
- **Explicit beats derived.** Combinators are the primary path: no reflection,
  trimming- and AOT-friendly. `Schema.auto` exists as a convenience, is marked
  `RequiresUnreferencedCode`, and is documented as the shortcut it is.
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
dotnet run --project tests/Etymon.Core.Tests
```

Tests run through Expecto's own runner rather than `dotnet test` — see
`tests/Directory.Build.props` for why.

## Documentation

Guides live in [`docs/`](docs/) and the API reference is generated from XML doc
comments with [fsdocs](https://fsprojects.github.io/FSharp.Formatting/).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Issues and pull requests are welcome,
particularly for the packages that have not been built yet — the design is still
cheap to change.

## Licence

[MIT](LICENSE).
