# Design: a union says how the union is written

> Status 2026-10-05: designed and shipped in `0.1.0-preview.17`. Answers
> action 3 of the [review of 2026-10-05](../review-2026-10-05.html): one
> union wire shape, so a product whose stored events carry an internally
> tagged union writes the codec by hand over `Schema.raw`, and the snapshot,
> the OpenAPI document and the TypeScript output see no cases. Deliberately
> not done: an externally tagged shape, and a named component per union case
> in the OpenAPI document; both are explained at the end.

## The defect

`Schema.union` writes every union one way: the tag under a caller-named key
and the payload under a hard-coded `value` key. Keel's `EntrySource` is pinned
by a digest to `{ "type": "contractor_bill", "billNumber": "KT-32" }`, the
payload's fields merged beside the tag, so Keel reads and writes the union by
hand over `Schema.raw` with a predicate, and `SchemaInfo` records the field as
raw. The snapshot records the field as unknown, the OpenAPI document shows a
free object, the TypeScript output says `unknown`, and a case added or removed
passes every derivation.

Two smaller gaps sit beside the large one: `SchemaInfo` records a unit case's
payload as `SPrim Raw`, so a case with no payload and a case with a raw payload
are the same description; and the `value` key cannot be renamed.

## The design

**`UnionShape`**, in `Etymon.Schema`, says how a union is written:

- `AdjacentTag (tag, payloadKey)`: `{ "<tag>": "circle", "<payloadKey>": … }`,
  the shape every union has had, with the payload key now a parameter;
- `InternalTag tag`: `{ "<tag>": "contractor_bill", …payload fields… }`, the
  tag written first and the payload's own fields beside the tag.

**`Schema.unionWith shape name cases`** builds a union of either shape.
`Schema.union name tag cases` stays and means
`Schema.unionWith (AdjacentTag (tag, "value")) name cases`, so every existing
union keeps the bytes the union has. An internal tag merges the payload's
fields with the tag, so every case of an internally tagged union carries an
object payload or no payload, with no field named like the tag; `unionWith`
refuses anything else when the schema is built, not when a value is written.
The one payload the union cannot see when built is a reference
(`Schema.recursive`), which is checked on the first write, failing with the
union's and the case's names. An adjacent shape naming one key for the tag and
the payload is refused the same way.

**`SUnion` carries the shape and the honest payload**:
`SUnion of name * shape: UnionShape * cases: (string * SchemaInfo option) list`.
A case with no payload is `None`, not a raw payload. Every derivation reads the
shape:

- the codec writes the tag first under either shape and reads an internally
  tagged payload from the whole object, which the object reader accepts because
  the object reader ignores a key the object does not declare;
- the OpenAPI document renders an adjacently tagged case as today, with the
  payload key, and an internally tagged case as `allOf` of the payload and an
  object requiring the tag constant; in the OpenAPI 3.1 dialect an internally
  tagged union whose every case has a named payload also carries
  `discriminator { propertyName; mapping }`;
- the TypeScript output renders an internally tagged case as
  `({ readonly type: "x" } & Payload)`;
- the generator in `Etymon.Invariants.FsCheck` writes the tag and merges a
  generated payload object for an internal tag;
- the compatibility snapshot records the wire shape on `FieldType.Choice` as
  `encoding: string option`, `adjacent:<tag>:<payloadKey>` or
  `internal:<tag>`, unrecorded in a file older than format 4 and never
  compared then, at any depth through a list, a map or a nullable; a changed
  encoding breaks both directions, because what was already written carries
  the old keys and already-deployed code reads the old keys. The union's own shape, recorded by `Shape.ofSchemaDeep`, names the tag
  field after the shape's tag.

## Decisions

- **The tag is written first.** A reader that scans for the tag stops early,
  and Keel's digest-pinned bytes carry the tag first.
- **An internal tag demands an object payload at construction.** The
  alternative, failing on the first write of a non-object payload, is the
  failure a test suite without that case never sees. A reference payload is
  the exception the union cannot avoid, so the write checks the bytes and
  names the case, and the generator in `Etymon.Invariants.FsCheck` never
  hides a colliding field, so the property suite sees what the codec writes.
- **`CaseSchema` splits the case from the shape.** A case says whether a value
  is the case, writes the payload alone and reads the payload alone; the union
  decides where the payload goes. The record is public and changes, and only
  `Schema.case` and `Schema.caseUnit` construct the record in the suite.
- **The encoding on `Choice` is text, not a type.** The snapshot file stays
  readable by a person, and a comparison of two spellings is all the matrix
  needs.

## Deliberately not done

- **An externally tagged shape** (`{ "circle": … }`). No product writes one,
  and a unit case under an external tag has no obvious spelling; add the case
  to `UnionShape` when a consumer brings bytes.
- **A named component per union case in the OpenAPI document.** The
  discriminator mapping points at the payload's own component, which is what
  an internally tagged case is; an adjacently tagged case stays inline, as
  today, because the case object has no name of the case's own.
- **Rewriting Keel's codec.** Keel moves `EntrySource` onto
  `Schema.unionWith (InternalTag "type")` with the digest test as proof; the
  move is Keel's change.

## Tests

One test per case: an internally tagged union writing the tag first and the
payload's fields beside the tag, a unit case as the tag alone, both reading
back under strict decoding, a missing tag and an unknown tag reported at the
tag's path, a non-object payload and a field named like the tag refused when
the union is built, a reference payload that is not an object failing on write
by name, an adjacent shape with one key for both refused, an adjacent union
with a renamed payload key, a two-field payload keeping the payload's field
order beside the tag; every existing adjacent test unchanged; the
OpenAPI render of an internally tagged union as `allOf` with the discriminator
in the OpenAPI 3.1 dialect and without one in JSON Schema; the TypeScript
render as an intersection; generated samples of an internally tagged union
reading back; the snapshot recording and round-tripping the encoding, a
version-3 file reading the encoding as unrecorded, a union inside a list, a
map and a nullable read from such a file breaking nothing, and a changed
encoding breaking both directions.

## What the consumers do next

- **Keel** replaces the hand-written `EntrySource` codec in
  `Keel.Ledger.Store/EventCodec.fs` with `Schema.unionWith (InternalTag "type")`
  and keeps the digest test as the proof that the bytes did not move.
- **FlowQL** keeps `Schema.union`; the bytes are unchanged.
- **Windlass** matches `SUnion` with a wildcard in `Map/Target.fs` and
  compiles unchanged.
