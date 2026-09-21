namespace Etymon

open System
open System.Text

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
    let scriptOptions =
        {
            IncludeDestructive = false
            Transactional = true
        }

    // ---- rendering -----------------------------------------------------------

    let private checkName (table: string) (column: string) (index: int) = $"ck_%s{table}_%s{column}_%d{index}"

    let private columnDefinition (dialect: SqlDialect) (column: Column) =
        let builder = StringBuilder()

        builder.Append(dialect.Quote column.Name).Append(' ').Append(dialect.TypeName column.Type)
        |> ignore

        if not column.Nullable then
            builder.Append " NOT NULL" |> ignore

        match column.Default with
        | Some value -> builder.Append(" DEFAULT ").Append(value) |> ignore
        | None -> ()

        builder.ToString()

    let private tableChecks (dialect: SqlDialect) (table: Table) =
        table.Columns
        |> List.collect (fun column ->
            column.Constraints
            |> List.mapi (fun i c -> i, c)
            |> List.choose (fun (i, c) ->
                dialect.CheckExpression column.Name c
                |> Option.map (fun expression ->
                    $"CONSTRAINT %s{dialect.Quote(checkName table.Name column.Name i)} CHECK (%s{expression})"
                )
            )
        )

    let private createTable (dialect: SqlDialect) (table: Table) =
        let parts =
            [
                yield! table.Columns |> List.map (columnDefinition dialect)

                if not (List.isEmpty table.PrimaryKey) then
                    let columns = table.PrimaryKey |> List.map dialect.Quote |> String.concat ", "
                    let keyName = dialect.Quote("pk_" + table.Name)
                    yield $"CONSTRAINT %s{keyName} PRIMARY KEY (%s{columns})"

                for unique in table.Unique do
                    let columns = unique.Columns |> List.map dialect.Quote |> String.concat ", "
                    yield $"CONSTRAINT %s{dialect.Quote unique.Name} UNIQUE (%s{columns})"

                for key in table.ForeignKeys do
                    let columns = key.Columns |> List.map dialect.Quote |> String.concat ", "
                    let target = key.ReferencesColumns |> List.map dialect.Quote |> String.concat ", "

                    yield
                        $"CONSTRAINT %s{dialect.Quote key.Name} FOREIGN KEY (%s{columns}) REFERENCES %s{dialect.Quote key.ReferencesTable} (%s{target})"

                yield! tableChecks dialect table
            ]

        let body =
            parts
            |> List.map (fun p -> "    " + p)
            |> String.concat ("," + Environment.NewLine)

        $"CREATE TABLE %s{dialect.Quote table.Name} ("
        + Environment.NewLine
        + body
        + Environment.NewLine
        + ");"

    /// The statements for one change, or the reason the dialect cannot express it.
    let private render (dialect: SqlDialect) (change: Change) : Result<string list, string> =
        match change with
        | Change.CreateTable table -> Ok [ createTable dialect table ]
        | Change.DropTable table -> Ok [ $"DROP TABLE %s{dialect.Quote table.Name};" ]

        // Refused rather than skipped. This version cannot name the constraint
        // it would have to drop, so it cannot write the change -- but a reviewer
        // who is told nothing will ship a script that leaves the old rule in
        // place, and that is the failure mode worth being noisy about.
        | Change.ConstraintsDiffer(table, column, before, after) ->
            let render (constraints: Constraint list) =
                match constraints with
                | [] -> "no rules"
                | _ -> String.Join("; ", constraints |> List.map Constraint.describe)

            Error
                $"the rules on %s{table}.%s{column} differ between the two snapshots, and this version cannot express a constraint change. Before: %s{render before}. After: %s{render after}. Write the ALTER by hand, or recreate the constraint."

        | Change.AddColumn(table, column) ->
            // A NOT NULL column cannot simply be added to a table that has rows,
            // unless it has a default. Saying so is more use than a statement
            // that fails on deployment.
            if not column.Nullable && column.Default.IsNone then
                Error
                    $"adding %s{table}.%s{column.Name} as NOT NULL needs a default, or existing rows have nothing to put in it. Give the column a default, or add it nullable and tighten it in a later migration."
            else
                Ok
                    [
                        $"ALTER TABLE %s{dialect.Quote table} ADD COLUMN %s{columnDefinition dialect column};"
                    ]

        | Change.DropColumn(table, column) ->
            if dialect.CanDropColumn then
                Ok
                    [
                        $"ALTER TABLE %s{dialect.Quote table} DROP COLUMN %s{dialect.Quote column.Name};"
                    ]
            else
                Error $"%s{dialect.Name} cannot drop a column."

        | Change.AlterColumnType(table, column, before, after) ->
            if dialect.CanAlterColumnType then
                Ok
                    [
                        $"ALTER TABLE %s{dialect.Quote table} ALTER COLUMN %s{dialect.Quote column} TYPE %s{dialect.TypeName after};"
                    ]
            else
                Error
                    $"%s{dialect.Name} cannot change a column's type. Changing %s{table}.%s{column} from %A{before} to %A{after} means rebuilding the table, copying the data and swapping it in -- which depends on data Etymon has never seen, so it will not guess at the statements."

        | Change.MakeNotNull(table, column) ->
            if dialect.CanAlterNullability then
                Ok
                    [
                        $"ALTER TABLE %s{dialect.Quote table} ALTER COLUMN %s{dialect.Quote column} SET NOT NULL;"
                    ]
            else
                Error $"%s{dialect.Name} cannot change a column's nullability after the table exists."

        | Change.MakeNullable(table, column) ->
            if dialect.CanAlterNullability then
                Ok
                    [
                        $"ALTER TABLE %s{dialect.Quote table} ALTER COLUMN %s{dialect.Quote column} DROP NOT NULL;"
                    ]
            else
                Error $"%s{dialect.Name} cannot change a column's nullability after the table exists."

        | Change.AddUnique(table, unique) ->
            let columns = unique.Columns |> List.map dialect.Quote |> String.concat ", "

            Ok
                [
                    $"ALTER TABLE %s{dialect.Quote table} ADD CONSTRAINT %s{dialect.Quote unique.Name} UNIQUE (%s{columns});"
                ]

        | Change.DropUnique(table, unique) ->
            Ok
                [
                    $"ALTER TABLE %s{dialect.Quote table} DROP CONSTRAINT %s{dialect.Quote unique.Name};"
                ]

    let private renderAll (dialect: SqlDialect) (opts: ScriptOptions) (changes: Change list) =
        let builder = StringBuilder()
        let mutable unsupported = []

        builder.AppendLine($"-- Generated by Etymon for %s{dialect.Name}.") |> ignore

        if opts.Transactional then
            builder.AppendLine("BEGIN;").AppendLine() |> ignore

        for change in changes do
            let destructive = Diff.isDestructive change

            builder.AppendLine($"-- %s{Diff.describe change}") |> ignore

            match render dialect change with
            | Error reason ->
                unsupported <- unsupported @ [ change, reason ]

                builder.AppendLine($"-- NOT GENERATED: %s{reason}").AppendLine() |> ignore

            | Ok statements ->
                if destructive && not opts.IncludeDestructive then
                    // Commented out on purpose. A destructive statement that runs
                    // because nobody read it is the failure this whole package
                    // exists to prevent.
                    builder.AppendLine("-- DESTRUCTIVE: review, then uncomment to apply.") |> ignore

                    for statement in statements do
                        for line in statement.Split Environment.NewLine do
                            builder.AppendLine("-- " + line) |> ignore
                else
                    for statement in statements do
                        builder.AppendLine statement |> ignore

                builder.AppendLine() |> ignore

        if opts.Transactional then
            builder.AppendLine "COMMIT;" |> ignore

        builder.ToString().TrimEnd() + Environment.NewLine, unsupported

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
    let script (opts: ScriptOptions) (dialect: SqlDialect) (changes: Change list) : Migration =
        let up, unsupported = renderAll dialect opts changes
        let inverted = changes |> List.rev |> List.map Diff.invert

        let down =
            if inverted |> List.forall Option.isSome then
                let downChanges = inverted |> List.choose id
                let text, _ = renderAll dialect opts downChanges
                Some text
            else
                // One irreversible change makes the whole script irreversible.
                // A partial down migration is a trap.
                None

        {
            Up = up
            Down = down
            Destructive = changes |> List.filter Diff.isDestructive
            Unsupported = unsupported
        }

    /// <summary>
    /// The whole pipeline: diff two snapshots and write the migration.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Migrations.between Dialect.postgres previous current
    /// </code></example>
    let between (dialect: SqlDialect) (before: Snapshot) (after: Snapshot) =
        script scriptOptions dialect (Diff.plan before after)

    // ---- snapshots on disk ---------------------------------------------------

    let private sqlTypeSchema: Schema<SqlType> =
        Schema.union
            "SqlType"
            "kind"
            [
                Schema.caseUnit
                    "text"
                    SqlType.Text
                    (function
                    | SqlType.Text -> true
                    | _ -> false
                    )
                Schema.case
                    "varchar"
                    Schema.int
                    SqlType.VarChar
                    (function
                    | SqlType.VarChar n -> ValueSome n
                    | _ -> ValueNone
                    )
                Schema.caseUnit
                    "integer"
                    SqlType.Integer
                    (function
                    | SqlType.Integer -> true
                    | _ -> false
                    )
                Schema.caseUnit
                    "bigint"
                    SqlType.BigInt
                    (function
                    | SqlType.BigInt -> true
                    | _ -> false
                    )
                Schema.case
                    "numeric"
                    (Schema.object "Numeric" {
                        let! precision = Schema.required "precision" Schema.int fst
                        and! scale = Schema.required "scale" Schema.int snd
                        return (precision, scale)
                    })
                    SqlType.Numeric
                    (function
                    | SqlType.Numeric(p, s) -> ValueSome(p, s)
                    | _ -> ValueNone
                    )
                Schema.caseUnit
                    "double"
                    SqlType.Double
                    (function
                    | SqlType.Double -> true
                    | _ -> false
                    )
                Schema.caseUnit
                    "boolean"
                    SqlType.Boolean
                    (function
                    | SqlType.Boolean -> true
                    | _ -> false
                    )
                Schema.caseUnit
                    "uuid"
                    SqlType.Uuid
                    (function
                    | SqlType.Uuid -> true
                    | _ -> false
                    )
                Schema.caseUnit
                    "timestamp"
                    SqlType.Timestamp
                    (function
                    | SqlType.Timestamp -> true
                    | _ -> false
                    )
                Schema.caseUnit
                    "date"
                    SqlType.Date
                    (function
                    | SqlType.Date -> true
                    | _ -> false
                    )
                Schema.caseUnit
                    "time"
                    SqlType.Time
                    (function
                    | SqlType.Time -> true
                    | _ -> false
                    )
                Schema.caseUnit
                    "interval"
                    SqlType.Interval
                    (function
                    | SqlType.Interval -> true
                    | _ -> false
                    )
                Schema.caseUnit
                    "binary"
                    SqlType.Binary
                    (function
                    | SqlType.Binary -> true
                    | _ -> false
                    )
                Schema.caseUnit
                    "json"
                    SqlType.Json
                    (function
                    | SqlType.Json -> true
                    | _ -> false
                    )
            ]

    let private columnSchema: Schema<Column> =
        Schema.object "Column" {
            let! name = Schema.required "name" Schema.string (fun c -> c.Name)
            and! columnType = Schema.required "type" sqlTypeSchema (fun c -> c.Type)
            and! nullable = Schema.required "nullable" Schema.bool (fun c -> c.Nullable)
            and! defaultValue = Schema.optional "default" Schema.string (fun c -> c.Default)

            and! constraints =
                Schema.defaulted "constraints" (Schema.list ConstraintCodec.schema) [] (fun c -> c.Constraints)

            return
                {
                    Name = name
                    Type = columnType
                    Nullable = nullable
                    Default = defaultValue
                    Constraints = constraints
                }
        }

    let private uniqueSchema: Schema<UniqueConstraint> =
        Schema.object "UniqueConstraint" {
            let! name = Schema.required "name" Schema.string (fun u -> u.Name)

            and! columns =
                Schema.required "columns" (Schema.list Schema.string) (fun u -> u.Columns)

            return { Name = name; Columns = columns }
        }

    let private foreignKeySchema: Schema<ForeignKey> =
        Schema.object "ForeignKey" {
            let! name = Schema.required "name" Schema.string (fun f -> f.Name)

            and! columns =
                Schema.required "columns" (Schema.list Schema.string) (fun f -> f.Columns)

            and! referencesTable =
                Schema.required "referencesTable" Schema.string (fun f -> f.ReferencesTable)

            and! referencesColumns =
                Schema.required "referencesColumns" (Schema.list Schema.string) (fun f -> f.ReferencesColumns)

            return
                {
                    Name = name
                    Columns = columns
                    ReferencesTable = referencesTable
                    ReferencesColumns = referencesColumns
                }
        }

    let private tableSchema: Schema<Table> =
        Schema.object "Table" {
            let! name = Schema.required "name" Schema.string (fun t -> t.Name)

            and! columns =
                Schema.required "columns" (Schema.list columnSchema) (fun t -> t.Columns)

            and! primaryKey =
                Schema.required "primaryKey" (Schema.list Schema.string) (fun t -> t.PrimaryKey)

            and! unique =
                Schema.defaulted "unique" (Schema.list uniqueSchema) [] (fun t -> t.Unique)

            and! foreignKeys =
                Schema.defaulted "foreignKeys" (Schema.list foreignKeySchema) [] (fun t -> t.ForeignKeys)

            return
                {
                    Name = name
                    Columns = columns
                    PrimaryKey = primaryKey
                    Unique = unique
                    ForeignKeys = foreignKeys
                }
        }

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
    let snapshotSchema: Schema<Snapshot> =
        Schema.object "Snapshot" {
            let! formatVersion =
                Schema.required "formatVersion" Schema.int (fun s -> s.FormatVersion)

            and! tables = Schema.required "tables" (Schema.list tableSchema) (fun s -> s.Tables)

            return
                {
                    FormatVersion = formatVersion
                    Tables = tables
                }
        }

    /// <summary>A snapshot as indented JSON, ready to commit.</summary>
    /// <example><code lang="fsharp">
    /// File.tryWriteAllText "migrations/0003.snapshot.json" (Migrations.toJson snapshot)
    /// </code></example>
    let toJson (snapshot: Snapshot) =
        Schema.toJsonIndented snapshotSchema snapshot

    /// <summary>
    /// A snapshot read back from JSON, reporting every problem with a path.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Migrations.fromJson text // Error [ "tables[0].columns[2].name: is required" ]
    /// </code></example>
    let fromJson (text: string) : Validation<Snapshot> = Schema.fromJson snapshotSchema text
