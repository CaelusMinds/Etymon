namespace Etymon

open System
open System.Globalization

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
        Quote: string -> string
        /// The dialect's name for a column type.
        TypeName: SqlType -> string
        /// A CHECK expression for a constraint on a column, or none when the
        /// dialect cannot express it. Returning none is how a rule that cannot
        /// become a database constraint stays documented rather than pretended.
        CheckExpression: string -> Constraint -> string option
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

    let private literal (value: string) = "'" + value.Replace("'", "''") + "'"

    let private numText (n: Num) =
        match n with
        | Num.Int v -> v.ToString(CultureInfo.InvariantCulture)
        | Num.Dec v -> v.ToString(CultureInfo.InvariantCulture)
        | Num.Float v -> v.ToString("R", CultureInfo.InvariantCulture)

    /// Constraints that every SQL database can express the same way. Length and
    /// range become CHECK clauses; a pattern or a named format does not, because
    /// regular-expression support differs too much to generate something that
    /// would mean the same thing everywhere.
    let private portableCheck (quote: string -> string) (column: string) (c: Constraint) =
        let col = quote column

        match c with
        | Constraint.Length(min, max) ->
            let parts =
                [
                    match min with
                    | Some v when v > 0 -> $"length(%s{col}) >= %d{v}"
                    | _ -> ()
                    match max with
                    | Some v -> $"length(%s{col}) <= %d{v}"
                    | None -> ()
                ]

            if List.isEmpty parts then
                None
            else
                Some(String.Join(" AND ", parts))

        | Constraint.Range(min, max, exclusiveMin, exclusiveMax) ->
            // Hoisted, because F# will not allow a string literal inside an
            // interpolation in a single-quoted interpolated string.
            let lowOperator = if exclusiveMin then ">" else ">="
            let highOperator = if exclusiveMax then "<" else "<="

            let parts =
                [
                    match min with
                    | Some v -> $"%s{col} %s{lowOperator} %s{numText v}"
                    | None -> ()
                    match max with
                    | Some v -> $"%s{col} %s{highOperator} %s{numText v}"
                    | None -> ()
                ]

            if List.isEmpty parts then
                None
            else
                Some(String.Join(" AND ", parts))

        | Constraint.OneOf values when not (List.isEmpty values) ->
            Some(col + " IN (" + String.Join(", ", values |> List.map literal) + ")")

        | Constraint.Items(min, max, _) ->
            // A collection lives in a JSON column, and counting its elements is
            // dialect-specific enough not to be worth guessing at.
            ignore (min, max)
            None

        // A pattern needs a regular-expression operator, which PostgreSQL spells
        // ~ and SQLite does not have at all without an extension. A named format
        // has no SQL equivalent. An opaque rule is, by definition, not data.
        | Constraint.Pattern _
        | Constraint.HasFormat _
        | Constraint.Opaque _
        | Constraint.OneOf _
        | Constraint.Length _ -> None

    /// <summary>
    /// PostgreSQL.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Migrations.script Dialect.postgres changes
    /// </code></example>
    let postgres: SqlDialect =
        let quote (name: string) =
            "\"" + name.Replace("\"", "\"\"") + "\""

        {
            Name = "PostgreSQL"
            Quote = quote
            TypeName =
                fun t ->
                    match t with
                    | SqlType.Text -> "text"
                    | SqlType.VarChar length -> $"varchar(%d{length})"
                    | SqlType.Integer -> "integer"
                    | SqlType.BigInt -> "bigint"
                    | SqlType.Numeric(precision, scale) -> $"numeric(%d{precision}, %d{scale})"
                    | SqlType.Double -> "double precision"
                    | SqlType.Boolean -> "boolean"
                    | SqlType.Uuid -> "uuid"
                    | SqlType.Timestamp -> "timestamptz"
                    | SqlType.Date -> "date"
                    | SqlType.Time -> "time"
                    | SqlType.Interval -> "interval"
                    | SqlType.Binary -> "bytea"
                    | SqlType.Json -> "jsonb"
            CheckExpression =
                fun column c ->
                    match c with
                    // PostgreSQL has a regular-expression operator, so a pattern can
                    // become a real constraint here even though it cannot everywhere.
                    | Constraint.Pattern regex -> Some(quote column + " ~ " + literal regex)
                    | other -> portableCheck quote column other
            CanAlterColumnType = true
            CanDropColumn = true
            CanAlterNullability = true
        }

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
    let sqlite: SqlDialect =
        let quote (name: string) =
            "\"" + name.Replace("\"", "\"\"") + "\""

        {
            Name = "SQLite"
            Quote = quote
            TypeName =
                fun t ->
                    match t with
                    | SqlType.Text -> "TEXT"
                    | SqlType.VarChar length -> $"VARCHAR(%d{length})"
                    | SqlType.Integer -> "INTEGER"
                    | SqlType.BigInt -> "INTEGER"
                    | SqlType.Numeric(precision, scale) -> $"NUMERIC(%d{precision}, %d{scale})"
                    | SqlType.Double -> "REAL"
                    // SQLite has no boolean type; 0 and 1 in an INTEGER is the
                    // documented representation.
                    | SqlType.Boolean -> "INTEGER"
                    | SqlType.Uuid -> "TEXT"
                    | SqlType.Timestamp -> "TEXT"
                    | SqlType.Date -> "TEXT"
                    | SqlType.Time -> "TEXT"
                    | SqlType.Interval -> "TEXT"
                    | SqlType.Binary -> "BLOB"
                    | SqlType.Json -> "TEXT"
            CheckExpression = fun column c -> portableCheck quote column c
            CanAlterColumnType = false
            CanDropColumn = true
            CanAlterNullability = false
        }
