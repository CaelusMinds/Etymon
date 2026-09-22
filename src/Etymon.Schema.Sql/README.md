# Etymon.Schema.Sql

**Renders a `Schema` as SQL:** a table model, a diff between two committed
snapshots, and a migration script for PostgreSQL or SQLite.

Part of the [Etymon](https://github.com/CaelusMinds/Etymon) suite, and a sibling
of the other renderers:

| Package | Renders |
| --- | --- |
| `Etymon.Schema.OpenApi` | an OpenAPI document |
| `Etymon.Schema.TypeScript` | TypeScript types |
| **`Etymon.Schema.Sql`** | **SQL** |

One `Schema`, several renderings — the documentation, the client, the database.
Nobody expects `Schema.OpenApi` to serve a document or `Schema.TypeScript` to run
`tsc`, and the same applies here.

```fsharp
// A Schema in, a migration out.
let migration = Migrations.between Dialect.postgres previousSnapshot currentSnapshot
File.WriteAllText("migrations/0007_add_bill_series.sql", migration.Up)
```

## The boundary is a reviewed file

This package **drafts** migrations. Applying them is a **runner**'s job — DbUp,
Flyway, a shell script — and you already have one. The runner owns ordering, the
journal of what has been applied, locking and transactions; that is commodity
work and it is where the liability lives.

So the handover is a `.sql` file in your repository, and that is the design, not
a shortfall:

- **No database driver.** This package depends on `Etymon.Core` and
  `Etymon.Schema` and nothing else. It cannot connect to anything, so it cannot
  do anything to your data by accident.
- **A colleague can object before it runs.** A change that exists as a file in a
  pull request is one somebody can read, question and reject. That is a slower
  loop than "apply on startup" and it is the right one for a database.
- **Your runner's guarantees stay yours.** Nothing here competes with the tool
  that already knows which migrations have run.

## If you use EF Core, use EF Core migrations

Stated first because it is the most likely reason not to install this.

[EF Core][ef] does types-to-migrations too, and does it with far more: navigation
properties and relationships, a migrations history table, `dotnet ef` tooling, a
provider ecosystem. **If EF owns your schema, let it.** Running both means two
things generating DDL from two models, which is precisely the drift this suite
exists to prevent — and you can still take the rest of Etymon for JSON, OpenAPI,
validation and configuration.

This package is for stacks without an ORM: Dapper, raw ADO.NET, F#-first
applications where the alternative today is hand-writing DDL. Against that
baseline it offers immutable F# records rather than mutable entity classes, a
refusal to guess where EF would apply a convention, and SQL you read in a pull
request rather than a generated C# class.

[ef]: https://learn.microsoft.com/ef/core/

## If your tables already say more than a Schema can, keep writing SQL

A `Schema` describes a **value**. A database schema can hold a great deal that no
value description implies: row-level check constraints spanning several columns,
triggers, partial indexes, `COMMENT ON` explaining why a column exists at all.

An application with a mature hand-written database schema is not a candidate for
retrofitting this. You would derive the easy three quarters and hand-maintain the
rest, and now the rules live in two places — which is the drift this suite exists
to prevent, reintroduced by the tool meant to prevent it.

A real example, from an accounting product with 35 tables:

| | Count | Derivable from a `Schema`? |
| --- | ---: | --- |
| `NOT NULL` | 258 | Yes |
| `CHECK` | 65 | About three quarters |
| Foreign keys | 51 | Declared in mapping options |
| Partial indexes | 12 | No |
| Triggers | 9 | No |
| `COMMENT ON` | 37 | No |

The check constraints that do **not** derive are the cross-field row invariants:
`total = subtotal + tax`, `(state = 'sent') = (sent_at is not null)`. No field's
type implies either. (`Etymon.Invariants` expresses rules like these for values
in your application; this package does not render them as SQL.)

**Where this pays is a new application, started `Schema`-first**, before that
divergence accumulates — so that the 258 `NOT NULL`s and most of the 65 checks
were never written by hand in the first place.

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

## Constraint changes are refused, not written

**This version cannot express a constraint change as SQL.** Say a rule tightens
from `Check.length (Some 1) (Some 100)` to `(Some 1) (Some 40)`: the diff sees
it, and refuses:

```sql
-- the rules on invoice.total differ between the two snapshots
-- NOT GENERATED: the rules on invoice.total differ between the two snapshots, and
-- this version cannot express a constraint change. Before: must be at least 0.
-- After: must be between 1 and 1000000. Write the ALTER by hand, or recreate the
-- constraint.
```

It is also listed in `migration.Unsupported`, so a build can fail on it rather
than relying on somebody reading the file:

```fsharp
if not (List.isEmpty migration.Unsupported) then
    failwith "this migration is incomplete; see the NOT GENERATED notes"
```

This used to be silent, and silence was the dangerous part: a changed `CHECK`
produced no change at all, so the script looked complete, the reviewer approved
it, and the database went on enforcing the old rule. A refusal is worse than a
correct answer and far better than nothing.

## Known limitations

- **Constraint changes are reported but not generated.** See the section above;
  this is the one to know about if your tables carry meaningful `CHECK`s.
- **Foreign keys and uniqueness are inputs, not derivations.** A schema cannot
  express them, so they come from `TableOptions`.
- **One schema makes one table.** Modelling a nested record as its own table with
  a foreign key means writing both schemas and both `TableOptions`.
- **PostgreSQL output is generated and reviewed but not yet executed in CI.**
  SQLite output is executed against a real in-memory database in the test suite;
  the equivalent PostgreSQL run needs Testcontainers and is not wired up.

## Public surface

The complete public surface of this package, every value with its full signature
and its documentation, is in `Surface.fsi` beside this file. It is written by the
build from the implementation, so it is where to learn what a function hands
back without compiling anything.

## Licence

[MIT](https://github.com/CaelusMinds/Etymon/blob/main/LICENSE).
