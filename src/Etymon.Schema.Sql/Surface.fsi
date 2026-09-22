
namespace FSharp



namespace Etymon
    
    /// <summary>
    /// The type of a column, named abstractly so that one model can describe a table
    /// in more than one dialect.
    /// </summary>
    /// <remarks>
    /// Deliberately small. Every case here is one that Etymon can derive from a
    /// Schema and that every supported dialect can express. A type that only one
    /// database has belongs in an override, not in this list.
    /// </remarks>
    [<RequireQualifiedAccess>]
    type SqlType =
        
        /// Unbounded text.
        | Text
        
        /// Text with a maximum length, from a length constraint.
        | VarChar of length: int
        
        /// A 32-bit integer.
        | Integer
        
        /// A 64-bit integer.
        | BigInt
        
        /// An exact decimal.
        | Numeric of precision: int * scale: int
        
        /// Double-precision floating point.
        | Double
        
        /// A boolean.
        | Boolean
        
        /// A UUID.
        | Uuid
        
        /// A timestamp with a time zone.
        | Timestamp
        
        /// A calendar date.
        | Date
        
        /// A wall-clock time.
        | Time
        
        /// A duration.
        | Interval
        
        /// Binary data.
        | Binary
        
        /// A JSON document, for a value whose shape the table does not model.
        | Json
    
    /// One column of a table.
    type Column =
        {
          
          /// The column name.
          Name: string
          
          /// What it holds.
          Type: SqlType
          
          /// Whether it accepts nulls.
          Nullable: bool
          
          /// A literal default, already in SQL form, or none.
          Default: string option
          
          /// The rules the value must satisfy, in the one vocabulary from
          /// <c>Etymon.Core</c>. Each dialect renders what it can express as a
          /// CHECK and ignores the rest; nothing is restated here.
          Constraints: Constraint list
        }
    
    /// A reference from one table to another.
    type ForeignKey =
        {
          
          /// The constraint name.
          Name: string
          
          /// The columns in this table.
          Columns: string list
          
          /// The table referred to.
          ReferencesTable: string
          
          /// The columns referred to.
          ReferencesColumns: string list
        }
    
    /// A uniqueness rule over one or more columns.
    type UniqueConstraint =
        {
          
          /// The constraint name.
          Name: string
          
          /// The columns that must be unique together.
          Columns: string list
        }
    
    /// One table.
    type Table =
        {
          
          /// The table name.
          Name: string
          
          /// Its columns, in declaration order.
          Columns: Column list
          
          /// The columns forming the primary key. Empty means the table has none,
          /// which Etymon reports rather than inventing one.
          PrimaryKey: string list
          
          /// Uniqueness rules beyond the primary key.
          Unique: UniqueConstraint list
          
          /// References to other tables.
          ForeignKeys: ForeignKey list
        }
    
    /// <summary>
    /// The tables, at a point in time.
    /// </summary>
    /// <remarks>
    /// Written to a file in your repository and committed, so that a migration is
    /// something a reviewer can read in a pull request rather than something that
    /// happened to a database at four in the morning.
    /// </remarks>
    type Snapshot =
        {
          
          /// A version marker for the snapshot format itself, so that a future
          /// change to it can be detected rather than misread.
          FormatVersion: int
          
          /// The tables, in name order.
          Tables: Table list
        }
    
    /// Inspecting the relational model.
    [<RequireQualifiedAccess>]
    module SqlModel =
        
        /// <summary>The snapshot format this version of Etymon writes and reads.</summary>
        /// <example><code lang="fsharp">
        /// SqlModel.formatVersion // 1
        /// </code></example>
        val formatVersion: int
        
        /// <summary>An empty snapshot: what a database looks like before anything exists.</summary>
        /// <example><code lang="fsharp">
        /// Migrations.plan SqlModel.emptySnapshot current // every table is created
        /// </code></example>
        val emptySnapshot: Snapshot
        
        /// <summary>A snapshot of some tables, in a stable order.</summary>
        /// <example><code lang="fsharp">
        /// SqlModel.snapshotOf [ personTable; addressTable ]
        /// </code></example>
        val snapshotOf: tables: Table list -> Snapshot
        
        /// <summary>A column by name, if the table has one.</summary>
        /// <example><code lang="fsharp">
        /// SqlModel.tryColumn "email" personTable
        /// </code></example>
        val tryColumn: name: string -> table: Table -> Column option
        
        /// <summary>A table by name, if the snapshot has one.</summary>
        /// <example><code lang="fsharp">
        /// SqlModel.tryTable "person" snapshot
        /// </code></example>
        val tryTable: name: string -> snapshot: Snapshot -> Table option
        
        /// <summary>
        /// Whether changing one column type to another can lose data.
        /// </summary>
        /// <remarks>
        /// Widening is safe; narrowing is not, and neither is changing to an
        /// unrelated type. Etymon errs toward calling a change destructive, because
        /// the cost of an unnecessary review is a minute and the cost of a silent
        /// truncation is a support ticket a month later.
        /// </remarks>
        /// <example><code lang="fsharp">
        /// SqlModel.narrows (SqlType.VarChar 200) (SqlType.VarChar 50) // true
        /// SqlModel.narrows SqlType.Integer SqlType.BigInt // false
        /// </code></example>
        val narrows: before: SqlType -> after: SqlType -> bool

namespace Etymon
    
    /// <summary>
    /// What to do with a nested object inside a record being mapped to a table.
    /// </summary>
    /// <remarks>
    /// There is no right answer, which is exactly why Etymon will not pick one. A
    /// nested address could be three more columns, a JSON document, or a row in
    /// another table with a foreign key — and each of those is correct for some
    /// application and wrong for the rest.
    /// </remarks>
    [<RequireQualifiedAccess>]
    type NestedStrategy =
        
        /// Store the nested object as a JSON document in one column.
        | AsJson
        
        /// Give each leaf its own column, named with the parent prefixed.
        | Flatten of separator: string
        
        /// Leave it out of the table entirely.
        | Omit
    
    /// <summary>
    /// What to do with a list inside a record being mapped to a table.
    /// </summary>
    /// <remarks>
    /// Same problem as nesting, with the same answer: a list of tags could be a JSON
    /// array, a delimited string or a join table, and Etymon does not know which one
    /// this is.
    /// </remarks>
    [<RequireQualifiedAccess>]
    type CollectionStrategy =
        
        /// Store the whole list as a JSON array in one column.
        | AsJson
        
        /// Leave it out of the table entirely.
        | Omit
    
    /// <summary>
    /// The decisions Etymon will not make for you.
    /// </summary>
    /// <remarks>
    /// A Schema says nothing about primary keys, foreign keys, or how
    /// a nested value should be stored. Deriving a table from one therefore means
    /// guessing, and a guess that is wrong often enough poisons trust in everything
    /// else the suite generates. So the ambiguous decisions are inputs, and leaving
    /// one out is an error rather than a default.
    /// </remarks>
    type TableOptions =
        {
          
          /// The table name. Defaults to the Schema's name when absent.
          TableName: string option
          
          /// The columns forming the primary key. Required: Etymon will not invent
          /// one, and a table without a key is a problem worth being told about.
          PrimaryKey: string list
          
          /// What to do with nested objects. Required only if the Schema has any.
          Nested: NestedStrategy option
          
          /// What to do with lists. Required only if the Schema has any.
          Collections: CollectionStrategy option
          
          /// Column types to use instead of the derived one, by column name.
          TypeOverrides: Map<string,SqlType>
          
          /// Uniqueness rules, which a Schema cannot express.
          Unique: UniqueConstraint list
          
          /// References to other tables, which a Schema cannot express either.
          ForeignKeys: ForeignKey list
        }
    
    /// Why a Schema could not be turned into a table.
    [<RequireQualifiedAccess>]
    type MappingError =
        
        /// The Schema has a nested object and the options did not say what to do
        /// with it.
        | NestedNotDecided of path: string
        
        /// The Schema has a list and the options did not say what to do with it.
        | CollectionNotDecided of path: string
        
        /// No primary key was given.
        | NoPrimaryKey of table: string
        
        /// The primary key names a column the table does not have.
        | UnknownKeyColumn of table: string * column: string
        
        /// The Schema is not a record, so there is nothing to make a table from.
        | NotATable of what: string
        
        /// The schema contains something no table can hold.
        | Unsupported of path: string * what: string
    
    /// Turning a <see cref="T:Etymon.Schema`1"/> into a <see cref="T:Etymon.Table"/>.
    [<RequireQualifiedAccess>]
    module Mapping =
        
        /// <summary>
        /// Options that decide nothing. Start here and supply what your schema needs;
        /// anything left undecided that the schema actually uses is reported.
        /// </summary>
        /// <example><code lang="fsharp">
        /// { Mapping.options with PrimaryKey = [ "id" ]; Nested = Some (NestedStrategy.Flatten "_") }
        /// </code></example>
        val options: TableOptions
        
        /// <summary>Options for the common case: a flat record with one key column.</summary>
        /// <example><code lang="fsharp">
        /// Mapping.keyedBy "id"
        /// </code></example>
        val keyedBy: column: string -> TableOptions
        
        /// <summary>What went wrong, in a sentence.</summary>
        /// <example><code lang="fsharp">
        /// MappingError.NestedNotDecided "address" |> Mapping.describe
        /// // "'address' is a nested object, and the options did not say ..."
        /// </code></example>
        val describe: error: MappingError -> string
        
        /// <summary>Every mapping problem, one per line.</summary>
        /// <example><code lang="fsharp">
        /// match Mapping.tableOf opts schema with
        /// | Error problems -> eprintfn "%s" (Mapping.report problems)
        /// | Ok table -> ()
        /// </code></example>
        val report: errors: MappingError list -> string
        
        val private lengthOf: constraints: Constraint list -> int option
        
        /// The type a leaf becomes. A string with a maximum length becomes VARCHAR of
        /// that length, so the constraint the schema already carries turns into one
        /// the database enforces rather than being restated.
        val private typeOf: info: SchemaInfo -> SqlType
        
        val private columnName: separator: string -> path: string list -> string
        
        /// Produces the columns for one field, or the reasons it could not.
        val private columnsFor:
          opts: TableOptions ->
            prefix: string list ->
            required: bool ->
            info: SchemaInfo -> Result<Column list,MappingError list>
        
        /// <summary>
        /// Derives a table from a schema, or reports every decision that was not made.
        /// </summary>
        /// <remarks>
        /// A schema describes a value on a wire; a table describes a row in a
        /// database. Where the two do not line up, this returns errors rather than
        /// choosing, because the choice is one you have to live with and a wrong
        /// default would be discovered in production.
        /// </remarks>
        /// <example><code lang="fsharp">
        /// Mapping.tableOf (Mapping.keyedBy "id") personSchema
        ///
        /// // With a nested address and no strategy:
        /// // Error [ NestedNotDecided "address" ]
        /// // "'address' is a nested object, and the options did not say what to do
        /// //  with it. It could be flattened into columns, stored as JSON, or
        /// //  omitted. Set TableOptions.Nested."
        /// </code></example>
        val tableOf:
          opts: TableOptions ->
            schema: Schema<'T> -> Result<Table,MappingError list>

namespace Etymon
    
    /// <summary>
    /// The parts of SQL generation that differ between databases.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A record of functions rather than an interface, for the same reason the rest
    /// of the suite uses records: a dialect is data you can inspect, extend and test
    /// a piece at a time.
    /// </para>
    /// <para>
    /// The capability flags are not decoration. SQLite cannot change a column's type
    /// or add a constraint to an existing table, and the honest response is to refuse
    /// to generate the statement and say why, rather than emitting SQL that fails at
    /// three in the morning.
    /// </para>
    /// </remarks>
    [<NoEquality; NoComparison>]
    type SqlDialect =
        {
          
          /// What to call this dialect in a comment at the top of a script.
          Name: string
          
          /// Quotes an identifier so that a reserved word or odd character is safe.
          Quote: (string -> string)
          
          /// The dialect's name for a column type.
          TypeName: (SqlType -> string)
          
          /// A CHECK expression for a constraint on a column, or none when the
          /// dialect cannot express it. Returning none is how a rule that cannot
          /// become a database constraint stays documented rather than pretended.
          CheckExpression: (string -> Constraint -> string option)
          
          /// Whether ALTER TABLE can change a column's type.
          CanAlterColumnType: bool
          
          /// Whether ALTER TABLE can drop a column.
          CanDropColumn: bool
          
          /// Whether ALTER TABLE can change a column between null and not null.
          CanAlterNullability: bool
        }
    
    /// The dialects Etymon ships with.
    [<RequireQualifiedAccess>]
    module Dialect =
        
        val private literal: value: string -> string
        
        val private numText: n: Num -> string
        
        /// Constraints that every SQL database can express the same way. Length and
        /// range become CHECK clauses; a pattern or a named format does not, because
        /// regular-expression support differs too much to generate something that
        /// would mean the same thing everywhere.
        val private portableCheck:
          quote: (string -> string) ->
            column: string -> c: Constraint -> string option
        
        /// <summary>
        /// PostgreSQL.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Migrations.script Dialect.postgres changes
        /// </code></example>
        val postgres: SqlDialect
        
        /// <summary>
        /// SQLite.
        /// </summary>
        /// <remarks>
        /// SQLite cannot change a column's type or its nullability after the fact:
        /// doing so means rebuilding the table, copying the data and swapping it in.
        /// That is twelve steps the official documentation sets out, it depends on
        /// data Etymon has never seen, and generating it blind would be worse than
        /// refusing. So those changes are reported as unsupported instead.
        /// </remarks>
        /// <example><code lang="fsharp">
        /// Migrations.script Dialect.sqlite changes
        /// </code></example>
        val sqlite: SqlDialect

namespace Etymon
    
    /// <summary>
    /// One difference between two snapshots.
    /// </summary>
    /// <remarks>
    /// Kept as data rather than generated straight to SQL, so that a change can be
    /// examined — is it destructive? is it reversible? — before anything decides
    /// what to write.
    /// </remarks>
    [<RequireQualifiedAccess>]
    type Change =
        
        /// A table that did not exist before.
        | CreateTable of table: Table
        
        /// A table that no longer exists. Destructive.
        | DropTable of table: Table
        
        /// A column added to an existing table.
        | AddColumn of table: string * column: Column
        
        /// A column removed from an existing table. Destructive.
        | DropColumn of table: string * column: Column
        
        /// A column whose type changed. Destructive when the new type is narrower.
        | AlterColumnType of
          table: string * column: string * before: SqlType * after: SqlType
        
        /// A column that stopped accepting nulls. Destructive: existing rows may
        /// hold one.
        | MakeNotNull of table: string * column: string
        
        /// A column that started accepting nulls.
        | MakeNullable of table: string * column: string
        
        /// A uniqueness rule added. Destructive: existing rows may already break it.
        | AddUnique of table: string * constraint': UniqueConstraint
        
        /// A uniqueness rule removed.
        | DropUnique of table: string * constraint': UniqueConstraint
        
        /// <summary>
        /// The two snapshots disagree about the rules on a column, and this version
        /// cannot express a constraint change as SQL.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Emitted so that the gap is loud. A changed <c>CHECK</c> used to produce no
        /// change at all, which meant the generated migration was silently
        /// incomplete: the reviewer saw a script that looked finished, and the
        /// database went on enforcing the old rule.
        /// </para>
        /// <para>
        /// A refusal is worse than a correct answer and far better than silence.
        /// </para>
        /// </remarks>
        | ConstraintsDiffer of
          table: string * column: string * before: Constraint list *
          after: Constraint list
    
    /// Comparing snapshots.
    [<RequireQualifiedAccess>]
    module Diff =
        
        /// <summary>
        /// Whether applying a change can lose data or fail on data that already
        /// exists.
        /// </summary>
        /// <remarks>
        /// Etymon errs toward calling a change destructive. The cost of an
        /// unnecessary review is a minute; the cost of a silent truncation is a
        /// support ticket a month later that nobody connects to a migration.
        /// </remarks>
        /// <example><code lang="fsharp">
        /// Diff.isDestructive (Change.DropColumn("person", column)) // true
        /// Diff.isDestructive (Change.AddColumn("person", column)) // false
        /// </code></example>
        val isDestructive: change: Change -> bool
        
        /// <summary>What a change does, in a sentence.</summary>
        /// <example><code lang="fsharp">
        /// Diff.describe (Change.DropColumn("person", column))
        /// // "drop the column person.nickname, losing whatever it holds"
        /// </code></example>
        val describe: change: Change -> string
        
        /// <summary>
        /// The change that undoes another, where one exists.
        /// </summary>
        /// <remarks>
        /// Only changes that lose nothing can be reversed. Dropping a table is not
        /// reversible by recreating it: the rows are gone, and a script that
        /// pretended otherwise would be worse than one that admits it cannot help.
        /// </remarks>
        /// <example><code lang="fsharp">
        /// Diff.invert (Change.AddColumn("person", column)) // Some (DropColumn ...)
        /// Diff.invert (Change.DropTable table) // None -- the rows are gone
        /// </code></example>
        val invert: change: Change -> Change option
        
        val private sameName: a: string -> b: string -> bool
        
        /// <summary>
        /// Every difference between two snapshots, in an order that can be applied.
        /// </summary>
        /// <remarks>
        /// Tables are created before columns are added to them, and dropped last, so
        /// that the list can be executed from top to bottom without reordering.
        /// </remarks>
        /// <example><code lang="fsharp">
        /// Diff.plan previousSnapshot currentSnapshot
        /// </code></example>
        val plan: before: Snapshot -> after: Snapshot -> Change list

namespace Etymon
    
    /// <summary>
    /// A generated migration.
    /// </summary>
    /// <remarks>
    /// <c>Down</c> is absent rather than wrong when the change cannot be undone.
    /// A script that claimed to reverse a dropped table would recreate its shape and
    /// none of its rows, which is worse than admitting it cannot help.
    /// </remarks>
    type Migration =
        {
          
          /// The statements that apply the change.
          Up: string
          
          /// The statements that undo it, when every change is reversible.
          Down: string option
          
          /// The changes that can lose data or fail against existing rows. Listed
          /// so that a reviewer sees them without reading the SQL.
          Destructive: Change list
          
          /// Changes the dialect cannot express, with the reason.
          Unsupported: (Change * string) list
        }
    
    /// How a migration script is written.
    type ScriptOptions =
        {
          
          /// Whether to emit destructive statements as SQL. When false — the
          /// default — they are written as comments, so that applying the script
          /// cannot destroy anything nobody read.
          IncludeDestructive: bool
          
          /// Whether to wrap the script in a transaction. PostgreSQL can run DDL
          /// transactionally; not every database can.
          Transactional: bool
        }
    
    /// <summary>
    /// Deriving a relational model from a schema, diffing it, and writing SQL.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This package generates SQL text and executes nothing. It has no database
    /// driver and no NuGet dependency at all, which means a project can generate and
    /// review migrations without taking on Npgsql, and the same model can be
    /// rendered for more than one database.
    /// </para>
    /// <para>
    /// Snapshots are meant to be committed. A migration that exists as a file in a
    /// pull request is one a colleague can object to; a migration that exists only
    /// as something that happened to a database at four in the morning is not.
    /// </para>
    /// </remarks>
    [<RequireQualifiedAccess>]
    module Migrations =
        
        /// <summary>
        /// The default: destructive statements are written as comments, and the
        /// script runs in a transaction.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Migrations.script Migrations.scriptOptions Dialect.postgres changes
        /// </code></example>
        val scriptOptions: ScriptOptions
        
        val private checkName:
          table: string -> column: string -> index: int -> string
        
        val private columnDefinition:
          dialect: SqlDialect -> column: Column -> string
        
        val private tableChecks:
          dialect: SqlDialect -> table: Table -> string list
        
        val private createTable: dialect: SqlDialect -> table: Table -> string
        
        /// The statements for one change, or the reason the dialect cannot express it.
        val private render:
          dialect: SqlDialect -> change: Change -> Result<string list,string>
        
        val private renderAll:
          dialect: SqlDialect ->
            opts: ScriptOptions ->
            changes: Change list -> string * (Change * string) list
        
        /// <summary>
        /// Writes a migration for a list of changes.
        /// </summary>
        /// <remarks>
        /// Destructive statements are commented out unless
        /// <c>IncludeDestructive</c> says otherwise, and every change is preceded by
        /// a plain-English comment saying what it does. A migration is read more
        /// often than it is written.
        /// </remarks>
        /// <example><code lang="fsharp">
        /// let migration = Migrations.script Migrations.scriptOptions Dialect.postgres changes
        /// printfn "%s" migration.Up
        /// for change in migration.Destructive do
        ///     eprintfn "needs review: %s" (Diff.describe change)
        /// </code></example>
        val script:
          opts: ScriptOptions ->
            dialect: SqlDialect -> changes: Change list -> Migration
        
        /// <summary>
        /// The whole pipeline: diff two snapshots and write the migration.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Migrations.between Dialect.postgres previous current
        /// </code></example>
        val between:
          dialect: SqlDialect ->
            before: Snapshot -> after: Snapshot -> Migration
        
        val private sqlTypeSchema: Schema<SqlType>
        
        val private columnSchema: Schema<Column>
        
        val private uniqueSchema: Schema<UniqueConstraint>
        
        val private foreignKeySchema: Schema<ForeignKey>
        
        val private tableSchema: Schema<Table>
        
        /// <summary>
        /// The schema for a snapshot file.
        /// </summary>
        /// <remarks>
        /// Etymon describes its own migration format with its own schema type, which
        /// means the format is documented, validated and round-trip-tested by the
        /// same machinery as everything else — and that a malformed snapshot is
        /// reported with a path rather than a stack trace.
        /// </remarks>
        /// <example><code lang="fsharp">
        /// OpenApi.toJsonSchemaText Migrations.snapshotSchema // the snapshot format, documented
        /// </code></example>
        val snapshotSchema: Schema<Snapshot>
        
        /// <summary>A snapshot as indented JSON, ready to commit.</summary>
        /// <example><code lang="fsharp">
        /// File.tryWriteAllText "migrations/0003.snapshot.json" (Migrations.toJson snapshot)
        /// </code></example>
        val toJson: snapshot: Snapshot -> string
        
        /// <summary>
        /// A snapshot read back from JSON, reporting every problem with a path.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Migrations.fromJson text // Error [ "tables[0].columns[2].name: is required" ]
        /// </code></example>
        val fromJson: text: string -> Validation<Snapshot>

