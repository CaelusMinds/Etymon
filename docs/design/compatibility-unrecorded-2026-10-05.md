# Design: the snapshot must not lag

> Status 2026-10-05: designed and shipped in `0.1.0-preview.15`. Answers
> action 1 of the [review of 2026-10-05](../review-2026-10-05.html): four
> changes that `Etymon.Schema.Compatibility` let pass without a verdict.
> Deliberately not done: a shape-level rename declaration, and a regeneration
> step that refuses; both are explained at the end.

## The defect

Every consumer harness has the same three parts: the committed snapshot file,
the current snapshot derived from the codecs, and three tests over the pair
(`Compatibility.between` filtered to `Direction.Backward`, `Events.check`, and
a names-only check that every current name is in the file). Four changes passed
all three tests in `0.1.0-preview.14`:

1. **An added optional field was never recorded.** The matrix is right that an
   optional field breaks neither direction, so `between` said nothing, the
   names check saw the same names, and the committed file stayed as the file
   was. A later removal of the same field, now required, compared against a
   file that never carried the field, and passed. Mast patched the miss as a
   normalized text comparison of the two files, inside Mast's own test
   support, against the ruling that a defect a product finds in Etymon is
   fixed in Etymon.
2. **A removed shape had no verdict.** `between` visited the names of the
   *after* snapshot only, so a shape present in the committed file and gone
   from the code produced nothing. The `_, None` arm was dead code.
3. **A declared rename produced no verdict in either direction.** The rename
   declaration matched `from` with `to` for the alteration comparison and
   then said nothing, although new code reading a stored event looks for `to`
   and finds `from`, and a client already written reads `from` in a response
   that now carries `to`.
4. **Stranded versions were computed over the current snapshot alone.** A
   harness derives the current snapshot from the codecs, so the current
   snapshot carries only the versions the code declares today. The committed
   file is where the stored versions live, and `Events.check` never looked at
   the committed file for versions. A version bump without an upcaster passed.

## The design

Three additions to the matrix and the policies, one addition to the snapshot
module, no new types.

**`ShapeSnapshot.extend committed current`** is the regeneration primitive. The
result holds every shape of the committed snapshot, with each current shape
added or replacing the committed shape of the same name and version. A version
present only in the committed file stays, because stored events carry the
version whether or not the code still declares the version. Harnesses print
`ShapeSnapshots.toJson (ShapeSnapshot.extend committed current)` instead of
`ShapeSnapshots.toJson current`, and history stops disappearing on
regeneration.

**`Compatibility.between` visits the union of names.** A shape present in the
committed snapshot and absent from the current one is reported as
`the shape 'X' was removed`, breaking both directions: what was already written
as `X` has nothing reading the shape now, and already-deployed code reading `X`
receives nothing. `Events` sees the Backward break; `Wire.responses` sees the
Forward break; `Wire.requests` sees the Backward break.

**A declared rename of a required field is a change.** `between` reports
`'from' was renamed to 'to'` breaking both directions when the field is
required: Backward because what was already written carries `from`, Forward
because already-deployed code reads `from`. An optional field renamed breaks
neither direction, exactly as an optional field added or removed, so the
matrix stays silent and the lag is caught by the next item.

**A spent rename declaration is ignored.** Once the committed shape records the
new name, a declaration naming the old name can no longer be answered by the
shape, and honoring the declaration would resolve the new field to nothing and
hide every later change to the field. `between` and the rename-ambiguity check
read only declarations the committed shape can still answer.

**Stranded versions walk toward the version the code declares**, not the
highest version in the union, so a file that carries a version the code
reverted still names the version the code reads today.

**`Detect.unrecorded`**, surfaced as `Events.unrecorded` and `Wire.unrecorded`,
reports every current shape the committed snapshot does not record as the
shape is now: a name or version absent from the file, or the same name and
version with different fields. The message names the fields, and says to
regenerate with `ShapeSnapshot.extend` and commit the file. `Events.check`,
`Wire.unresolvedRequests` and the new `Wire.unresolvedResponses` include the
report, and `Events.check` and `Wire.unresolvedRequests` compute stranded
versions over `ShapeSnapshot.extend before after`, so a version the committed
file carries and the code no longer declares still demands an upcaster.

## Decisions

- **An unrecorded shape is `Unresolved`, not a `ShapeChange`.** A `ShapeChange`
  carries the directions a change breaks, and a lagging file breaks nothing by
  itself. `Unresolved` is "something that must be settled before a change is
  safe to ship", and a file that does not describe the bytes is exactly such a
  thing. The report plugs into the existing `check` functions without a new
  type.
- **The matrix keeps the invariant that every reported change breaks a
  direction.** An optional rename is therefore not reported by `between`; the
  lag shows through `unrecorded` instead.
- **A removed shape breaks both directions.** Under `Events` the Forward break
  is harmless in practice, since nothing writes the shape any more, and the
  policy filters to Backward anyway. The matrix states the fact and the policy
  chooses the direction, as everywhere else in the package.
- **Stranded versions are computed over the union of both snapshots.** The
  committed file is the record of what was written; the current snapshot is
  the record of what the code reads. Neither alone can say a version is
  stranded.

## Deliberately not done

- **A shape-level rename declaration.** Renaming an event type leaves stored
  events under the old type, and the honest verdict is the removed-shape
  break: keep the old shape readable, or upcast the old type. A declaration
  that silenced the verdict would hide the one case where history is lost.
- **A regeneration step that refuses.** `ShapeSnapshot.extend` is a pure merge.
  After a version bump the Backward verdict is the signal to declare the
  upcaster, and regenerating with `extend` is what settles the verdict, because
  the matrix compares the file's latest shape with the code's latest shape
  until the file records the new version. A refusal therefore belongs only to a
  Backward break that arrives without a new version and a declared upcaster,
  and the harness, not the library, is where such a refusal belongs. Revisit if
  a consumer asks for `Events.regenerate` returning `Result`.
- **Forcing a version bump on every in-place change.** Adding an optional field
  to a stored event needs no new version, and every consumer harness today
  carries v1 shapes only. The matrix already demands a bump where reading
  breaks.

## Tests

One test per case, under both policies where both apply: removed shape under
`Events` and under `Wire.requests` and `Wire.responses`; declared rename of a
required field in both directions and of an optional field in neither;
`unrecorded` for an added optional field, a new shape, a new version, an
identical pair, and a version-1 file against a recorded one; `extend` keeping a
committed-only version, replacing a same-version shape and adding a new one;
stranded versions over the union with and without an upcaster, for events and
for requests, and toward the code's version where the file carries a higher
one; a spent rename declaration hiding nothing; a declared rename combined with
a required-ness change; a removed request shape reported as unfixable;
`Wire.unresolvedResponses` for a rename ambiguity and a lag.

## What the consumers do next

- **Mast** deletes the normalized text comparison in
  `tests/Support/Mast.Testing/WireCompatibility.fs` and calls
  `Wire.unresolvedRequests` and `Wire.unresolvedResponses`.
- **Keel** and **FlowQL** regenerate the event snapshot with
  `ShapeSnapshot.extend committed current`, drop the names-only test, and let
  `Events.check` carry the lag report.
