# Etymon.Schema

Define the type once. Derive everything else.

Part of the [Etymon](https://github.com/CaelusMinds/Etymon) suite. Depends on
`Etymon.Core` and `Etymon.Base`, and on no NuGet package at all beyond
`FSharp.Core` — `System.Text.Json` ships in the shared framework for both target
frameworks, so it is an in-box API here rather than a dependency you inherit.

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
JSON Schema keywords, `Etymon.Migrations` into SQL constraints,
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
