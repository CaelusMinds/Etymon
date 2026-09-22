# Why Etymon

Every example below is real. The "without" code is what people actually write,
because most of it is what the author of this library wrote for years. The
"with" code compiles against `0.1.0-preview.10`, and the output is what it
actually prints — you can run most of it from
[`samples/Etymon.Sample.Derivations`](https://github.com/CaelusMinds/Etymon/tree/main/samples/Etymon.Sample.Derivations).

---

## The problem, in one sentence

A rule like *"a booking is between 1 and 30 nights"* is one fact about your
domain, and it ends up written down in five separate places that no compiler
ever compares.

Here is that rule, in a codebase without Etymon:

```fsharp
// 1. The decoder, so a bad request is rejected.
if nights < 1 || nights > 30 then
    Error [ "nights must be between 1 and 30" ]

// 2. The OpenAPI document, so an integrator knows before they send it.
"nights": { "type": "integer", "minimum": 1, "maximum": 30 }

// 3. The database, so a bug cannot write a value the domain forbids.
CONSTRAINT ck_booking_nights CHECK ("nights" >= 1 AND "nights" <= 30)

// 4. The test data, so property tests generate values that are actually legal.
Gen.choose (1, 30)

// 5. The configuration reader, for the default.
match Int32.TryParse raw with
| true, n when n >= 1 && n <= 30 -> Ok n
| _ -> Error "bad nights"
```

Nothing connects them. Change the rule to 1–60 and four of those five are now
lying. The code still compiles, the tests still pass, and the first person to
find out is an integrator whose valid request was rejected — or worse, nobody,
because the database quietly accepted something the domain thinks is impossible.

**This is not a hypothetical failure mode. It is the normal one.** The rules
drift apart slowly, one reasonable commit at a time.

---

## The same rule, declared once

```fsharp
let nights =
    Schema.int |> Schema.constrain (Check.intRange (Some 1) (Some 30))
```

That value reaches all five places, and none of them restate it:

```fsharp
Schema.fromJson bookingSchema payload    // "nights: must be between 1 and 30"
OpenApi.toJsonSchema bookingSchema       // "minimum": 1, "maximum": 30
Mapping.tableOf options bookingSchema    // CHECK ("nights" >= 1 AND "nights" <= 30)
Generate.valid bookingSchema             // only ever generates nights in range
Config.load bookingSchema sources        // "nights: must be between 1 and 30 (from environment)"
```

Change `(Some 30)` to `(Some 60)` and all five change together, because there is
only one of them. That is the whole idea; everything below is a consequence.

---

## Worked examples

### 1. Validation that tells the caller everything at once

**Without.** The shape almost everyone writes first:

```fsharp
let toBooking (dto: BookingDto) =
    if String.IsNullOrWhiteSpace dto.Guest then Error "guest is required"
    elif dto.Nights < 1 then Error "nights must be at least 1"
    elif not (isValidEmail dto.Email) then Error "email is invalid"
    else Ok { ... }
```

Four problems in the request, one reported. The person filling in the form
submits five times and is told one new thing each time.

**With.** The object builder is *applicative*: `let!` then `and!`, never `let!`
twice. That is not a style preference — it makes a field that depends on another
field's value impossible to express, and that impossibility is what guarantees
nothing can short-circuit:

```fsharp
let bookingSchema =
    Schema.object "Booking" {
        let! guest  = Schema.required "guest" (Schema.string |> Schema.constrain (Check.length (Some 1) (Some 80))) (fun b -> b.Guest)
        and! email  = Schema.required "email" (Schema.string |> Schema.constrain (Check.format Format.Email)) (fun b -> b.Email)
        and! room   = Schema.required "room"  (Schema.string |> Schema.constrain (Check.oneOf [ "single"; "double"; "suite" ])) (fun b -> b.Room)
        and! nights = Schema.required "nights" (Schema.int |> Schema.constrain (Check.intRange (Some 1) (Some 30))) (fun b -> b.Nights)
        return { Guest = guest; Email = email; Room = room; Nights = nights }
    }
```

Actual output for a request with five things wrong:

```
id: must be a valid uuid
guest: must be between 1 and 80 characters
email: must be a valid email
room: must be one of: single, double, suite
nights: must be between 1 and 30
```

**Why it helps.** Every error carries a *path*, not a flat field name:
`lines[2].quantity`, `address.zip`. A form can highlight the right box without
parsing a sentence. One round trip instead of five.

---

### 2. An OpenAPI document that cannot drift

**Without.** Two common approaches, both of which rot:

- *Hand-written YAML.* It is correct on the day it is written. Then a field is
  renamed and nobody remembers the file exists.
- *Reflection over your DTO type.* Better — a renamed field changes the
  document. But the type does not know that `room` is one of three strings, so
  the document says `"type": "string"` and the integrator finds out by being
  rejected.

**With.** The document is derived from the same value that does the rejecting:

```json
"room": { "type": "string", "enum": [ "single", "double", "suite" ] },
"nights": { "type": "integer", "format": "int32", "minimum": 1, "maximum": 30 },
"guest": { "type": "string", "minLength": 1, "maxLength": 80,
           "description": "Name the booking is held under" }
```

**Why it helps.** The document cannot describe a rule the server does not
enforce, and the server cannot enforce a rule the document omits, because
there is one rule. Your integrator's generated client rejects the bad value
before it leaves their process.

---

### 3. A database schema that agrees with the domain

**Without.** Hand-written DDL, or an ORM that infers it from mutable entity
classes. Either way the `CHECK` constraints are usually missing entirely,
because writing them by hand is tedious and nobody checks that they match.

**With.**

```sql
CREATE TABLE "booking" (
    "id" uuid NOT NULL,
    "guest" varchar(80) NOT NULL,
    "room" text NOT NULL,
    "nights" integer NOT NULL,
    CONSTRAINT "pk_booking" PRIMARY KEY ("id"),
    CONSTRAINT "ck_booking_guest_0" CHECK (length("guest") >= 1 AND length("guest") <= 80),
    CONSTRAINT "ck_booking_room_0" CHECK ("room" IN ('single', 'double', 'suite')),
    CONSTRAINT "ck_booking_nights_0" CHECK ("nights" >= 1 AND "nights" <= 30)
);
```

Note `varchar(80)` — the length constraint became the column width, not just a
check. You review this SQL in a pull request. It is not a generated C# class you
have to read a migration runner to understand.

**Why it helps.** The database is the last line of defence, and it is the one
that matters when a background job or a manual `UPDATE` bypasses your validation
layer entirely.

> **Read this before adopting it.** If EF Core owns your schema, keep using EF
> migrations. Two things generating DDL is two sources of truth, which is the
> exact drift this suite exists to prevent. `Etymon.Schema.Sql` is for stacks
> without an ORM — Dapper, raw ADO.NET — where the alternative is hand-writing
> the DDL anyway.

---

### 4. Test data that is valid by construction

**Without.**

```fsharp
let genBooking = gen {
    let! nights = Gen.choose (1, 30)       // the rule, copied
    let! room = Gen.elements [ "single"; "double"; "suite" ]   // and again
    ...
}
```

The generator is a fourth copy of the rules. When the rule changes, the
generator keeps producing values the schema now rejects, and your property test
fails for a reason that has nothing to do with the property.

**With.**

```fsharp
Generate.valid bookingSchema        // only values the schema accepts
Generate.invalid bookingSchema      // values it should reject, and why
```

**Why it helps.** The generator cannot disagree with the validator, because it
is derived from it. And `Generate.invalid` gives you the negative cases, which
is the half people skip.

---

### 5. Configuration that says what is wrong and where it came from

**Without.**

```
Unhandled exception: System.FormatException: The input string 'forty-five' was not in a correct format.
```

Which setting? Which file? Was it the environment variable or the JSON?

**With.**

```
1 configuration problem:
  nights: must be between 1 and 30 (from environment)

Explaining where each setting would come from:
room    double  appsettings.json
nights  45      environment
```

**Why it helps.** "Which source won" is the actual question when configuration
is wrong, and `Config.explain` answers it without starting the application.
Anything wrapped in `Secret<'T>` redacts itself through `ToString`, `%O`, `%A`
and `string`, so it cannot reach a log by accident.

---

### 6. HTTP endpoints described once

**Without.** The path string appears in the route registration and again in the
OpenAPI catalogue. The request type is named in the handler and again in the
document. The client repeats the URL a third time. Nothing enforces agreement.

**With.** An endpoint is a value, and everything else interprets it:

```fsharp
let getUser =
    Api.get "getUser" (Route.one [ "users" ] (Param.guid "id") []) userSchema
    |> Api.failsWith 404 problemSchema "No user with that id"

// Serve it — on Giraffe, or on minimal APIs:
app |> MinimalApi.map getUser (fun userId _ctx -> task { ... })

// Call it. The URL is built from the same Route the server matches with:
match! Client.call http getUser userId with
| Ok user -> ...
| Error (ApiError.Failed f) when f.Declared -> ...   // a status the endpoint admits to
```

**Why it helps.** The client cannot request a path the server does not serve. A
path value that does not parse never reaches your handler. A status the endpoint
never declared is refused rather than forwarded, because it would be absent from
the document and unknown to every generated client.

The end-to-end tests assert that the Giraffe adapter and the minimal-API adapter
produce **byte-identical responses**, which is the claim made concrete.

---

## What it costs

A schema is slower than a codec you wrote by hand. Decoding an eight-field
record, measured with [`bench/Etymon.Benchmarks`](https://github.com/CaelusMinds/Etymon/tree/main/bench/Etymon.Benchmarks):

| | Time | Allocated | Validates |
| --- | ---: | ---: | :---: |
| Hand-written `JsonDocument` loop | 557 ns | 336 B | no |
| `System.Text.Json` (reflection) | 389 ns | 400 B | no |
| **Etymon** | **1,606 ns** | **1,344 B** | **yes** |

About three times the time of a hand-written codec that checks nothing, while
applying an email format, two length bounds and a range, and producing every
failure with a path.

Reproduce it:

```bash
dotnet run --project bench/Etymon.Benchmarks -c Release -- --filter '*Decoding*'
```

**Where that is the wrong trade.** A hot inner loop deserialising millions of
trusted internal messages. Use the hand-written codec; that is what it is for.

**Where it is the right one.** An API boundary, a configuration file, a request
from someone you do not control — anywhere the cost is dominated by the network
and the value of being right is high.

---

## When *not* to use this

Said plainly, because a library that claims to suit everything suits nothing:

- **You only need one of the derivations.** If all you want is a JSON codec,
  [Thoth.Json](https://github.com/thoth-org/Thoth.Json) is excellent and you
  should use it. The reason to take Etymon is that one declaration reaches
  several places.
- **EF Core owns your database.** Use EF migrations.
- **You want reflection-based derivation from existing types.** Etymon is
  combinator-first on purpose — no reflection, trimming- and AOT-friendly, small
  enough for a Blazor WebAssembly payload. That means you write the schema.
- **You need a web framework.** Etymon is not one and will not become one. It
  adapts to Giraffe or ASP.NET Core; it does not replace them.

---

## Safe under input you did not choose

If Etymon validates your API's requests, an attacker picks its input.

- **Rejection is linear in the input size.** This was not always true:
  accumulating errors used to be quadratic, and 200,000 bad array elements — an
  800 KB body — took five minutes of CPU. Found by measuring, fixed, and pinned
  by a test that fails if it regresses.
- **Deep nesting is refused, not crashed.** A recursive decoder walking a
  deliberately deep value is how a stack overflow happens, and a stack overflow
  cannot be caught — it takes the process with it.
- **What is still yours to bound.** Reporting every problem means a request with
  10,000 bad elements produces 10,000 errors. That is the promise, so the cap
  belongs at your boundary; Etymon will not silently truncate.

---

## Commitments

**It keeps up with .NET.** Every package targets `net8.0` and `net10.0` today,
and new targets are added as .NET ships them. `FSharp.Core` is pinned at the
lowest version asked of consumers (8.0.100) and compiled against it, so the
library cannot accidentally use an API newer than its own declared floor. Every
test runs on both target frameworks, on Windows, macOS and Linux, on every
commit — twice already a bug existed on exactly one of those and nowhere else.

**It is built against a real application.** Etymon is developed alongside
[Keel](https://github.com/CaelusMinds), an accounting and payroll platform, which
is its first and largest consumer. Keel is being built for production and is
adopting the suite across its contract and HTTP layers. That relationship is the
reason several things in here exist, and the reason several bugs were found:
the dependency that would have put file-IO code into a browser payload, and the
missing error-code mechanism that would have made adoption a breaking change for
Keel's own integrators, were both found by a real consumer rather than by
reading the code.

**Previews move, and say so.** While the suite is `0.x` the API can change
between previews, and it does. [`CHANGELOG.md`](../CHANGELOG.md) lists every
breaking change per version, at the top, with what to do about it. Every package
ships the same version number, so "which versions go together" is never a
question.

**Nothing is claimed that is not measured.** The performance figures come from a
benchmark you can run, the trimmed size from `eng/trim-size.sh`, and the
cross-platform results from CI. Three numbers in earlier versions of this
documentation were wrong; they were found by measuring and corrected, which is
the only reason to quote numbers at all.

---

## Getting started

```xml
<PackageReference Include="Etymon" Version="0.1.0-preview.10" />
```

Previews are not restored unless you ask for the version by name. Take the
`Etymon` meta-package for everything that costs nothing but `FSharp.Core`, or
just the one package you need — see the
[package table](https://github.com/CaelusMinds/Etymon#packages).

Then run the sample, which is this entire document in a file you can execute:

```bash
dotnet run --project samples/Etymon.Sample.Derivations
```
