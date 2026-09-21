# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Every package in the suite ships the same version number while the suite is 0.x,
so "which versions go together" is never a question. Independent versioning will
be reconsidered at 1.0.

While the suite is in preview the API can change between previews, and it does.
Each entry below says what breaks and what to do about it, because a preview
that moves quietly is worse than one that moves.

## [Unreleased]

### Changed -- breaking

- **`Etymon.Migrations` is now `Etymon.Schema.Sql`.** The package maps a Schema to
  a table model, compares two models and emits SQL text. It renders; it does not
  migrate, and its README had to say so in its first paragraph -- a name that
  needs correcting in its opening sentence is a name doing negative work. It now
  sits in the pattern that already says what it is: `Schema.OpenApi` renders a
  document, `Schema.TypeScript` renders types, `Schema.Sql` renders SQL. Nobody
  expects `Schema.OpenApi` to serve a document, so nobody will expect this one to
  touch a database.

  Module names are unchanged: `Migrations.between` is still `Migrations.between`,
  and "migration" remains the word for the output. You write migrations; this
  package drafts them. Update the package reference and the namespace is the
  same.

### Added

- **`Etymon.Schema.Events`.** In an event-sourced system the tables barely
  change; what changes is what is written inside the payload, and no migration
  tool sees it because no column moved. This derives an event shape from a
  Schema, commits the set as a snapshot, and compares two snapshots for
  compatibility in both directions -- can new code read events already written,
  and can already-deployed code read events written by new code.

  It never emits SQL, never opens a connection, and never proposes rewriting an
  event. Not behind a flag: the events already written are the one irreplaceable
  thing in the system.

  Renames are declared rather than detected, because a rename is
  indistinguishable from a removal plus an addition and the difference decides
  whether stored events can be read. Upcaster coverage is checked by graph
  reachability and the gap is named by version.

- **`ConstraintCodec`** in `Etymon.Schema`: the constraint vocabulary as a
  Schema. Both snapshot formats record constraints, and two hand-written copies
  of one format is the drift this suite exists to prevent.

- **`docs/glossary.md`.** Schema against database schema, migration against
  runner, backward against forward. Written because a design discussion went
  round in circles for six exchanges before anyone noticed that two words each
  meant two things.

### Fixed

- **A changed `CHECK` produced no diff and no warning.** The generated migration
  was silently incomplete: the script looked finished, the reviewer approved it,
  and the database went on enforcing the old rule. Constraint differences are now
  reported as a change, refused in writing in the script (`-- NOT GENERATED:`)
  and listed in `Unsupported` so a build can fail on them. Still not expressible
  as SQL -- but a refusal is worse than a correct answer and far better than
  silence.

## [0.1.0-preview.3]

### Changed — breaking

- **`Schema.bimap` is now `Schema.convert`.** In every other library `bimap`
  maps the two sides of a two-parameter type, such as the success and the
  failure of a result. This one maps a type to another type and back, which is a
  different operation wearing a familiar name. Rename at the call site; the
  signature is unchanged.
- **`ValidationErrors` compares by its contents rather than structurally.** The
  type is now a concatenation tree, so two collections holding the same errors
  can have different shapes; equality is implemented to ignore that. Nothing a
  caller does changes, but the type no longer supports comparison (`<`, `>`),
  which it never meaningfully did.

### Added

- **`Check.withCode` and `Check.saying`.** A check can report a stable error code
  and its own wording instead of the constraint it came from. For adopting
  Etymon behind an API whose callers already branch on error codes: without it,
  moving a rule out of hand-written code and into a schema changes what the API
  returns, which is enough of a reason not to do it. The constraint is still
  carried, so the OpenAPI document, the generated SQL and the generators are
  unaffected.
- **`Parse.byteIn`, `Parse.timeOnlyIn` and `Parse.timeSpanIn`.** Every other
  numeric and temporal type had a culture-taking variant and these three did not.
- **`Etymon.Api.AspNetCore` registers endpoints with ASP.NET's own metadata** —
  operation id, summary, tags, the request type, every declared status, and the
  422 the adapter produces itself for a body the schema refuses. An application
  that already publishes an OpenAPI document now gets Etymon endpoints in it
  without describing them a second time. The constraints still do not reach that
  document, because ASP.NET builds its schemas by reflecting over the CLR type;
  `ApiOpenApi` writes one that carries them.
- **`bench/Etymon.Benchmarks`**, and `eng/trim-size.sh`, so the numbers in the
  README are reproducible rather than asserted.

### Fixed

- **Rejecting a large input was quadratic.** Accumulation joins error
  collections, and `ValidationErrors` was a list, so joining copied the left
  side. 200,000 bad array elements — an 800 KB request body — took five minutes
  of CPU, which made it a denial of service in the part of the library that
  exists to protect the application. It is now a concatenation tree: the same
  input takes 0.3 seconds, and a test fails if it goes back.
- **Decoding built a path for every field it read successfully.** Paths are the
  machinery that turns a failure into `lines[2].quantity`; they are now assembled
  as an error unwinds rather than while descending into the value, so a
  successful decode allocates nothing to describe a problem it did not find.
  With `Path` becoming a struct: 12% faster, 32% less allocated.
- **Encoding copied the payload three times**, through a `MemoryStream` and
  `ToArray`. `Utf8JsonWriter` takes an `IBufferWriter` directly. A third less
  allocated, same bytes out.
- **`Generate.valid`'s refusal of recursive schemas told callers to "supply one
  explicitly for this type"**, which reads as though there is somewhere to supply
  it. There is not. The message now says what a caller can actually do.

### Known limitations

- `Generate.valid` still cannot generate values for a recursive schema. A
  depth-budgeted generator is the fix and is not built; a half-built one that
  emitted subtly invalid values would be worse than an honest refusal.
- Reporting every problem at once means a request with 10,000 bad elements
  produces 10,000 errors, roughly 150 times the size of the body that caused it.
  That is the promise, so the cap belongs at the application's boundary.

## [0.1.0-preview.2]

### Changed — breaking

- **`Parse` moved from `Etymon.Base` to `Etymon.Core`,** and **`Etymon.Schema`
  no longer depends on `Etymon.Base`.** Schema used nine things from Base, all of
  them `Parse`, and took `Files` and `Env` along with them — file handles in a
  Blazor WebAssembly payload that can never call them. Both files keep
  `namespace Etymon`, so `Parse.int` is still `Parse.int` and no source changes;
  binary-breaking only. `Etymon.Base` now depends on `Etymon.Core`, which it did
  not before.
- **`Check.format Format.Uri` rejects values it used to accept.** `Uri.TryCreate`
  with `UriKind.Absolute` adopts a rooted path as an implicit `file:` URI, so
  `/relative` validated on Linux and was rejected on Windows, and `C:\Windows`
  did the reverse. The scheme the parser settles on must now be the scheme the
  string actually names. `urn:`, `mailto:` and an explicit `file:` are unaffected.
- **`File` operations report `FileError.InUse` on Linux and macOS** where they
  reported `IoFailure`. .NET does enforce `FileShare` on Unix, emulating it with
  an advisory lock, but reports the platform's `EAGAIN` rather than a Win32
  sharing violation. Code branching on `InUse` to retry now retries everywhere.

### Added

- The `Etymon` meta-package, `samples/Etymon.Sample.Derivations`, and
  `eng/test-linux.sh`.

## [0.1.0-preview.1]

The first public preview: fourteen packages, published together.

### Added

- **Etymon.Core** — `Path`, the `Constraint` vocabulary as data, the accumulating
  error model (`ValidationError`, `ValidationErrors`, `ErrorReason`),
  `Validation<'T>` with a `validation { let! … and! … }` expression,
  `ConstraintCheck` and `Check`, `Secret<'T>`, and `Refinement` with `Refine`.
- **Etymon.Base** — `Str`, `Dict`, `Env`, `File`, `Dir` and `FileError`:
  `option` and `Result` where the base library returns `null` and throws.
- **Etymon.Schema** — `Schema<'T>`, a typed codec paired with an untyped
  description of itself, built with an applicative object expression that has no
  `Bind`, so a field cannot depend on another field's value and every field's
  errors accumulate.
- **Etymon.Schema.OpenApi** — JSON Schema 2020-12 and OpenAPI 3.1 components.
- **Etymon.Schema.TypeScript** — TypeScript declarations, types only.
- **Etymon.Invariants** and **Etymon.Invariants.FsCheck** — rules no single field
  can hold, and generators that produce only values a schema accepts.
- **Etymon.Config** — configuration through a schema, every problem at once, with
  the source each value came from.
- **Etymon.Schema.Sql** — a relational model derived from a schema, snapshot
  diffing and SQL, carrying no database driver.
- **Etymon.Api**, **Etymon.Api.Giraffe**, **Etymon.Api.AspNetCore** and
  **Etymon.Api.Client** — endpoints as values, with adapters that interpret them.
- Repository scaffolding: central package management, deterministic builds,
  embedded symbols, SourceLink, Fantomas, and dependency rules enforced as build
  errors (`ETY0001`–`ETY0003`).

[Unreleased]: https://github.com/CaelusMinds/Etymon/compare/v0.1.0-preview.3...HEAD
[0.1.0-preview.3]: https://github.com/CaelusMinds/Etymon/releases/tag/v0.1.0-preview.3
[0.1.0-preview.2]: https://github.com/CaelusMinds/Etymon/releases/tag/v0.1.0-preview.2
[0.1.0-preview.1]: https://github.com/CaelusMinds/Etymon/releases/tag/v0.1.0-preview.1
