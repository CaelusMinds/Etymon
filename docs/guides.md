# Guides

## Describing a type

A schema is one value that knows how to write a type, read it, validate it and
describe it. The object builder is applicative — `let!` then `and!`, never
`let!` twice — which is not a stylistic choice: it makes a field that depends on
another field impossible to express, and that is what guarantees every field is
checked and every error is reported.

```fsharp
let personSchema =
    Schema.object "Person" {
        let! name =
            Schema.required "name" (Schema.string |> Schema.constrain Check.nonEmpty) (fun p -> p.Name)
        and! age =
            Schema.required "age" (Schema.int |> Schema.constrain (Check.intRange (Some 0) (Some 130))) (fun p -> p.Age)
        and! email = Schema.optional "email" Schema.string (fun p -> p.Email)
        return { Name = name; Age = age; Email = email }
    }
```

Four bad fields produce four errors, each carrying a path like `address.zip` or
`items[3].name`.

### Optional is not nullable

`Schema.optional` means the key may be absent. `Schema.nullable` means the key is
there and its value may be `null`. They are different in JSON, different in JSON
Schema, and different in TypeScript (`?:` against `| null`), so they are kept
different here. Conflating them is the commonest way a generated type ends up
lying about its payload.

## Constraints are data

`Check.length`, `Check.oneOf`, `Check.intRange` and the rest do not close over a
predicate — they record a `Constraint` value in `Etymon.Core` that five packages
read. That is why one declaration reaches the OpenAPI document, the `CHECK`
clause, the generator and the TypeScript literal union.

A rule Etymon cannot inspect becomes `Constraint.Opaque`: still enforced, still
named in the documentation, never half-derived into something that looks right
and is not.

## Rules no single field can hold

"Departure must be after arrival" belongs to the value, not to a field, so it
cannot live in the schema. It lives in `Etymon.Invariants` and is checked beside
the schema:

```fsharp
let invariants =
    Invariants.forType "Booking" [
        Invariant.after ("departs", fun b -> b.Departs) ("arrives", fun b -> b.Arrives)
    ]

Schema.fromJson bookingSchema payload |> Invariants.andThen invariants
```

## Endpoints are values

An `Endpoint` is a declaration — verb, typed route, request schema, response
schemas, declared failures — and nothing in it performs HTTP. The adapters
interpret it:

- `Etymon.Api.Giraffe` turns it into an `HttpHandler` that composes into the
  `choose` you already have.
- `Etymon.Api.AspNetCore` registers it on minimal-API routing.
- `Etymon.Api.Client` calls it, building the URL from the same `Route` value the
  server matches with.
- `ApiOpenApi` writes the document.

One declaration, several interpreters. The end-to-end tests assert the two server
adapters produce byte-identical responses, because that is the claim.

A status the endpoint never declared is refused rather than forwarded: it would
be absent from the document and unknown to the generated client, which makes it
a bug in the handler rather than something to pass on.

## Migrations, and when not to use them

`Etymon.Schema.Sql` derives a table from a schema, diffs two snapshots and writes
SQL you review in a pull request. **If EF Core owns your schema, use EF
migrations.** Two things generating DDL means two sources of truth, which is the
drift this suite exists to prevent. These migrations are for stacks without an
ORM — Dapper, raw ADO.NET — where the alternative is hand-writing DDL.

Where a derivation would have to guess — a nested record could be a foreign key,
flattened columns, or `jsonb` — Etymon returns an error naming the choice instead
of picking one you would discover in production.

## Configuration

`Etymon.Config` reads configuration through a schema, in `DecodeMode.Coercing`,
because configuration arrives as text whatever it means. It reports every problem
at once, says what was expected, and names the source each value came from.

```fsharp
Config.load configSchema [ Source.jsonFile "appsettings.json"; Source.environment "APP_" ]
// nights: must be between 1 and 30 (from environment)
```

`Config.explain` prints where each setting would come from without loading
anything, which is usually the question being asked when configuration is wrong.

Anything wrapped in `Secret<'T>` redacts itself through `ToString`, `%O`, `%A`
and `string`, so it cannot reach a log by accident.
