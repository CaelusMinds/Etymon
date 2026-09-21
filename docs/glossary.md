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

## Event shape

The field structure of one event type at one version: which fields exist, their
types, which are optional.

Distinct from the **event type** (the name) and an **event instance** (a row).

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

**Backward compatible** — new code can read events already written. This is the
one you always need; losing it makes history unreadable.

**Forward compatible** — older deployed code can read events written by newer
code. This matters during a rollout, when two versions run against one log.

| | reads what | breaks when |
| --- | --- | --- |
| Backward | new code ← old events | you add a required field with no upcaster |
| Forward | old code ← new events | you add a union case old code cannot match |

---

## Where these came from

Every term here was ambiguous in a real conversation while adopting this suite
into an event-sourced accounting product. They are written down because the
alternative is relearning them, at cost, each time somebody new reads the code.
