# Etymon.Migrations

**A relational model derived from your schema, diffed into SQL you can review in
a pull request.**

Part of the [Etymon](https://github.com/CaelusMinds/Etymon) suite. Depends on
`Etymon.Core` and `Etymon.Schema`, and on **no database driver at all** — it
generates SQL text and executes nothing, so a project can produce and review
migrations without taking on Npgsql.

## Ambiguity is an error, not a guess

This is the design decision the whole package turns on.

A schema describes a value on a wire. A table describes a row in a database.
Where the two do not line up, Etymon refuses rather than choosing:

```fsharp
Mapping.tableOf (Mapping.keyedBy "id") personSchema
// Error [ NestedNotDecided "address" ]
```
```
'address' is a nested object, and the options did not say what to do with it.
It could be flattened into columns, stored as JSON, or omitted. Set
TableOptions.Nested.
```

There is no right answer to that question. A nested address is three more
columns in one application, a `jsonb` document in another, and a foreign key to
its own table in a third. A default would be wrong often enough to poison trust
in everything else the suite generates — so the decision is an input:

```fsharp
{ Mapping.keyedBy "id" with Nested = Some (NestedStrategy.Flatten "_") }
// id, name, address_street, address_zip
```

The same applies to primary keys. Etymon will not invent one, because a table
without a key is a problem worth being told about. Every undecided question is
reported at once, not one at a time.

## Your schema's rules become the database's rules

```fsharp
Schema.required "name" (Schema.string |> Schema.constrain (Check.length (Some 1) (Some 100)))
Schema.required "age"  (Schema.int    |> Schema.constrain (Check.intRange (Some 0) (Some 130)))
```
```sql
CREATE TABLE "Person" (
    "id" text NOT NULL,
    "name" varchar(100) NOT NULL,
    "age" integer NOT NULL,
    "email" text,
    CONSTRAINT "pk_Person" PRIMARY KEY ("id"),
    CONSTRAINT "ck_Person_name_0" CHECK (length("name") >= 1 AND length("name") <= 100),
    CONSTRAINT "ck_Person_age_0" CHECK ("age" >= 0 AND "age" <= 130)
);
```

Nothing was restated. The length bound that makes the column `varchar(100)` is
the same one the JSON decoder enforces and the same one the OpenAPI document
advertises.

## Destructive changes are flagged, not executed

By default a destructive statement is written as a **comment**:

```sql
-- drop the column Person.nickname, losing whatever it holds
-- DESTRUCTIVE: review, then uncomment to apply.
-- ALTER TABLE "Person" DROP COLUMN "nickname";
```

The failure this package exists to prevent is a destructive statement that ran
because nobody read it. Pass `IncludeDestructive = true` when you have read it.

Etymon errs toward calling a change destructive: an unnecessary review costs a
minute, a silent truncation costs a support ticket a month later that nobody
connects to a migration. Narrowing a type counts. So does adding a `NOT NULL` or
a uniqueness rule, because both fail against rows that already exist.

**A down migration is absent rather than wrong.** One irreversible change makes
the whole script irreversible, because a partial down migration is a trap — and
recreating a dropped table's shape does not bring back its rows.

## Dialects refuse what they cannot do

```
-- widen the type of Person.name from VarChar 100 to Text
-- NOT GENERATED: SQLite cannot change a column's type. Changing Person.name
-- means rebuilding the table, copying the data and swapping it in -- which
-- depends on data Etymon has never seen, so it will not guess at the statements.
```

PostgreSQL has a regular-expression operator, so a `Pattern` constraint becomes a
real `CHECK` there. SQLite does not, so the rule stays documented rather than
faked. Adding a `NOT NULL` column without a default is refused in every dialect,
because it fails on any table with rows in it.

## Snapshots belong in your repository

```fsharp
File.tryWriteAllText "migrations/0003.snapshot.json" (Migrations.toJson snapshot)
Migrations.between Dialect.postgres previous current
```

A migration that exists as a file in a pull request is one a colleague can object
to. A migration that exists only as something that happened to a database at four
in the morning is not.

The snapshot format is itself described by an `Etymon.Schema`, so it is
round-trip tested by the same machinery as everything else, and a malformed
snapshot is reported as `tables[0].columns[2].name: is required` rather than a
stack trace.

## Known limitations

- **Diffing does not yet compare constraints.** They are recorded in the
  snapshot, so the data is there, but a changed `CHECK` will not appear as a
  change. Column additions, removals, type changes, nullability and uniqueness
  all do.
- **Foreign keys and uniqueness are inputs, not derivations.** A schema cannot
  express them, so they come from `TableOptions`.
- **One schema makes one table.** Modelling a nested record as its own table with
  a foreign key means writing both schemas and both `TableOptions`.
- **PostgreSQL output is generated and reviewed but not yet executed in CI.**
  SQLite output is executed against a real in-memory database in the test suite;
  the equivalent PostgreSQL run needs Testcontainers and is not wired up.

## Licence

[MIT](https://github.com/CaelusMinds/Etymon/blob/main/LICENSE).
