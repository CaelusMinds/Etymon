# Glossary

One definition per term, used everywhere. Where other text in this repository
contradicts this page, the other text is wrong.

This exists because two words each meant two things, and a design discussion went
round in circles for six exchanges before anyone noticed that was the problem.
The cost of an ambiguous term is not confusion in the moment; it is a
conversation where both people are right.

---

## Schema (Etymon) vs database schema

The worst collision in the suite, because both readings are correct English and
they turn up in the same sentence constantly.

**Schema** — an `Etymon.Schema` value: a description of the shape of a value.
Capitalised in prose when it means this. It is the suite's own noun.

**database schema** — the set of tables, constraints and other objects in a
database; also a PostgreSQL namespace, as in `receivables.invoice`. **Never write
bare "schema" for this.** Write "database schema", "the tables", or the namespace
name.

> **The test.** If you can replace the word with *the description*, it is a
> Schema. If you can replace it with *the tables*, write "the tables".

```
Wrong:  the schema is derived from the schema
Right:  the tables are derived from the Schema
```

---

## Migration

**A migration is an artifact, not an act.** It is a file describing a change to a
database, reviewable in a pull request, applied once.

- Producing one is **drafting** it.
- Applying one is **running** it.
- **Etymon drafts. Etymon never runs.**

Do not use "migrate" as a verb for anything an Etymon package does. `Etymon.Schema.Sql`
drafts migrations; your runner runs them.

---

## Runner

The thing that applies migrations to a database: DbUp, Flyway, Alembic, a shell
script. It owns ordering, the journal of what has been applied, locking and
transactions.

**Etymon ships no runner and will not.** It is commodity work, it is where the
liability lives, and every consumer already has one. The boundary between the two
is a reviewed file, and that is a feature: a change that exists as a file in a
pull request is one a colleague can object to before it touches anything.

---

## Snapshot

A recorded state of a model at a point in time, serialised to JSON and committed
to the repository.

Diffing happens **between two snapshots**. A snapshot is the thing that makes a
diff possible without a database — which is what keeps every package in this
suite free of a driver.

---

## Shape

The field structure of one named thing at one version: which fields exist, their
types, which are optional.

An event type, a request body, a response body — the machinery is the same, and
the **policy** on top is what makes it mean something.

Distinct from the **type** (the name) and an **instance** (a row, or a payload
on the wire).

---

## Policy

A set of rules applied to the compatibility matrix, deciding which direction
matters and what can be done about a break. There are two, over one matrix:

- **`Events`** — the old shape is in your database, you own the reader, and an
  upcaster always rescues it.
- **`Wire`** — the old shape is in somebody else's code. Requests can be rescued
  by an upcaster; **responses cannot be rescued at all**, because the code that
  breaks is not code you ship.

Two copies of a compatibility matrix is how the two quietly stop agreeing, so
there is one definition and both policies read it.

---

## Codec

The encode/decode pair that turns a value into stored bytes and back.

**The codec is authoritative for what was written.** Where a hand-written codec
and a type disagree, the codec describes what is actually sitting in the
database. Any tooling that reasons about stored data reasons about codecs, not
types.

---

## Upcaster

A function that reads an older event shape and produces the current one.

**Old events are never rewritten; the reader is taught to understand them.**

Note the direction: an upcaster transforms **on read**. It is a permanent
property of the code, not a one-time change to data — which is exactly why it is
not a migration and must not be modelled as one.

---

## Compatibility, and its direction

Compatibility is directional, and both directions matter. **Never write
"compatible" unqualified.**

**Backward compatible** — new code can read payloads already written. This is
the one you always need; losing it makes history unreadable.

**Forward compatible** — already-deployed code can read payloads written by
newer code. This matters during a rollout, when two versions run against one
log — and on the wire, where the already-deployed code belongs to somebody else
and you cannot update it at all.

| | reads what | breaks when |
| --- | --- | --- |
| Backward | new code ← old payloads | you add a required field with no upcaster |
| Forward | old code ← new payloads | you add a union case old code cannot match |

**In anything a developer reads, name the audience rather than the direction.**
"Forward" and "backward" are the two words people reliably get the wrong way
round. Write *"clients already written (they read what you send)"*, which cannot
be misread.

Compatibility now has an **audience** as well as a direction, and the audience
decides whether a break can be fixed at all: a backward break in your own event
log is an upcaster you write, while a forward break on the wire is code you do
not ship.

---

## Where these came from

Every term here was ambiguous in a real conversation while adopting this suite
into an event-sourced accounting product. They are written down because the
alternative is relearning them, at cost, each time somebody new reads the code.
