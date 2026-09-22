# Etymon.Schema.OpenApi

JSON Schema 2020-12 and OpenAPI 3.1 from an [Etymon](https://github.com/CaelusMinds/Etymon)
schema.

Depends on `Etymon.Core` and `Etymon.Schema`, and on no NuGet package beyond
`FSharp.Core`.

```fsharp
OpenApi.toJsonSchema personSchema          // a standalone JSON Schema 2020-12 document
OpenApi.toComponents personSchema          // the components/schemas object for OpenAPI 3.1
OpenApi.toComponentsWithRoot personSchema  // both, with the root addressed at them
OpenApi.toJsonSchemaText personSchema      // the same, rendered as indented text
```

Assembling one document from many operations wants the **pair**:
`toComponentsWithRoot` gives the named types for `components/schemas` and a root
already addressed at `#/components/schemas/…`, so nothing has to re-address
anything:

```fsharp
let listing = OpenApi.toComponentsWithRoot (Schema.list accountSchema)
// listing.Root       = { "type": "array", "items": { "$ref": "#/components/schemas/Account" } }
// listing.Components = { "Account": { … } }
```

Doing that lift by hand has a trap in it. The natural implementation reads
`$ref` at the **root** of the standalone document and rewrites it — correct for
an operation returning one object, and silently wrong for one returning a list,
because an array carries its `$ref` under `items` and the root has none. The
lift finds nothing and the response is described as an empty object. Nothing
fails, the document is still valid OpenAPI, and a test asserting "this path is
described" is satisfied by a description that says nothing.

The document is derived from the same schema value that does the encoding and
decoding, so it cannot describe something different from what your code actually
accepts. Nothing here reads the codec — it is a pure function of the schema's
description.

## What comes out

```fsharp
let personSchema =
    Schema.object "Person" {
        let! name = Schema.required "name" (Schema.string |> Schema.constrain Check.nonEmpty) (fun p -> p.Name)
        and! age = Schema.required "age" (Schema.int |> Schema.constrain (Check.intRange (Some 0) (Some 130))) (fun p -> p.Age)
        and! email = Schema.optional "email" Email.schema (fun p -> p.Email)
        and! address = Schema.required "address" Address.schema (fun p -> p.Address)
        return { Name = name; Age = age; Email = email; Address = address }
    }
```

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$ref": "#/$defs/Person",
  "$defs": {
    "Person": {
      "type": "object",
      "properties": {
        "name": { "type": "string", "minLength": 1 },
        "age": { "type": "integer", "format": "int32", "minimum": 0, "maximum": 130 },
        "email": { "type": "string", "minLength": 3, "maxLength": 254, "format": "email" },
        "address": { "$ref": "#/$defs/Address" }
      },
      "required": ["name", "age", "address"]
    },
    "Address": { "…": "…" }
  }
}
```

Constraints become keywords: `minLength`/`maxLength`, `minimum`/`maximum` and
their exclusive forms, `pattern`, `format`, `enum`, `minItems`/`maxItems`/
`uniqueItems`. Named schemas are written once under `$defs` (or
`components/schemas`) and referenced, which is also what makes a **recursive**
type expressible. Field descriptions, defaults and examples all carry through,
and a field marked `Schema.sensitive` comes out `writeOnly`.

Unions render as `oneOf`, one branch per case, each pinning its tag with `const`.
A case carrying no payload requires only the tag.

## Two things it deliberately will not do

**It never claims `additionalProperties: false`.** Etymon's decoder ignores keys
it does not recognise. Emitting that keyword would advertise a stricter contract
than the code enforces, and a generated document that lies is worse than no
document.

**It never invents a keyword for a rule it cannot express.** `Constraint.Opaque`
— the escape hatch for a rule written as an arbitrary predicate — has no JSON
Schema equivalent. Rather than dropping it silently, its description is appended
to the schema's `description`, so a reader is told about a rule the document
cannot enforce:

```fsharp
Schema.int
|> Schema.describe "A count"
|> Schema.constrain (Check.opaque "even" "must be even" (fun n -> n % 2 = 0))
```
```json
{ "type": "integer", "format": "int32", "description": "A count. must be even" }
```

## Notes

Output is **deterministic** — fields in declaration order, definitions in name
order — so a snapshot test of a generated document is meaningful and a change to
it shows up as a diff in a pull request.

Text output uses relaxed JSON escaping, so a regex like `^\d+$` reads as itself
rather than as `^\\d+$`. The HTML-safe escaping the default encoder applies
only matters when JSON is injected into a page unescaped, which is not what
happens to a schema document.

## Public surface

The complete public surface of this package, every value with its full signature
and its documentation, is in `Surface.fsi` beside this file. It is written by the
build from the implementation, so it is where to learn what a function hands
back without compiling anything.

## Licence

[MIT](https://github.com/CaelusMinds/Etymon/blob/main/LICENSE).
