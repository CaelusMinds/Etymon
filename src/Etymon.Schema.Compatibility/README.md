# Etymon.Schema.Compatibility

**Is this change safe against the payloads that already exist?**

Part of the [Etymon](https://github.com/CaelusMinds/Etymon) suite. Depends on
`Etymon.Core` and `Etymon.Schema`, and on nothing else.

## One matrix, two policies

Underneath is a capability that mentions no storage at all: a shape snapshot
committed to the repository, a diff between two snapshots, and a compatibility
matrix reported in both directions. On top of it sit two policies, because the
same change means different things depending on where the old shape lives.

| | `Events` | `Wire` |
| --- | --- | --- |
| The old shape lives in | your database | somebody else's code |
| Direction that matters | backward | backward for requests, forward for responses |
| The fix | an upcaster | an upcaster for requests; **nothing** for responses |
| Can you count what is affected | yes — query the log | no |

**The matrix has exactly one definition and both policies read it.** Two copies
of a compatibility table is how the two quietly stop agreeing, and a
disagreement there is one nobody notices until it matters.

## The problem it started from

In an event-sourced system the tables barely change. The event table has the
columns it will always have, and no migration tool has anything to say — because
no column moved.

But something did change, and it is the dangerous kind: **the events already
written are in the old shape, and they cannot be rewritten.** The code now has
to read two shapes, and nothing checks that it can.

Event sourcing moves the schema off the columns and into the payload. It is
still schema, and it still wants deriving — which is why this sits beside
`Etymon.Schema.Sql` and `Etymon.Schema.OpenApi`. It describes shape; it does not
describe a change to a database.

The wire policy is the same observation about a different boundary: an API's
request and response bodies are shapes too, and the ones already in clients'
code cannot be rewritten either.

## The hard rule

**It never emits SQL, never opens a connection, and never proposes rewriting an
event.** Not behind a flag, not as an option.

Migrating an event store does not mean rewriting events. The only legitimate
moves are **upcasting** — leave history alone, transform on read — and, rarely,
copy-forward: write a new stream and keep the old one. A tool that offered to
"fix up the old rows" would be offering to destroy the only irreplaceable thing
in the system.

## What it does

```fsharp
// A shape, derived from the Schema your codec is built from.
let raisedV2 = Shape.ofSchema "InvoiceRaised" 2 invoiceRaisedSchema

// A snapshot, committed to the repository.
File.WriteAllText("events.snapshot.json", ShapeSnapshots.toJson (ShapeSnapshot.of' shapes))

// Two snapshots in, verdicts out. Never a database.
let changes = Compatibility.between renames previousSnapshot currentSnapshot
printfn "%s" (Compatibility.report changes)
```

```
  InvoiceRaised: the required field 'dueOn' was added.
    Backward (new code reading events already written): events already written
    have no 'dueOn', so an upcaster must supply one.
```

## Compatibility has a direction, and both matter

**Never write "compatible" unqualified.**

- **Backward** — new code can read events already written. The one you always
  need; losing it makes history unreadable.
- **Forward** — already-deployed code can read events written by newer code.
  Matters during a rollout, when two versions run against one log.

| Change | Backward | Forward |
| --- | --- | --- |
| added optional field | compatible | compatible |
| **added required field** | **breaking** — needs an upcaster supplying a default | compatible |
| removed optional field | compatible | compatible |
| **removed required field** | compatible | **breaking** |
| **changed a field's type** | **breaking** | **breaking** |
| narrowed a constraint | **breaking** — old events may violate it | compatible |
| widened a constraint | compatible | **breaking** |
| added a union case | compatible | **breaking** — old code cannot read it |
| removed a union case | **breaking** — old events carry it | compatible |

Constraint changes are reported in **both** directions, because this compares
rules rather than interpreting them: whether a change narrowed or widened is a
judgement, and the package says so rather than guessing.

## Renames are declared, never detected

A rename is indistinguishable from a removal plus an addition, and the
difference decides whether stored events can be read. So it refuses:

```
  'InvoiceRaised' v2 removes 'total' and adds 'amount'. That is either a rename
  or separate changes, and the difference decides whether stored events can be
  read. Declare it with Rename ("total", "amount"), or confirm they are separate.
```

```fsharp
let renames = [ { Event = "InvoiceRaised"; From = "total"; To = "amount" } ]
```

## Every stored version must be readable

An upcaster declares the version it reads and the version it produces. The check
is pure graph reachability: from every version in the snapshot, can a chain of
upcasters reach the current one?

```
  'InvoiceRaised' has stored shapes at v1 and v2 and v3, and the current shape is
  v3. Upcasters exist for v2 -> v3. Nothing reads v1.
```

**It cannot write the upcaster for you.** "What due date should a 2024 invoice
get?" is a business judgement. The package demands one and proves the chain is
complete, which is all of what is derivable and most of the value.

## The wire policy: responses are the ones that cannot be rescued

An event's old shape sits in your database, so an upcaster can always rescue it.
A DTO's old shape sits in somebody else's code, and whether it can be rescued
depends entirely on which way it travels.

**Requests** — old clients send old bodies and you read them. Backward
compatibility, and the event policy's answer applies almost unchanged: write an
upcaster.

**Responses** — you send new bodies and their code reads them. Forward
compatibility, and **there is no upcaster to write, because the code that would
run it is not code you ship.** The only answers are do not make the change, or
version the endpoint. This package offers no hook for a response fix, because a
hook that cannot help is worse than an honest refusal: it suggests the problem
has been handled.

That is why the verdict is the product here. It is worth something only at the
moment before shipping.

```fsharp
match Wire.responses renames previous current |> Wire.unfixable with
| [] -> ()
| breaks -> failwith (Wire.report breaks)
```

```
  InvoiceDto: the field 'taxMinorUnits' was removed.
    Clients already written (they read what you send): a client reading
    taxMinorUnits will find it missing. Nothing you ship can fix this, because
    the code that breaks is theirs. Version the endpoint, or keep the shape as
    it was.

  CreateBillRequest: the required field 'periodStart' was added.
    Clients already written (they send what you read): bodies already in the
    wild carry no periodStart. An upcaster must take the old body and produce
    the current one.
```

Verdicts name the **audience**, not the direction. "Forward" and "backward" are
the two words people reliably get the wrong way round, and this is read by
somebody deciding whether to ship.

## Where the snapshot file belongs

**Not beside your SQL migrations.** That is the mistake to avoid; the right
neighbour depends on which policy it serves.

| Policy | File lives | Because |
| --- | --- | --- |
| `Events` | beside the **codecs** | the codec is what wrote the bytes |
| `Wire` | beside the **request and response schemas** | those describe what goes on the wire |

In both cases the snapshot changes in the same pull request as the change that
moved the shape, so a colleague reviews the verdict and the cause together. The
migrations describe the tables, which in an event-sourced system barely change
at all.

The format is itself described by an Etymon `Schema`, so it round-trips through
the same codec as everything else and a malformed file says where:

```
shapes[2].fields[0].name: is required
```

## Deliberately out of scope

- **Counting affected events.** "4,110 stored events lack this field" is useful
  and needs a database driver. A provider package may offer it later; the core
  stays pure and testable without infrastructure, like its siblings.
- **Counting affected clients — and there is no later for this one.** With
  events you can query the log. There is no equivalent on the wire: you do not
  know who your clients are or which version they are on. **Every wire verdict
  is categorical and never quantified.** No numbers are coming.
- **Deciding whether to version the endpoint.** Whether a break is worth a new
  API version, a deprecation window or a refusal is a product judgement. The
  package says what would break and for whom.
- **Writing upcasters.** A business judgement, not a derivation.
- **Knowing whether removing a field is safe** when something outside the
  application reads the log. It cannot see those readers.

## Licence

[MIT](https://github.com/CaelusMinds/Etymon/blob/main/LICENSE).
