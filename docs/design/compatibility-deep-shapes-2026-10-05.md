# Design: nested shapes and union payloads are recorded, not named

> Status 2026-10-05: designed and shipped in `0.1.0-preview.16`. Answers
> action 2 of the [review of 2026-10-05](../review-2026-10-05.html): a shape
> snapshot stopped at a nested name, so a change inside a nested object or a
> union case passed every harness. Deliberately not done: a payload type on
> each union case inside `FieldType`, and a separate version for a nested
> shape; both are explained at the end.

## The defect

`Shape.ofSchema` records a nested object as `FieldType.Nested name` and a union
as `FieldType.Choice (name, tags)` without walking either. A harness that wants
a nested shape compared lists the nested shape by hand, and a nested shape the
harness does not list is a shape no check covers. FlowQL's
`model.published.input` is declared `let private` behind the event codec, so
FlowQL's hand list cannot name the shape, and a field change inside the shape
passes FlowQL's suite today. Three smaller gaps sit beside the large one:

1. A union case's payload is never recorded at all, so a payload change from a
   record to a string passes.
2. A union-rooted or list-rooted `Schema` produces a shape with no fields and
   no error, so every change to such a payload passes.
3. `ShapeField` never copies `FieldInfo.Default`, so a changed default, which
   changes how every stored event lacking the field reads, passes.

## The design

**`Shape.ofSchemaDeep version schema : Shape list`** walks the `Schema` and
returns the root shape first, then every shape reachable from the root, in name
order, all at the given version:

- a nested object or union is recorded under the object's or union's own
  name, once, however many fields or roots reach the shape; a union records
  one field named after the tag key, as a union root does;
- a union case whose payload is an object or a union is recorded under the
  payload's own name; a union case whose payload is anything else recordable
  (a list, a scalar, a nullable) is recorded under `<Union>.<tag>` with one
  field named `$`, the document root; a case with no payload records
  nothing, because `SchemaInfo` carries a unit case as a raw payload;
- the walk stops at a reference (`SRef`), which is how recursion is broken,
  and at a raw payload; once every union is recorded, a reference to a union
  resolves to the union's cases, so a field holding the union by reference and
  a field holding the resolved union record one field type;
- two different definitions under one name are refused with
  `ArgumentException`, because a snapshot matches by name and one name can
  describe one shape.

`FieldType` is unchanged: a nested object is still `Nested name` and a union is
still `Choice (name, tags)`, so a version-2 file still reads and the file stays
readable by a person. The depth lives in the snapshot, as more shapes, not in
the field type.

**`Shape.ofSchema` on a root that is not an object** records one field instead
of none: a union root records one field named after the tag key with the
union's `Choice` type, and any other root records one field named `$` carrying
the root's type and rules. Every change to such a payload is then a change to a
field, and reported. A list or scalar root takes a name from `Schema.named`,
which is how such a root enters `Shape.ofSchemaDeep`.

**`ShapeField.Default : string option option`** records a field's default as
the encoded JSON text: the outer option is whether the writer recorded a
default at all, so a version-2 file reads as unrecorded and is never compared,
exactly as `ElementConstraints` already does; the inner option is whether the
field has a default. In the file a recorded default is a small object, `{}`
for none and `{ "value": text }` for one, because an optional key read as
JSON null reads as absent, and "recorded none" must survive the trip. A
changed default breaks Backward: what was already written and lacks the field
now reads as a different value. The snapshot
format is version 3 to say so; version-2 files still read.

**`ShapeSnapshot.of'` collapses identical duplicates** of one name and version,
which several roots sharing one nested object now produce, and refuses two
different shapes under one name and version with `ArgumentException`.

**A nested object name with no shape is unresolved.** `Events.check`,
`Wire.unresolvedRequests` and `Wire.unresolvedResponses` report every field
whose type refers, at any depth through a list, a map or a nullable, to a
nested object name no shape in the snapshot carries. The message says to
derive with `Shape.ofSchemaDeep` or to add the shape. A harness that lists
nested shapes by hand and misses a nested object goes red with the name of the
missing shape. A union case payload such a list misses is not a reference the
snapshot can see, because `Choice` carries tags only, and is covered only by
`Shape.ofSchemaDeep`; the first gap above is closed by the derivation, not by
the report.

## Decisions

- **Nested shapes take the root's version.** A nested shape's history is the
  history of the events that carry the shape, and an upcaster for the root
  reads the whole payload. Several roots at different versions sharing one
  nested object therefore record the nested object once per version with the
  same fields. A nested name re-versioned with the root reports as stranded
  until an upcaster is declared under the nested name, and the declaration
  says the root's upcasters cover the nested shape. Recorded here so the
  report is read as a declaration owed, not as a defect.
- **The payload of a union case is a shape, not a field type.** Recording the
  payload inside `FieldType.Choice` would change the file format of every
  union field and force a custom codec to read version-2 files; recording the
  payload as a shape reuses every field-level verdict and keeps `Choice` as
  tags, which is what the compatibility matrix compares.
- **A unit case and a raw case are the same to the snapshot.** `SchemaInfo`
  records a unit case's payload as `SPrim Raw`, and a raw payload is one the
  snapshot cannot reason about, so neither records a shape.
- **`$` names the root.** A list-rooted or scalar-rooted payload has no field
  to name, and the JSON-path spelling of the document root is the one spelling
  a reader of the report recognizes.

## Deliberately not done

- **A payload type on each union case inside `FieldType`.** See the second
  decision; revisit if a consumer needs the case payload visible in the field
  type itself rather than as a shape.
- **A separate version for a nested shape.** A version a nested shape carries
  on the shape's own account would need a second upcaster vocabulary, and no
  consumer has asked for one.
- **A shallow derivation that warns.** `Shape.ofSchema` stays shallow and
  silent, because the unresolved report for a nested name with no shape is the
  warning, and the report names the shape.

## Tests

One test per case: `ofSchemaDeep` returning the root first and every nested
object once; a union case with an object payload under the object's name, with
a union payload under the union's name, with a scalar payload under
`<Union>.<tag>` and `$`, and with no payload recording nothing; the walk
stopping at a reference, for a recursive object and for a recursive union
reached through a field, with the reference resolving to the union's cases and
`Events.check` reporting nothing; a list root named with `Schema.named`; two
definitions under one name refused; a union root and a list root recording one field; a default recorded
as text, a required field recorded as no default, and a version-2 file reading
as unrecorded; a changed default breaking Backward, an unrecorded default
breaking nothing; `of'` collapsing identical duplicates and refusing different
ones; a nested object name with no shape reported under both policies, through a
list, a map, a nullable and a list of nullables, and nothing reported when the
shape is present; a version-3 file round-tripping, a regenerated file reporting
nothing unrecorded against the same shapes, and a version-2 file still reading.

## What the consumers do next

- **FlowQL** replaces the hand-listed `nestedShapes` with
  `Shape.ofSchemaDeep` and regenerates `events.json`, which records
  `model.published.input` for the first time.
- **Keel** does the same, which retires the `nestedShapes` list and the
  comment explaining the list.
- **Mast** derives wire shapes with `Shape.ofSchemaDeep` so a response built
  from nested records is compared in full.
