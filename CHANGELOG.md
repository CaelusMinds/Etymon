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

## [0.1.0-preview.17]

One union wire shape, so a product whose stored events carry an internally
tagged union wrote the codec by hand over `Schema.raw`, and the snapshot, the
OpenAPI document and the TypeScript output saw no cases. Action 3 of the
[review of 2026-10-05](docs/review-2026-10-05.md), designed in
[a union says how the union is written](docs/design/union-wire-shape-2026-10-05.md).

### Added

- **`UnionShape`** and **`Schema.unionWith shape name cases`**.
  `UnionShape.AdjacentTag (tag, payloadKey)` is the shape every union has had,
  with the payload key now a parameter; `UnionShape.InternalTag tag` writes
  the tag first and the payload's own fields beside the tag,
  `{ "type": "contractor_bill", "billNumber": "KT-32" }`. Every case of an
  internally tagged union carries an object payload or none, with no field
  named like the tag, and anything else is refused when the union is built;
  a payload held by reference (`Schema.recursive`) cannot be seen then and is
  checked on the first write, which fails naming the union and the case. `Schema.union name tag cases` stays and
  means `Schema.unionWith (UnionShape.AdjacentTag (tag, "value")) name cases`,
  so every existing union keeps the bytes the union has.
- **Every derivation reads the shape.** The OpenAPI document renders an
  internally tagged case as `allOf` of the payload and the tag constant, and in
  the OpenAPI 3.1 dialect adds `discriminator { propertyName; mapping }` when
  every case has a component to map to; the TypeScript output renders the case
  as `({ readonly type: "x" } & Payload)`; the generator in
  `Etymon.Invariants.FsCheck` merges a generated payload beside the tag; the
  compatibility snapshot records the shape on `FieldType.Choice` and reports a
  changed shape as breaking both directions. The snapshot format is version 4;
  version-3 files still read, with the shape not recorded and so never
  compared.

### Fixed

- **A unit case and a raw-payload case were the same description.**
  `SUnion` now carries `SchemaInfo option` per case, `None` for a case with
  no payload.

### Breaking

- `SUnion` is `SUnion of name * shape: UnionShape * cases: (string * SchemaInfo option) list`.
  A match on `SUnion(name, tag, cases)` takes the tag from the shape and
  matches each payload as an option; a wildcard match compiles unchanged.
- `CaseSchema` is `{ Tag; Payload; IsCase; WritePayload; ReadPayload }`: a
  case writes and reads the payload alone, and the union decides where the
  payload sits. Code constructing the record by hand uses `Schema.case` and
  `Schema.caseUnit` instead.
- `FieldType.Choice` gains `encoding: string option`. Code constructing
  `FieldType.Choice` by hand adds the third element, `None` for not recorded.

## [0.1.0-preview.16]

A shape snapshot stopped at a nested name, so a change inside a nested object
or a union case passed every harness. Found by the
[review of 2026-10-05](docs/review-2026-10-05.md) (action 2) and designed in
[nested shapes and union payloads are recorded, not named](docs/design/compatibility-deep-shapes-2026-10-05.md).

### Added

- **`Shape.ofSchemaDeep version schema`**: the root shape and every shape
  reachable from the root, in name order, at one version. A nested object or
  union is recorded under the object's or union's own name, once; a union
  case with an object or union payload under the payload's own name; a union
  case with any other recordable payload under `Union.tag` with one field
  named `$`; a case with no payload records nothing. A reference to a
  recorded union resolves to the union's cases. Two different definitions
  under one name are refused. Harnesses replace a hand-kept list of nested
  shapes with `codecs |> List.collect (Shape.ofSchemaDeep 1)`.
- **A nested object name with no shape is unresolved.** `Events.check`,
  `Wire.unresolvedRequests` and `Wire.unresolvedResponses` report every field
  that refers, at any depth through a list, a map or a nullable, to a nested
  object name no shape in the snapshot carries, with the remedy. A union
  case payload a hand-kept list misses is not a reference the snapshot can
  see, and is covered only by `Shape.ofSchemaDeep`.
- **`ShapeField.Default`** records a field's default as the encoded JSON text,
  and a changed default is reported as breaking Backward: what was already
  written and lacks the field now reads as a different value. The snapshot
  format is version 3; version-2 files still read, with defaults not recorded
  and so never compared.

### Fixed

- **A union-rooted or list-rooted `Schema` produced a shape with no fields and
  no error.** A union root now records one field named after the tag key with
  the union's cases; any other non-object root records one field named `$`
  with the root's type and rules. Every change to such a payload is a change to
  a field, and reported.
- **`ShapeSnapshot.of'` kept two shapes under one name and version.** Identical
  copies, which several roots sharing one nested object now produce, collapse
  to one; two different shapes under one name and version are refused.

### Breaking

- `ShapeField` gains `Default: string option option`. Code constructing
  `ShapeField` by hand adds the field, `Some None` for a field with no
  default.
- A regenerated snapshot changes for every union-rooted or non-object-rooted
  shape, which gains a field, and for every field, which gains a `default`
  key. Regenerate with `ShapeSnapshot.extend committed current` as usual; the
  lag report names the shapes.
- A harness that lists nested shapes by hand and misses a nested object goes
  red with the name of the missing shape.

## [0.1.0-preview.15]

Four changes `Etymon.Schema.Compatibility` let pass without a verdict, found
by the [review of 2026-10-05](docs/review-2026-10-05.md) and designed in
[the snapshot must not lag](docs/design/compatibility-unrecorded-2026-10-05.md).
Every consumer harness had the same three tests, and every one of the four
passed all three.

### Fixed

- **A snapshot could lag without anything saying so.** An optional field
  added to a stored event breaks neither direction, so `Compatibility.between`
  said nothing, and nothing demanded the committed file be regenerated. A later
  removal of the same field, now required, was compared against a file that
  never carried the field, and passed. `Events.unrecorded` and
  `Wire.unrecorded` now report every current shape the committed snapshot does
  not record as the shape is now, naming the fields, and `Events.check`,
  `Wire.unresolvedRequests` and the new `Wire.unresolvedResponses` include the
  report. One consumer had patched the miss as a text comparison of the two
  files in the consumer's own test support.
- **A removed shape had no verdict.** `Compatibility.between` visited the names
  of the current snapshot only, so a shape present in the committed file and
  gone from the code produced nothing. The union of both snapshots' names is
  visited, and a removed shape is reported as breaking both directions: what
  was already written under the name has nothing reading the shape, and
  already-deployed code reading the name receives nothing.
- **A declared rename had no verdict.** The declaration matched the old name
  with the new one and then said nothing, although new code reading a stored
  event looks for the new name and finds the old one, and a client already
  written reads the old name in a response that now carries the new one. A
  declared rename of a required field is reported as a rename, breaking both
  directions. An optional field renamed breaks neither, as an optional field
  added or removed does.
- **Stranded versions were looked for in the current snapshot alone.** A
  harness derives the current snapshot from the codecs, so the current snapshot
  carries only the versions the code declares today, while the committed file
  is where the stored versions live. `Events.check` and
  `Wire.unresolvedRequests` compute reachability over
  `ShapeSnapshot.extend before after`, so a version in the file that the code
  no longer declares still demands an upcaster.

### Added

- **`ShapeSnapshot.extend committed current`**: the regeneration primitive.
  Every committed shape stays, and each current shape is added or replaces the
  committed shape of the same name and version. Regenerate with
  `ShapeSnapshots.toJson (ShapeSnapshot.extend committed current)` and history
  stops disappearing from the file.
- **`Wire.unresolvedResponses renames before after`**: rename ambiguities and
  the lag report for the response side, which had no unresolved report at all.

### Breaking

- Behavior, not signatures. `Events.check` and `Wire.unresolvedRequests` report
  a lagging snapshot and versions found only in the committed file;
  `Compatibility.between` reports removed shapes and declared renames of
  required fields. A harness green on preview.14 can go red on preview.15
  wherever the committed file does not describe the codecs, which is the
  point. Regenerate the file with `ShapeSnapshot.extend`, drop any names-only
  check and any text comparison of the two files, and declare the upcasters the
  report names.

## [0.1.0-preview.14]

### Fixed

- **TypeScript quotes a key that is not an identifier.** A field named
  `order total`, `2x` or `say "hi"` is a legal JSON key and was written bare,
  as `readonly order total: string;`, which does not compile. Such a key is
  now a quoted string (`readonly "order total": string;`), escaped where the
  key holds a quote, a backslash or a control character; an identifier,
  `$` and `_` included, stays bare. A union's tag follows the same rule.
  Found by a consumer generating a type per database query, where column
  names arrive as the database spells them.

No breaking changes: every key that compiled before is written exactly as
before.

## [0.1.0-preview.13]

### Added

- **`RequestField`** (#24): the other direction of `WireField`. Since
  preview.10 `WireField` describes a nullable record field as what is
  *written* — present and null, required with a nullable type. A request body
  is *read*, leniently, so the honest description of the same field is optional
  and nullable, and nothing in the library said so; one consumer wrote the
  module itself and moved seventy fields onto it. It is beside `WireField` now,
  with the same members and signatures, built on `Schema.defaulted` with a null
  fallback so the document says `"default": null` — which is why it waited for
  #21. A DTO shared by both directions is described twice, once each way, and
  neither is the other's misuse. `requiredArray` and `requiredDictionary` are
  `WireField`'s, because required is required both ways.

No breaking changes.

## [0.1.0-preview.12]

Two defects from the same adoption, in the derivations. No public signature
changes.

### Fixed

- **`OpenApi.toComponents` threw on a field whose default is null** (#21).
  `Schema.defaulted` stores a JSON null default as `Some null`, because
  `System.Text.Json.Nodes` has no other value for it, and the renderer
  dereferenced it. It renders as `"default": null` now. This is the ordinary
  shape of a request field a caller may leave out and a form may send as null.
  Decoding, encoding and `Shape.ofSchema` were never affected.

- **A request field coming to admit null was reported as a break for clients
  already written** (#23). The matrix reported every type change as breaking in
  both directions. A change that only adds null — `string` to `string | null`,
  at any depth — is a widening: everything already written still reads, so it
  breaks only forward, where already-deployed code may now meet a null.
  Removing null is the mirror image. Because the fix is in the shared matrix,
  both policies get it: `Wire.requests` no longer flags the widening,
  `Wire.responses` still refuses it as the break with no remedy, and a stored
  event that gains a nullable field is no longer a backward break either.

## [0.1.0-preview.11]

One regression, introduced by preview.10 and reported within the hour.

### Fixed

- **A snapshot written before element rules existed diffed as a narrowing on
  every list field** (#20). preview.10 read the missing `elementConstraints`
  key as `[]`, and `[]` is indistinguishable from "no rules" — so the first
  comparison after upgrading reported a false narrowing on every list field
  with item rules, with nothing to tell it from a real one. Six event shapes and
  the same again on the wire, in one consumer.

  "Not recorded" and "recorded none" are different facts and are now different
  values: `ShapeField.ElementConstraints` is `Constraint list option`, `None`
  for a snapshot that predates the key, and a comparison happens only when both
  sides recorded. `Shape.ofSchema` always records. A written snapshot always
  carries the key, and `formatVersion` is 2 to say so; version-1 files still
  read.

### Breaking

- `ShapeField.ElementConstraints` changes type, one release after it was added:
  `Constraint list` → `Constraint list option`. Code constructing `ShapeField`
  by hand wraps the value in `Some`; code reading it matches on the option.

## [0.1.0-preview.10]

Four more defects from the same adoption. One changes what goes on the wire,
which is the reason previews exist.

### Breaking

- **`WireField` now writes what System.Text.Json writes** (#13). A nullable
  field is written as `"x": null`, never as an absent key, and is described as
  required with a nullable type — `["string", "null"]`, in `required` — rather
  than as optional. Before this, the OpenAPI document told a generated client to
  expect `undefined` and handed it `null`, and the compatibility snapshot
  recorded a key that was always there as one that might not be, so removing it
  later diffed as safe. Forty-four fields across nineteen response shapes in one
  consumer, every one written present-and-null. Decoding is unchanged: absent
  and `null` both read as `null`.

  New: `Schema.present`, the primitive this is built on — always written,
  described required-nullable, read leniently — for a record that already
  speaks in `option`. `WireField.nullable` and `Schema.nullable` now mean the
  same thing; `Schema.optional` alone means a key that may be missing.
  `WireField.array` and `WireField.dictionary` are described as required too,
  because they always write a collection; what they write and read is unchanged.

- **`ShapeField` gains `ElementConstraints`** (#17). `Shape.ofSchema` carried a
  field's constraints for a scalar and dropped them for the items of a sequence,
  so removing a code from `roles` or `permissions` — the fields most likely to
  hold a code set — was not reported. A change to them now diffs like a change
  to a scalar's rules. Snapshots written before the key existed still read;
  their collections simply have no element rules recorded. Code constructing
  `ShapeField` by hand adds the field.

### Fixed

- **`Refine.wrap` ran before the checks that preceded it** (#16), so the only
  public way to guard a partial conversion — a `satisfies` that a code parses,
  then a `wrap` that parses it — threw from inside the decoder instead of
  refusing. On a replay of stored events that reads as data corruption. Rules
  added before `wrap` now run before it. New: `Refine.parse`, for a conversion
  that can refuse on its own, publishing its rule like any other.
  `Refine.validate` is unchanged.
- **`Schema.stringly` appeared in the XML documentation and could not be
  called** (#15). It is a private helper; it no longer carries a doc comment.
  The public way to carry a coded value — `Schema.constrain (Check.oneOf codes)`
  then `Schema.convert` — is in the README, and it publishes the code set as an
  `enum`, which `stringly` never did.
- Every line of a compatibility report ended in two full stops (#17, in passing).

## [0.1.0-preview.9]

### Added

- **Every shipping package now carries `Surface.fsi`** (#11): its complete
  public surface as the compiler infers it -- every value with its full
  signature, return type included, and its documentation -- at the root of the
  nupkg beside the README. XML documentation has nowhere to put a return type,
  so a reader working from the package on disk was learning what a function
  hands back by compiling something and reading the error. Four such cycles in
  one adoption, for information the package already knew.

  The file is written by the build and committed under `src/<Package>/`, so a
  change to the public surface is a diff in review, and CI fails when the
  committed copy is stale. Generated rather than hand-written; the reasoning is
  in `docs/decisions.md`.

Nothing else changed. No breaking changes.

## [0.1.0-preview.8]

Six defects reported from one adoption, across 22 request and 38 response
shapes. Five are fixed here. Each was found by using the packages rather than by
reading them, which is the only way most of these surface.

### Fixed

- **A required collection had no correct short path** (#6). A field typed
  `Dictionary<string, string>` — non-nullable, always populated — could be
  described by `WireField.dictionary`, which compiles, reads well, and describes
  the field as **optional**. The correct form, `Schema.required` with
  `Schema.map`, speaks `Map` where the record holds a `Dictionary`, so the caller
  converts by hand in both directions. The correct path was the inconvenient one
  and the incorrect path was silent.

  preview.7 made it quieter: once the crossing started handing back non-null
  collections, the wrong helper assigns cleanly into a non-nullable field and the
  compiler has nothing to say. The wrong optionality then reaches the published
  OpenAPI document as a nullability clients must handle, and the compatibility
  snapshot as an optionality that makes removing the field later look safe when
  it is breaking.

  New: `WireField.requiredArray`, `WireField.requiredDictionary`. The rule, now
  in the README: reach for `WireField` because a field is nullable, never because
  it is a collection.

- **A wire verdict about an HTTP request body said "stored events"** (#7).
  preview.6 fixed this in the shared matrix and missed the other half:
  `Wire.unresolvedRequests` delegated to `Events.check`, so it inherited the
  event vocabulary wholesale. The two checks now sit below both policies and take
  their noun from the policy. `Wire.reportUnresolved` is new, so printing a wire
  outcome never routes through `Events`. Event output is byte-identical, and a
  test asserts that.

- **`WireField` had no member for a nullable field of an arbitrary `Schema`**
  (#8). A nested object that may be null, a string carrying `Schema.sensitive`, a
  `Schema.raw` payload — each fell back to the `Option.ofObj` / `Option.toObj`
  pair the module exists to remove, at the fields where going through a `Schema`
  matters most. `WireField.nullable` takes any `Schema`; `text` and
  `constrainedText` are now defined in terms of it.

- **`Shape.ofSchema` asked for a name the `Schema` already carried** (#9), from
  two sources, with nothing checking they agree. A shape recorded under a
  mismatched name diffs cleanly against itself forever and is never compared with
  what it describes — the failure is silence.

- **`Schema.OpenApi` could not assemble a document from many schemas** (#10), and
  the obvious hand-rolled attempt silently breaks arrays: a lift that reads
  `$ref` at the root finds nothing for a list, because an array carries its
  `$ref` under `items`, and the response is described as an empty object. Valid
  OpenAPI, green tests, no description. `OpenApi.toComponentsWithRoot` returns the
  root already addressed at `#/components/schemas/`, so there is no rewriting
  pass to get wrong.

### Breaking

- `Shape.ofSchema` loses its first parameter; the name comes from the `Schema`.
  Callers wanting the old behaviour rename the call to `Shape.ofSchemaNamed` and
  change nothing else. An unnamed `Schema` is now refused rather than recorded
  under no name.

### Still open

- **Return types are invisible to a reader working from the package rather than
  the source** (#11). The XML documentation carries summaries, remarks and
  examples but not signatures, because .NET XML documentation has nowhere to put
  them. The F#-native answer is `.fsi` signature files, which would also make the
  public surface reviewable in its own right. That is a larger change than the
  rest of this release and is not in it.

## [0.1.0-preview.7]

### Fixed

- **`WireField` could not take a nullable field, which is the only kind it is
  for.** The getters shipped in preview.5 typed as `'T -> string`, `'T -> 'F[]`.
  Against a record whose fields are not annotated that compiles; against the
  records `System.Text.Json` actually produces, in a project with
  `<Nullable>enable</Nullable>`, every call site failed FS3261. The module exists
  to serve exactly the projects it would not compile in.

  The getters are now annotated -- `'T -> string | null`, `'T -> 'F[] | null`,
  `'T -> IDictionary<string, 'V> | null` -- and `Etymon.Schema` compiles with
  nullness enabled so the annotations are checked rather than decorative.

- **The crossing now goes both ways.** Previously the getter read the nullable
  form and the expression bound an `option`, so the reads were clean and every
  field of the `return` carried an `Option.toObj` back. That removes the smaller
  half of the work. `WireField.text` now binds `string | null`, `WireField.int`
  binds `Nullable<int>`, `WireField.array` binds an array -- the record is
  rebuilt by naming its fields and nothing else.

### Notes

Two decisions the collection fields make, unchanged but now documented:

- A null collection and an absent key both read as empty, and the bound value is
  an empty collection rather than null, so no call site needs a guard. Where the
  distinction carries meaning, state it with `Schema.required`.
- `WireField.dictionary` takes `IDictionary<string, 'V>` and hands back a
  `Dictionary<string, 'V>`: the interface going in so either field type
  satisfies it, the concrete type coming back so it assigns without a cast.

Nullness is enabled on `Etymon.Schema` and its tests, not suite-wide. The syntax
compiles against the pinned FSharp.Core 8.0.100, but the narrowing helpers
(`nonNull`, `NonNull`) need FSharp.Core 9, and 8.0.100 is the deliberate consumer
floor. The test project has it enabled because that is the audience being tested.
## [0.1.0-preview.6]

### Fixed

- **A wire verdict said "events".** The compatibility matrix is shared by both
  policies, but its wording was written for the event one, so `Wire` concatenated
  reasons that talked about events into verdicts about HTTP responses. The matrix
  now states facts in neutral terms and each policy supplies its own advice --
  which is the right layering anyway: an upcaster is the answer for an event or a
  request body and there is no answer for a response, so the matrix is not the
  place to prescribe one. Two sentences also ran together without a full stop.

  Found by running the published package rather than the local build. The verdict
  is the product in this package, so its prose is not cosmetic.

- **The large-input test asserted a ten-second wall clock**, which stopped a
  release on a loaded runner for reasons unrelated to the code. It measures a
  complexity class, not a speed: the linear path takes about a sixth of a second
  and the quadratic one took sixty-seven, so the bound is now thirty.

### Changed

- GitHub Actions bumped: checkout 4 to 7, setup-dotnet 4 to 6, upload-artifact 4
  to 7, clearing the Node 20 deprecation warning on every run.

## [0.1.0-preview.5]

### Changed -- breaking

- **`Etymon.Schema.Events` is now `Etymon.Schema.Compatibility`,** and the
  event-specific types lose the word: `EventShape` is `Shape`, `EventField` is
  `ShapeField`, `EventSnapshot` is `ShapeSnapshot`, and the `Upcasters` module is
  `Events`.

  What shipped in preview.4 turned out to be one use of a more general
  capability. Strip "events" out of "is this change safe against the data already
  written?" and nothing breaks: a shape snapshot, a diff, and a compatibility
  matrix mention no storage at all. What is event-specific is the policy wrapped
  around them.

  A second policy has now appeared over the identical matrix, so the package is
  named for the capability and the two uses are policies inside it. The matrix
  has exactly one definition and both read it -- two copies of a compatibility
  table is how the two quietly stop agreeing.

  `Etymon.Schema.Events` stops at `0.1.0-preview.4`. The event policy's own rules
  are unchanged.

### Added

- **The `Wire` policy: wire contracts.** A DTO's old shape sits in somebody
  else's code, not in your database, so what can be done about a break depends
  on which way it travels.

  Requests are backward compatibility and the event answer applies: write an
  upcaster. **Responses are forward compatibility and cannot be rescued at all**,
  because the code that would run an upcaster is not code you ship -- the only
  answers are do not make the change, or version the endpoint. The package offers
  no hook for a response fix, because a hook that cannot help suggests the
  problem has been handled.

  Verdicts name the audience rather than the direction, since "forward" and
  "backward" are the two words people reliably get the wrong way round.

- **`WireField` in `Etymon.Schema`.** A record deserialised by System.Text.Json
  is nullable throughout, a Schema speaks in `option`, and every field crosses.
  The first real adoption wrote this module for roughly sixty fields, and every
  consumer arriving from System.Text.Json would write it again.

  It is not a shorter spelling of `Schema.optional`. That says *this key may be
  absent*, about the contract; `WireField.text` says *this field is nullable
  because of how it arrived*, about the record, and temporarily. When those
  records stop being nullable, `WireField` is the list of everywhere it mattered.

- **The adoption rule, written down** in the `Etymon.Schema` README: a field the
  record declares nullable is optional in the Schema, a field it declares
  non-nullable is `Schema.required`. Take the record at its word. Without it the
  first question on every type is "should this be required?" and the answer
  drifts across a codebase.

## [0.1.0-preview.4]

### Changed -- breaking

- **`Etymon.Migrations` is now `Etymon.Schema.Sql`.** `Etymon.Migrations` stops
  at `0.1.0-preview.3` and receives nothing further; take `Etymon.Schema.Sql`
  instead. The namespace is unchanged, so only the package reference moves.

  The package maps a Schema to
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

- **`Etymon.Schema.Compatibility`.** In an event-sourced system the tables barely
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

[0.1.0-preview.6]: https://github.com/CaelusMinds/Etymon/releases/tag/v0.1.0-preview.6
[0.1.0-preview.5]: https://github.com/CaelusMinds/Etymon/releases/tag/v0.1.0-preview.5
[0.1.0-preview.4]: https://github.com/CaelusMinds/Etymon/releases/tag/v0.1.0-preview.4
[0.1.0-preview.3]: https://github.com/CaelusMinds/Etymon/releases/tag/v0.1.0-preview.3
[0.1.0-preview.2]: https://github.com/CaelusMinds/Etymon/releases/tag/v0.1.0-preview.2
[0.1.0-preview.1]: https://github.com/CaelusMinds/Etymon/releases/tag/v0.1.0-preview.1
