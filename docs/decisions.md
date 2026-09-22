# Decisions

Settled. Recorded here so they are not re-argued, and so the reasoning survives
the person who had it.

> Every entry below is a decision already enforced somewhere on disk — in a build
> file, the `.editorconfig` or `.gitattributes`. Something argued but not yet
> built is not a decision yet, and does not belong here.

## Shape

An F# library suite, published as NuGet packages under MIT, one package per
folder under `src/`. Folder name, project name, package id and root namespace are
the same string, so there is nothing to look up.

`Etymon.Core` is the foundation and **depends on nothing but `FSharp.Core`**.
That is the package's reason to exist, not a preference: anything that has to be
true everywhere — a path into a value, a constraint, an accumulated error, a
refined type — must be takeable without taking anything else with it.

Rules live as data, once. A `Refinement` carries its `Constraints` alongside its
`Convert` and `Checks`, so `Etymon.Schema` and everything after it — JSON Schema,
OpenAPI, SQL, generators — derives its output from the same declaration instead
of restating the rule in its own vocabulary. A rule restated in a second place is
a rule that will eventually disagree with itself. The cost is a constraint case
per rule; the alternative is a suite that lies about what it enforces.

## Errors accumulate

`Convert` runs first and a failure there stops everything, because there is no
value left to check. Every check then runs, so a value that breaks three rules
reports three errors. Returning the first failure and stopping was rejected: the
consumers of this suite are forms, request bodies and imported files, where one
error at a time is the wrong shape of answer.

`ErrorReason` is what a program branches on and `Message` is what a human reads,
deliberately separated so a caller can re-word an error without parsing one.

## Secrets redact on the type

Redaction that only works inside one library's error messages is theatre — the
first `printfn "%A" config` undoes it. So `Secret<'T>` overrides `ToString` and
`StructuredFormatDisplay`, which covers `%O`, `%A`, `string` and most structured
loggers. `Secret.reveal` is the single greppable way out, so "where does this
secret escape?" is a search rather than an audit. Two secrets compare equal when
their values do, so records holding one stay usable in tests. Serialising a
`Secret` is deliberately **not** defined in `Etymon.Core`; what a sensitive field
does on the wire is `Etymon.Schema`'s decision.

## Targets and the dependency floor

`net8.0` and `net10.0`, both real, both built. `FSharp.Core 8.0.100` is the floor
we ask consumers to accept, and we compile against exactly that version so an API
newer than our own declared minimum cannot be reached by accident. Raising the
floor breaks somebody, so it moves only with a stated reason in `CHANGELOG.md`.

`System.Text.Json` ships in the shared framework for both targets, so
`Etymon.Schema` takes it as an in-box API and it is absent from
`Directory.Packages.props` on purpose. A shipping package takes **at most one**
runtime dependency, declared centrally with a comment saying why.

Versions are lockstep across the suite for 0.x: one `VersionPrefix` at the root,
so "which versions go together" is never a question a consumer has to ask.

## Trimmable, and small enough to download

Everything in `src/` declares `IsAotCompatible`. `Etymon.Core` in particular has
to travel into a Blazor WebAssembly payload, where a referenced assembly is bytes
a browser downloads over a connection we do not control. A package that genuinely
needs reflection opts out on its own project and says why; the default is not
negotiable.

Builds are deterministic with SourceLink and embedded symbols, set once at the
root. `IncludeSymbols` is false because the symbols are already in the assembly —
a separate `.snupkg` would be a second thing to publish and keep in step.

## Warnings are errors

`TreatWarningsAsErrors`, plus `FS0025`, `FS0040`, `FS0064` and `FS0193` listed
again in `WarningsAsErrors` so that relaxing the first locally does not silence
them. `src/` additionally warns on `1182` unused binding and `3390` malformed
doc comment, because a library that ships documentation should not ship a broken
`<summary>`. There is no suppression attribute to add.

Everything public carries an XML doc comment. `GenerateDocumentationFile` is on
in `src/` and off in `tests/` and `samples/`.

## Tests

Expecto, with FsCheck for laws and Verify for shapes. `tests/` mirrors `src/`
one-for-one and is never referenced by a shipping project; test-only packages
stay in their own group in `Directory.Packages.props`. Every feature and bug fix
ships tests in the same change.

`.verified.*` snapshots are forced to LF in `.gitattributes` because they have to
be byte-identical across operating systems, and `*.received.*` is gitignored and
marked binary so it cannot be committed to settle a diff.

## Formatting

Fantomas, configured in `.editorconfig`, with a deliberately short override list
— six settings and a line length. Every override is a rule a contributor has to
learn, so the bar for adding one is a problem the default actually causes, not a
disagreement with it. LF everywhere except `.cmd`.

## Layout

Three `Directory.Build.props` files, each importing the one above: the root owns
identity, version, targets, warnings and output; `src/` adds packability,
documentation, trimmability and package metadata; `tests/` and `samples/` turn
those back off. A property true of every project lives at the root and nowhere
else. All output goes to `artifacts/`, packages to `artifacts/packages`.

## Signature files are generated, not written

Every shipping package carries a `Surface.fsi`: its public surface as the
compiler infers it, with every value's full signature and documentation. It
exists because XML documentation has nowhere to put a return type, so a reader
working from the package on disk was learning what a function hands back by
compiling something and reading the error — four such cycles in one adoption,
for information the package already knew.

It is written by the build, from the implementation, and committed. Not
hand-written, which was the suggested form. A hand-written signature file is a
contract the compiler enforces, and that has a real benefit — the surface is
declared, and nothing becomes public by accident — and a real cost: fifteen
packages of signatures kept in step with the code, and a surface frozen until
somebody edits two files. The suite already controls visibility explicitly with
`private` and `[<RequireQualifiedAccess>]`, so the enforcement would buy little.
What a signature file is actually for — seeing the surface, and seeing it
change — the generated one gives: it cannot drift, and because it is committed,
a change to the public surface is a diff in review. CI fails when the committed
copy is stale.

Ruled out: stating the return type in each `<summary>` by hand. It answers the
question one function at a time and drifts the moment a signature changes.

## Open

| Question | State |
| --- | --- |
| `RepositoryOwner`, and therefore the repository URL and SourceLink | `CaelusMinds` in the root `Directory.Build.props`. One string, one place. |
| A solution file | None. Every command names a project by path until there is one. |
| `CHANGELOG.md` | Does not exist, and two rules above already depend on it. |
| CI | `.github/workflows/` is empty. `ContinuousIntegrationBuild` already keys off `CI=true`. |
| The documentation site | `fsdocs-tool` is pinned; `docs/` has no fsdocs input yet. |
| Which packages follow `Etymon.Schema` | Named in doc comments and package tags: JSON Schema, OpenAPI, SQL, generators. No boundaries drawn. |

## What Etymon will not take, and why

Recorded so that good work is not folded in by accident. Both of the following
came out of building an event-sourced accounting product alongside this suite;
both are genuinely missing from the F# ecosystem, and neither belongs here.

**A transactional outbox** — delivery policy, backoff, the ambiguous-send case,
claim-by-lease.

**A recurrence and cadence model** — recurrence shapes as a union with correct
daylight-saving handling: clocks-forward runs at the first valid instant,
clocks-back runs once, because the occurrence key is wall-clock time.

Both are runtime machinery. Neither derives anything from a type, so both break
the thesis this suite is built on — *define the type once, derive everything
else* — and admitting either would change what Etymon is. They belong in a
general platform library.

The only derivation angle on cadence is rendering a recurrence as a cron string
or as English ("every second Tuesday"), and that is too thin to justify a
package.

## Etymon.Core and Etymon.Base do not distinguish themselves by name

**Open, not settled.** A reader cannot guess which of the two holds what, which
is the same defect as a package named for something it does not do — just
cheaper, because nothing outside the suite depends on knowing.

Today the split is: `Etymon.Core` holds what the rest of the suite derives from
(paths, the constraint vocabulary, the error model, refined types, parsing);
`Etymon.Base` holds an F#-idiomatic layer over the .NET base library (strings,
dictionaries, environment variables, file IO) that nothing else in the suite
depends on. That is a real distinction and the names do not carry it.

The options are to merge them, or to rename so the split is legible. Neither has
been done, because the rename that would make it legible has not been found.
This is recorded so that it is a known debt rather than a thing each new reader
rediscovers.
