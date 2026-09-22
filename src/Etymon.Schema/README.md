# Etymon.Schema

**Define the type once. Derive everything else.**

Part of the [Etymon](https://github.com/CaelusMinds/Etymon) suite. Depends on
`Etymon.Core` and `Etymon.Base`, and on no NuGet package at all beyond
`FSharp.Core` — `System.Text.Json` ships in the shared framework for both target
frameworks, so it is an in-box API here rather than a dependency you inherit.

## Do not install this for the JSON

F# already has good JSON libraries. [Thoth.Json]'s decoder combinators look a
great deal like `Schema.object`, [Fleece] and [Chiron] are mature, and
[FSharp.SystemTextJson] handles records and unions with almost no ceremony.
**Nobody should switch libraries for the codec alone**, and this package does not
ask you to.

The reason to write a `Schema<'T>` is what else comes out of it. One constraint
declaration —

```fsharp
let ageSchema = Schema.int |> Schema.constrain (Check.intRange (Some 0) (Some 130))
```

— reaches five places, and none of them restate it:

```fsharp
Schema.fromJson personSchema payload    // "age: must be between 0 and 130"
OpenApi.toJsonSchema personSchema       // "minimum": 0, "maximum": 130
Mapping.tableOf options personSchema    // CHECK ("age" >= 0 AND "age" <= 130)
Generate.valid personSchema             // only ever generates ages in range
Config.load personSchema sources        // "age: expected an integer (from environment)"
```

If you only want the JSON, use Thoth. If you want the OpenAPI document, the SQL
and the generators to be incapable of disagreeing with the decoder, that is what
this is for.

One advantage does hold even ignoring all that: a `Schema<'T>` is a **single
value**, not a separate encoder and decoder that can drift apart. There is no
way to change how a value is written without changing how it is read.

[Thoth.Json]: https://github.com/thoth-org/Thoth.Json
[Chiron]: https://github.com/xyncro/chiron
[Fleece]: https://github.com/fsprojects/Fleece
[FSharp.SystemTextJson]: https://github.com/Tarmil/FSharp.SystemTextJson

## The idea

A `Schema<'T>` is one value that knows how a type is **encoded**, **decoded**,
**validated** and **documented**. You write it once:

```fsharp
type Person = { Name: string; Age: int; Email: Email option; Address: Address }

let personSchema =
    Schema.object "Person" {
        let! name = Schema.required "name" (Schema.string |> Schema.constrain Check.nonEmpty) (fun p -> p.Name)
        and! age = Schema.required "age" ageSchema (fun p -> p.Age)
        and! email = Schema.optional "email" Email.schema (fun p -> p.Email)
        and! address = Schema.required "address" Address.schema (fun p -> p.Address)
        return { Name = name; Age = age; Email = email; Address = address }
    }
```

and get all of it:

```fsharp
Schema.toJson personSchema person      // encode
Schema.fromJson personSchema payload   // decode, reporting every error at once
Schema.validate personSchema person    // check the rules, no JSON involved
Schema.info personSchema               // the description every other package reads
```

## Errors accumulate, and say where

```fsharp
Schema.fromJson personSchema """{"age":-3,"address":{"zip":"xx"}}"""
```
```
name: is required
age: must be between 0 and 130
address.street: is required
address.zip: must be exactly 5 characters
address.zip: must match ^\d+$
```

Five problems, five errors, each with a path. Nothing stops at the first. A bad
element in a list reports its index — `items[3].name` — and a bad field inside a
union case carries its whole path through the recursion.

## Constrained types come with their rules

A refinement from `Etymon.Core` lifts straight in, and brings its constraints:

```fsharp
let emailSchema = Schema.string |> Schema.refine Email.refinement

SchemaInfo.constraints emailSchema.Info
// [ Length(Some 3, Some 254); HasFormat Email ]
```

That list is the point of the whole suite. `Etymon.Schema.OpenApi` turns it into
JSON Schema keywords, `Etymon.Schema.Sql` into SQL constraints,
`Etymon.Invariants.FsCheck` into generators — every one of them reading the same
declaration rather than restating it.

## What it covers

Primitives (`string`, `int`, `int64`, `float`, `decimal`, `bool`, `guid`,
`dateTimeOffset`, `dateOnly`, `timeOnly`, `timeSpan`, `bytes`, `raw`),
`nullable`, `list`, `array`, `map`, objects, tagged unions, and `recursive` for
a type that contains itself.

**Optional and nullable are different things**, because JSON Schema, OpenAPI and
TypeScript all treat them differently. `Schema.optional` means the key may be
absent; `Schema.nullable` means the value may be `null`.

**Unions are adjacently tagged** — `{ "kind": "circle", "value": 1 }` — rather
than merging the payload into the outer object. That works whatever shape the
payload has, including a bare number, and the tag can never collide with a
payload field.

**Recursion needs `Schema.recursive`.** Without it, building a self-referential
schema overflows the stack while it is being constructed, rather than failing in
a way that tells you what is wrong.

## Two decode modes

`Strict` is for JSON that arrived as JSON: a string where a number was expected
is a mistake by whoever sent it.

`Coercing` is for the places where everything is a string whether it wants to be
or not — environment variables, query strings, form posts:

```fsharp
Schema.fromJson Schema.int "\"8080\""                          // Error
Schema.fromJsonWith DecodeMode.Coercing Schema.int "\"8080\""  // Ok 8080
```

`Etymon.Config` reads in coercing mode. Coercion never makes nonsense
acceptable: `"eighty"` is still not a number.

## Adopting this over records that came off the wire

If your request types are currently bound by `System.Text.Json`, they are
nullable throughout — `string | null`, `Nullable<int>`, `'a[] | null` — because
a decoder that cannot refuse has to put *something* in every field. A `Schema`
speaks in `option`, so every field crosses.

**One rule makes the conversion mechanical: take the record at its word.** A
field the record declares nullable is optional in the `Schema`; a field it
declares non-nullable is `Schema.required`.

Without that rule the first question on every single type is "should this be
required?", the answer is a judgement each time, and it drifts across a
codebase. With it, the conversion is transcription.

**`WireField` crosses in both directions.** The getter takes the nullable form,
and the value bound in the expression is the nullable form too — so the record is
rebuilt by naming its fields, with no `Option.toObj` at any of them:

```fsharp
let createBill =
    Schema.object "CreateBillRequest" {
        let! billNumber = WireField.text "billNumber" (fun r -> r.BillNumber)
        and! vendorId   = WireField.guid "vendorId" (fun r -> r.VendorId)
        and! lines      = WireField.array "lines" lineSchema (fun r -> r.Lines)
        return { BillNumber = billNumber; VendorId = vendorId; Lines = lines }
    }
```

A crossing that only went one way would remove the smaller half of the work: the
reads would be clean and every field of the `return` would carry a conversion
back. The signatures are annotated for F# nullness, so this compiles unchanged in
a project with `<Nullable>enable</Nullable>` — which is the audience the module
exists for.

**`WireField.text` is not a shorter spelling of `Schema.optional`.** They say
different things: `Schema.optional` says *this key may be absent*, which is a
statement about the contract; `WireField.text` says *this field is nullable
because of how it arrived*, which is a statement about the record, and a
temporary one. They are spelled the same today and they are not the same idea —
so when those records stop being nullable, `WireField` is the list of everywhere
it mattered.

Constraints still apply through the crossing: `WireField.constrainedText` takes a
`ConstraintCheck` and enforces it, which is the point of going through a `Schema`
at all.

**Reach for `WireField` because a field is nullable, never because it is a
collection.** A collection the record declares non-nullable is *required*, and
`WireField.requiredArray` / `WireField.requiredDictionary` say so while still
handling the list ↔ array and map ↔ dictionary crossing:

```fsharp
WireField.array         "lines"      lineSchema    (fun r -> r.Lines)       // 'F[] | null
WireField.requiredArray "roles"      Schema.string (fun p -> p.Roles)       // 'F[]
```

Getting that wrong is quiet. `WireField.array` compiles against a non-nullable
field and hands back a non-null array, so the compiler is satisfied — but the
field is then described as optional, and that reaches the published OpenAPI
document as a nullability clients must handle, and the compatibility snapshot as
an optionality that makes removing the field later look safe when it is
breaking. A tool whose job is refusing unsafe changes must not record the wrong
optionality.

For anything the typed members do not cover — a nested object that may be null,
a string carrying `Schema.sensitive`, a `Schema.raw` payload — `WireField.nullable`
takes any `Schema` at all, and the typed members are specialisations of it:

```fsharp
WireField.nullable "cadence"  cadenceSchema                          (fun r -> r.Cadence)
WireField.nullable "password" (Schema.string |> Schema.sensitive)    (fun r -> r.Password)
```

Two things the nullable collection fields decide for you, both deliberate:

- **A null collection and an absent key are the same thing**, and both read as
  empty. `WireField.array` binds an empty array rather than null, so it assigns
  straight into a nullable field and no call site needs a guard. Where the
  difference between *absent* and *empty* genuinely carries meaning, say so
  explicitly with `Schema.required`.
- **`WireField.dictionary` takes `IDictionary<string, 'V>` and hands back a
  `Dictionary<string, 'V>`.** Taking the interface means a record holding either
  satisfies it; handing back the concrete type means it assigns into the usual
  `Dictionary` field without a cast.

## Design notes

**Encoding cannot fail; decoding accumulates.** A value of type `'T` already is
whatever its type says, so `Schema.toJson` is total. Decoding reads a
`JsonElement` rather than a `Utf8JsonReader` — a forward-only reader cannot
collect errors from fields it has already passed, and in F# it cannot be captured
by a closure at all, which is what a combinator library is made of. So encoding
streams and decoding buffers, deliberately.

**No reflection.** Every combinator is explicit, which means the whole package is
trimming- and AOT-friendly. `Schema.auto` will be a labelled convenience, marked
`RequiresUnreferencedCode`, never the default path.

**Fields are written in declaration order**, and maps in key order, so encoding
the same value twice produces identical bytes. That is what makes a snapshot test
worth having.

**The default writer escapes HTML-sensitive characters**, so a `+` in a timestamp
offset comes out as `+`. Valid JSON, reads back identically, occasionally
startling. Use `Schema.toJsonWith` with
`JavaScriptEncoder.UnsafeRelaxedJsonEscaping` if the output is for a person and
never for a web page. UTC timestamps render as `Z`, so the common case is clean.

## Licence

[MIT](https://github.com/CaelusMinds/Etymon/blob/main/LICENSE).
