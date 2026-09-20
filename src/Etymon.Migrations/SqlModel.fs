namespace Etymon

/// <summary>
/// The type of a column, named abstractly so that one model can describe a table
/// in more than one dialect.
/// </summary>
/// <remarks>
/// Deliberately small. Every case here is one that Etymon can derive from a
/// schema and that every supported dialect can express. A type that only one
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
/// A relational schema at a point in time.
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
    let formatVersion = 1

    /// <summary>An empty snapshot: what a database looks like before anything exists.</summary>
    /// <example><code lang="fsharp">
    /// Migrations.plan SqlModel.emptySnapshot current // every table is created
    /// </code></example>
    let emptySnapshot =
        {
            FormatVersion = formatVersion
            Tables = []
        }

    /// <summary>A snapshot of some tables, in a stable order.</summary>
    /// <example><code lang="fsharp">
    /// SqlModel.snapshotOf [ personTable; addressTable ]
    /// </code></example>
    let snapshotOf (tables: Table list) =
        {
            FormatVersion = formatVersion
            Tables = tables |> List.sortBy (fun t -> t.Name)
        }

    /// <summary>A column by name, if the table has one.</summary>
    /// <example><code lang="fsharp">
    /// SqlModel.tryColumn "email" personTable
    /// </code></example>
    let tryColumn (name: string) (table: Table) =
        table.Columns
        |> List.tryFind (fun c -> System.String.Equals(c.Name, name, System.StringComparison.OrdinalIgnoreCase))

    /// <summary>A table by name, if the snapshot has one.</summary>
    /// <example><code lang="fsharp">
    /// SqlModel.tryTable "person" snapshot
    /// </code></example>
    let tryTable (name: string) (snapshot: Snapshot) =
        snapshot.Tables
        |> List.tryFind (fun t -> System.String.Equals(t.Name, name, System.StringComparison.OrdinalIgnoreCase))

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
    let narrows (before: SqlType) (after: SqlType) =
        match before, after with
        | a, b when a = b -> false
        | SqlType.VarChar a, SqlType.VarChar b -> b < a
        | SqlType.VarChar _, SqlType.Text -> false
        | SqlType.Text, SqlType.VarChar _ -> true
        | SqlType.Integer, SqlType.BigInt -> false
        | SqlType.BigInt, SqlType.Integer -> true
        | SqlType.Numeric(p1, s1), SqlType.Numeric(p2, s2) -> p2 < p1 || s2 < s1
        // Anything else is a change between unrelated types, which no database
        // converts in a way Etymon is willing to guess at.
        | _ -> true
